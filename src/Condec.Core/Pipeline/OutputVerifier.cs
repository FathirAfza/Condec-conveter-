// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Buffers;
using System.Security.Cryptography;

namespace Condec.Core.Pipeline;

/// <summary>
/// Reads the temporary output back from disk and compares it with the hashes taken while writing.
/// The file is flushed to disk before this runs, but the OS may still serve the reads from its cache.
/// </summary>
internal static class OutputVerifier
{
    /// <param name="onChunkVerified">Called with the chunk number (from 1) and the chunk count.</param>
    public static async Task VerifyChunksAsync(string path, WriteManifest manifest, Action<int, int> onChunkVerified, CancellationToken ct)
    {
        using var file = OpenForReading(path);
        if (file.Length != manifest.Length)
        {
            throw new VerificationFailedException(
                VerificationFailure.LengthMismatch,
                $"The file on disk has {file.Length} bytes, but {manifest.Length} were written.");
        }

        var buffer = ArrayPool<byte>.Shared.Rent(manifest.ChunkSize);
        try
        {
            for (var index = 0; index < manifest.ChunkCount; index++)
            {
                ct.ThrowIfCancellationRequested();

                var length = (int)Math.Min(manifest.ChunkSize, manifest.Length - ((long)index * manifest.ChunkSize));
                await file.ReadExactlyAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);

                if (!SHA256.HashData(buffer.AsSpan(0, length)).AsSpan().SequenceEqual(manifest.ChunkHashes[index]))
                {
                    throw new VerificationFailedException(
                        VerificationFailure.ChunkMismatch,
                        $"Chunk {index + 1} of {manifest.ChunkCount} doesn't match what was written.",
                        chunkNumber: index + 1);
                }

                onChunkVerified(index + 1, manifest.ChunkCount);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <param name="onProgress">Called with the fraction of the file hashed so far.</param>
    public static async Task VerifyFileHashAsync(string path, WriteManifest manifest, Action<double> onProgress, CancellationToken ct)
    {
        using var file = OpenForReading(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = ArrayPool<byte>.Shared.Rent(manifest.ChunkSize);
        long total = 0;
        try
        {
            int read;
            while ((read = await file.ReadAsync(buffer.AsMemory(0, manifest.ChunkSize), ct).ConfigureAwait(false)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
                total += read;
                onProgress(manifest.Length == 0 ? 1 : (double)total / manifest.Length);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (total != manifest.Length || !hash.GetHashAndReset().AsSpan().SequenceEqual(manifest.FileHash))
        {
            throw new VerificationFailedException(
                VerificationFailure.FileHashMismatch,
                "The SHA-256 of the file on disk doesn't match what was written.");
        }
    }

    private static FileStream OpenForReading(string path) => new(path, new FileStreamOptions
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        // Reads are already chunk-sized, so FileStream's own buffer would only add a copy.
        BufferSize = 0,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
    });
}

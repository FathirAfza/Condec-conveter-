using System.Security.Cryptography;

namespace Condec.Core.Pipeline;

/// <summary>SHA-256 of every fixed-size chunk plus SHA-256 of the whole data, fed incrementally.</summary>
internal sealed class ChunkHasher : IDisposable
{
    public const int DefaultChunkSize = 1024 * 1024;

    private readonly IncrementalHash _chunkHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly IncrementalHash _fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly List<byte[]> _chunkHashes = [];
    private int _bytesInChunk;
    private bool _completed;

    public ChunkHasher(int chunkSize = DefaultChunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        ChunkSize = chunkSize;
    }

    public int ChunkSize { get; }

    public long Length { get; private set; }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The hasher is already completed.");
        }

        _fileHash.AppendData(data);
        Length += data.Length;

        while (!data.IsEmpty)
        {
            var take = Math.Min(ChunkSize - _bytesInChunk, data.Length);
            _chunkHash.AppendData(data[..take]);
            _bytesInChunk += take;
            data = data[take..];

            if (_bytesInChunk == ChunkSize)
            {
                _chunkHashes.Add(_chunkHash.GetHashAndReset());
                _bytesInChunk = 0;
            }
        }
    }

    public WriteManifest Complete()
    {
        if (_completed)
        {
            throw new InvalidOperationException("The hasher is already completed.");
        }

        _completed = true;
        if (_bytesInChunk > 0)
        {
            _chunkHashes.Add(_chunkHash.GetHashAndReset());
            _bytesInChunk = 0;
        }

        return new WriteManifest(ChunkSize, Length, [.. _chunkHashes], _fileHash.GetHashAndReset());
    }

    public void Dispose()
    {
        _chunkHash.Dispose();
        _fileHash.Dispose();
    }
}

/// <summary>What was written, as hashed on the way to disk. The last chunk may be shorter than <see cref="ChunkSize"/>.</summary>
internal sealed record WriteManifest(int ChunkSize, long Length, IReadOnlyList<byte[]> ChunkHashes, byte[] FileHash)
{
    public int ChunkCount => ChunkHashes.Count;
}

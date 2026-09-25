// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Security.Cryptography;
using Condec.Core.Pipeline;

namespace Condec.Tests;

public class ChunkHashingStreamTests
{
    private const int ChunkSize = 64;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(ChunkSize - 1)]
    [InlineData(ChunkSize)]
    [InlineData(ChunkSize + 1)]
    [InlineData((ChunkSize * 3) + 17)]
    public void Manifest_MatchesIndependentHashesOfEveryChunk(int length)
    {
        var data = TestData.Bytes(length);
        using var inner = new MemoryStream();
        using var hasher = new ChunkHasher(ChunkSize);
        using (var stream = new ChunkHashingStream(inner, hasher, TestContext.Current.CancellationToken))
        {
            // Uneven writes that straddle chunk boundaries.
            var offset = 0;
            foreach (var size in new[] { 5, 70, 1, 200, 3, 1000 })
            {
                var take = Math.Min(size, length - offset);
                stream.Write(data, offset, take);
                offset += take;
            }
        }

        var manifest = hasher.Complete();

        Assert.Equal(data, inner.ToArray());
        Assert.Equal(length, manifest.Length);
        Assert.Equal((length + ChunkSize - 1) / ChunkSize, manifest.ChunkCount);
        for (var i = 0; i < manifest.ChunkCount; i++)
        {
            var chunk = data.AsSpan(i * ChunkSize, Math.Min(ChunkSize, length - (i * ChunkSize)));
            Assert.Equal(SHA256.HashData(chunk), manifest.ChunkHashes[i]);
        }

        Assert.Equal(SHA256.HashData(data), manifest.FileHash);
    }

    [Fact]
    public void Stream_IsWriteOnlyAndForwardOnly()
    {
        using var hasher = new ChunkHasher(ChunkSize);
        using var stream = new ChunkHashingStream(new MemoryStream(), hasher, TestContext.Current.CancellationToken);
        stream.Write(TestData.Bytes(10));

        Assert.False(stream.CanSeek);
        Assert.False(stream.CanRead);
        Assert.True(stream.CanWrite);
        Assert.Equal(10, stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => stream.Position = 0);
        Assert.Throws<NotSupportedException>(() => stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        Assert.Throws<NotSupportedException>(() => stream.ReadByte());
    }

    [Fact]
    public async Task Write_AfterCancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        using var hasher = new ChunkHasher(ChunkSize);
        using var stream = new ChunkHashingStream(new MemoryStream(), hasher, cts.Token);

        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => stream.Write(TestData.Bytes(4)));
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await stream.WriteAsync(TestData.Bytes(4), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Dispose_LeavesFileAndHasherToThePipeline()
    {
        using var inner = new MemoryStream();
        using var hasher = new ChunkHasher(ChunkSize);
        var stream = new ChunkHashingStream(inner, hasher, TestContext.Current.CancellationToken);
        stream.Write(TestData.Bytes(100));

        stream.Dispose();

        Assert.False(stream.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => stream.Write(TestData.Bytes(1)));
        Assert.True(inner.CanWrite);
        Assert.Equal(2, hasher.Complete().ChunkCount);
    }
}

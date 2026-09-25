using Condec.Core.Pipeline;

namespace Condec.Tests;

public sealed class OutputVerifierTests : IDisposable
{
    private const int ChunkSize = 64;
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task VerifyChunks_IntactFile_ReportsEveryChunk()
    {
        var (path, manifest) = WriteFile(TestData.Bytes((ChunkSize * 4) + 10));
        var reported = new List<(int Number, int Count)>();

        await OutputVerifier.VerifyChunksAsync(path, manifest, (n, c) => reported.Add((n, c)), TestContext.Current.CancellationToken);

        Assert.Equal([(1, 5), (2, 5), (3, 5), (4, 5), (5, 5)], reported);
    }

    [Fact]
    public async Task VerifyChunks_CorruptedByte_NamesTheChunk()
    {
        var (path, manifest) = WriteFile(TestData.Bytes(ChunkSize * 4));
        FlipByte(path, (ChunkSize * 2) + 5);

        var ex = await Assert.ThrowsAsync<VerificationFailedException>(
            () => OutputVerifier.VerifyChunksAsync(path, manifest, (_, _) => { }, TestContext.Current.CancellationToken));

        Assert.Equal(VerificationFailure.ChunkMismatch, ex.Failure);
        Assert.Equal(3, ex.ChunkNumber);
    }

    [Fact]
    public async Task VerifyChunks_TruncatedFile_FailsOnLength()
    {
        var (path, manifest) = WriteFile(TestData.Bytes(ChunkSize * 2));
        using (var file = new FileStream(path, FileMode.Open))
        {
            file.SetLength(ChunkSize);
        }

        var ex = await Assert.ThrowsAsync<VerificationFailedException>(
            () => OutputVerifier.VerifyChunksAsync(path, manifest, (_, _) => { }, TestContext.Current.CancellationToken));

        Assert.Equal(VerificationFailure.LengthMismatch, ex.Failure);
    }

    [Fact]
    public async Task VerifyFileHash_IntactFile_ReachesFullProgress()
    {
        var (path, manifest) = WriteFile(TestData.Bytes((ChunkSize * 3) + 1));
        var fractions = new List<double>();

        await OutputVerifier.VerifyFileHashAsync(path, manifest, fractions.Add, TestContext.Current.CancellationToken);

        Assert.Equal(1, fractions[^1]);
    }

    [Fact]
    public async Task VerifyFileHash_CorruptedByte_Fails()
    {
        var (path, manifest) = WriteFile(TestData.Bytes(ChunkSize * 3));
        FlipByte(path, 0);

        var ex = await Assert.ThrowsAsync<VerificationFailedException>(
            () => OutputVerifier.VerifyFileHashAsync(path, manifest, _ => { }, TestContext.Current.CancellationToken));

        Assert.Equal(VerificationFailure.FileHashMismatch, ex.Failure);
    }

    [Fact]
    public async Task VerifyChunks_Cancelled_Throws()
    {
        var (path, manifest) = WriteFile(TestData.Bytes(ChunkSize * 3));
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OutputVerifier.VerifyChunksAsync(path, manifest, (_, _) => cts.Cancel(), cts.Token));
    }

    private (string Path, WriteManifest Manifest) WriteFile(byte[] data)
    {
        var path = _dir.File("output.bin");
        File.WriteAllBytes(path, data);
        using var hasher = new ChunkHasher(ChunkSize);
        hasher.Append(data);
        return (path, hasher.Complete());
    }

    private static void FlipByte(string path, int offset)
    {
        var bytes = File.ReadAllBytes(path);
        bytes[offset] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
    }
}

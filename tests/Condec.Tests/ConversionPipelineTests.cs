// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Security.Cryptography;
using Condec.Core.Conversion;
using Condec.Core.Pipeline;

namespace Condec.Tests;

public sealed class ConversionPipelineTests : IDisposable
{
    private const int ChunkSize = 64;
    private readonly TempDirectory _dir = new();
    private readonly string _source;
    private readonly string _destination;
    private readonly string _journalDirectory;
    private readonly byte[] _payload = TestData.Bytes((ChunkSize * 4) + 44);

    public ConversionPipelineTests()
    {
        _source = _dir.File("laporan.docx");
        File.WriteAllBytes(_source, TestData.Bytes(32, seed: 2));
        _destination = _dir.File("laporan.pdf");
        _journalDirectory = _dir.File("journal");
    }

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ConversionJob Job => new(_source, ".pdf", _destination);

    [Fact]
    public async Task Success_SavesVerifiedOutputAndCleansUp()
    {
        var pipeline = CreatePipeline(Writing(_payload));

        var result = await pipeline.RunAsync(Job, null, Ct);

        Assert.Equal(_payload, File.ReadAllBytes(_destination));
        Assert.Equal(_destination, result.OutputPath);
        Assert.Equal(_payload.Length, result.Length);
        Assert.Equal(5, result.ChunkCount);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(_payload)), result.Sha256);
        AssertNoLeftovers();
    }

    [Fact]
    public async Task Progress_MovesThroughTheFourStagesInOrder()
    {
        var reports = new List<PipelineProgress>();
        var pipeline = CreatePipeline(Writing(_payload));

        await pipeline.RunAsync(Job, new SyncProgress<PipelineProgress>(reports.Add), Ct);

        Assert.Equal(PipelineStage.Decode, reports[0].Stage);
        Assert.Equal(0, reports[0].OverallFraction);
        Assert.Equal(1, reports[^1].OverallFraction);
        Assert.Equal(PipelineStage.VerifyIntegrity, reports[^1].Stage);

        for (var i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i].Stage >= reports[i - 1].Stage, $"Stage went back at report {i}.");
            Assert.True(reports[i].OverallFraction >= reports[i - 1].OverallFraction, $"Progress went back at report {i}.");
        }

        Assert.Contains(reports, r => r.Stage == PipelineStage.Decode && r.Detail == "decoded");
        var chunkReports = reports.Where(r => r.Stage == PipelineStage.VerifyChunks && r.ChunkNumber > 0).ToList();
        Assert.Equal([1, 2, 3, 4, 5], chunkReports.Select(r => r.ChunkNumber));
        Assert.All(chunkReports, r => Assert.Equal(5, r.ChunkCount));
        Assert.Equal(0.90, chunkReports[^1].OverallFraction, precision: 10);
    }

    [Fact]
    public async Task Output_IsWrittenToTempFileNextToDestination_UntilVerified()
    {
        string? seenPath = null;
        var validator = new FakeValidator(".pdf", path =>
        {
            seenPath = path;
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(_destination));
            Assert.Equal(_payload, File.ReadAllBytes(path));
            return Task.CompletedTask;
        });

        await CreatePipeline(Writing(_payload), validator).RunAsync(Job, null, Ct);

        Assert.Equal(_destination + ".condec-tmp", seenPath);
    }

    [Fact]
    public async Task ConverterFailure_LeavesNothingBehind_AndKeepsExistingDestination()
    {
        File.WriteAllText(_destination, "versi lama");
        var pipeline = CreatePipeline(async (request, _, ct) =>
        {
            await request.Output.WriteAsync(_payload.AsMemory(0, 100), ct);
            throw new InvalidDataException("rusak");
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => pipeline.RunAsync(Job, null, Ct));

        Assert.Equal("versi lama", File.ReadAllText(_destination));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task CancelDuringEncode_LeavesNothingBehind()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pipeline = CreatePipeline(async (request, _, ct) =>
        {
            await request.Output.WriteAsync(_payload.AsMemory(0, 100), ct);
            await cts.CancelAsync();
            await request.Output.WriteAsync(_payload.AsMemory(100), ct);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.RunAsync(Job, null, cts.Token));

        Assert.False(File.Exists(_destination));
        AssertNoLeftovers();
    }

    [Theory]
    [InlineData(PipelineStage.VerifyChunks)]
    [InlineData(PipelineStage.VerifyIntegrity)]
    public async Task CancelDuringVerification_LeavesNothingBehind(PipelineStage stage)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var progress = new SyncProgress<PipelineProgress>(p =>
        {
            if (p.Stage == stage)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreatePipeline(Writing(_payload)).RunAsync(Job, progress, cts.Token));

        Assert.False(File.Exists(_destination));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task InvalidOutput_IsDeleted_AndKeepsExistingDestination()
    {
        File.WriteAllText(_destination, "versi lama");
        var decoderError = new InvalidDataException("not a PDF");
        var validator = new FakeValidator(".pdf", _ => throw decoderError);

        var ex = await Assert.ThrowsAsync<VerificationFailedException>(
            () => CreatePipeline(Writing(_payload), validator).RunAsync(Job, null, Ct));

        Assert.Equal(VerificationFailure.InvalidOutput, ex.Failure);
        Assert.Same(decoderError, ex.InnerException);
        Assert.Equal("versi lama", File.ReadAllText(_destination));
        AssertNoLeftovers();
    }

    [Fact]
    public async Task ExistingFileWithTheTempName_IsNotTouched()
    {
        var foreign = _destination + ".condec-tmp";
        File.WriteAllText(foreign, "bukan milik Condec");
        var validator = new FakeValidator(".pdf");

        await CreatePipeline(Writing(_payload), validator).RunAsync(Job, null, Ct);

        Assert.Equal(_destination + ".2.condec-tmp", validator.ValidatedPaths.Single());
        Assert.Equal("bukan milik Condec", File.ReadAllText(foreign));
        Assert.Equal(_payload, File.ReadAllBytes(_destination));
    }

    [Fact]
    public async Task ConverterThatDisposesOutput_StillSucceeds()
    {
        var pipeline = CreatePipeline((request, _, _) =>
        {
            using (var writer = new StreamWriter(request.Output))
            {
                writer.Write("halo");
            }

            return Task.CompletedTask;
        });

        var result = await pipeline.RunAsync(Job, null, Ct);

        Assert.Equal("halo", File.ReadAllText(_destination));
        Assert.Equal(1, result.ChunkCount);
    }

    [Fact]
    public async Task UnsupportedTarget_Throws_BeforeCreatingAnything()
    {
        var pipeline = CreatePipeline(Writing(_payload));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => pipeline.RunAsync(new ConversionJob(_source, ".png", _dir.File("laporan.png")), null, Ct));

        Assert.False(Directory.Exists(_journalDirectory));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.condec-tmp"));
    }

    [Fact]
    public async Task TargetWithoutValidator_IsRefused()
    {
        var converter = new FakeConverter(".docx", ".pdf") { Body = Writing(_payload) };
        var pipeline = new ConversionPipeline(new ConverterRegistry([converter], []), new TempFileJournal(_journalDirectory), ChunkSize);

        await Assert.ThrowsAsync<NotSupportedException>(() => pipeline.RunAsync(Job, null, Ct));

        Assert.False(File.Exists(_destination));
    }

    [Fact]
    public async Task DestinationEqualToSource_IsRefused()
    {
        var pipeline = CreatePipeline(Writing(_payload));

        await Assert.ThrowsAsync<ArgumentException>(() => pipeline.RunAsync(new ConversionJob(_source, ".pdf", _source), null, Ct));

        Assert.Equal(32, new FileInfo(_source).Length);
    }

    private static ConvertBody Writing(byte[] payload) => FakeConverter.Writing(payload);

    private ConversionPipeline CreatePipeline(ConvertBody body, FakeValidator? validator = null)
    {
        var converter = new FakeConverter(".docx", ".pdf") { Body = body };
        var registry = new ConverterRegistry([converter], [validator ?? new FakeValidator(".pdf")]);
        return new ConversionPipeline(registry, new TempFileJournal(_journalDirectory), ChunkSize);
    }

    private void AssertNoLeftovers()
    {
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.condec-tmp"));
        if (Directory.Exists(_journalDirectory))
        {
            Assert.Empty(Directory.GetFiles(_journalDirectory));
        }
    }
}

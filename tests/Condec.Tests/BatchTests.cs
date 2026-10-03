// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Batch;
using Condec.Core.Conversion;
using Condec.Core.Pipeline;

namespace Condec.Tests;

public sealed class PageRangeTests
{
    private static IReadOnlyList<int> Pages(string text, int count)
    {
        Assert.True(PageRange.TryParse(text, out var range), text);
        return range.PagesIn(count);
    }

    [Fact]
    public void SpansAndSinglePagesAreRead() => Assert.Equal([1, 2, 3, 5], Pages("1-3, 5", 10));

    [Fact]
    public void AnOpenSpanRunsToTheLastPage() => Assert.Equal([8, 9, 10], Pages("8-", 10));

    [Fact]
    public void PagesComeOnceAndInOrder() => Assert.Equal([1, 2, 3, 4, 7], Pages("7; 3-4, 1-3, 2", 10));

    [Fact]
    public void PagesPastTheEndOfAShorterFileAreLeftOut()
    {
        Assert.Equal([2, 3], Pages("2-6", 3));
        Assert.Empty(Pages("8-", 5));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(",")]
    [InlineData("0")]
    [InlineData("5-3")]
    [InlineData("-3")]
    [InlineData("1-2-3")]
    [InlineData("a")]
    [InlineData("1.5")]
    [InlineData("+2")]
    [InlineData("99999999999")]
    public void NonsenseIsRefused(string text) => Assert.False(PageRange.TryParse(text, out _));

    [Fact]
    public void AllAndFirst()
    {
        Assert.True(PageRange.All.IsAll);
        Assert.Equal([1, 2, 3], PageRange.All.PagesIn(3));
        Assert.Equal([1], PageRange.First.PagesIn(3));
        Assert.False(PageRange.First.IsAll);
        Assert.Empty(PageRange.All.PagesIn(0));
    }
}

public sealed class SourceKindTests
{
    [Theory]
    [InlineData("foto.PNG", SourceKind.Image)]
    [InlineData("foto.jpeg", SourceKind.Image)]
    [InlineData("scan.tiff", SourceKind.Image)]
    [InlineData("surat.pdf", SourceKind.Pdf)]
    [InlineData("laporan.docx", SourceKind.Document)]
    [InlineData("data.xls", SourceKind.Spreadsheet)]
    [InlineData("slide.odp", SourceKind.Presentation)]
    [InlineData("lagu.flac", SourceKind.Audio)]
    [InlineData("klip.mov", SourceKind.Video)]
    [InlineData("denah.dwg", SourceKind.Cad)]
    [InlineData("tanpa-ekstensi", SourceKind.Other)]
    [InlineData("arsip.zip", SourceKind.Other)]
    public void KindByExtension(string path, SourceKind kind) => Assert.Equal(kind, SourceKinds.Of(path));

    [Fact]
    public void OnlyTargetsEveryFileCanReachOrAlreadyIsAreOffered()
    {
        var targets = new Dictionary<string, IReadOnlyList<string>>
        {
            [".png"] = [".jpg", ".bmp", ".gif"],
            [".jpg"] = [".png", ".bmp", ".gif"],
            [".tif"] = [".png", ".jpg"],
        };

        // PNG and JPG together can all become JPG (the JPG already is) or PNG; the TIFF can't become BMP or GIF.
        Assert.Equal([".jpg", ".bmp", ".gif", ".png"], SourceKinds.CommonTargets([".png", ".jpg", ".png"], e => targets[e], t => t));
        Assert.Equal([".jpg", ".png"], SourceKinds.CommonTargets([".png", ".jpg", ".tif"], e => targets[e], t => t));
    }

    [Theory]
    [InlineData(".jpeg", ".jpg", true)]
    [InlineData(".TIFF", ".tif", true)]
    [InlineData(".htm", ".html", true)]
    [InlineData(".png", ".jpg", false)]
    public void SameFormatUnderTwoNames(string first, string second, bool same) => Assert.Equal(same, SourceKinds.SameFormat(first, second));

    [Fact]
    public void OneKindOfFileKeepsItsWholeList() =>
        Assert.Equal([".jpg", ".bmp"], SourceKinds.CommonTargets<string>([".png"], _ => [".jpg", ".bmp"], t => t));
}

public sealed class OutputNameTests
{
    private const string Folder = @"C:\Hasil";

    private static IReadOnlyList<string> Names(IReadOnlyList<OutputRequest> requests, params string[] existing)
    {
        var taken = new HashSet<string>(existing.Select(e => Path.Combine(Folder, e)), StringComparer.OrdinalIgnoreCase);
        return [.. OutputNames.Plan(requests, Folder, " (hasil)", taken.Contains).Select(p => Path.GetFileName(p))];
    }

    [Fact]
    public void ResultNIsNamedAfterFileN() =>
        Assert.Equal(
            ["satu.jpg", "dua.jpg", "tiga.jpg"],
            Names([new(@"D:\a\satu.png", null, 1, ".jpg"), new(@"D:\a\dua.heic", null, 1, ".jpg"), new(@"D:\a\tiga.bmp", null, 1, ".jpg")]));

    [Fact]
    public void PagesArePaddedSoTheySortInOrder()
    {
        Assert.Equal(["laporan-01.png", "laporan-12.png"], Names([new(@"D:\a\laporan.pdf", 1, 12, ".png"), new(@"D:\a\laporan.pdf", 12, 12, ".png")]));
        Assert.Equal(["surat-3.png"], Names([new(@"D:\a\surat.pdf", 3, 9, ".png")]));
    }

    [Fact]
    public void ATakenNameGetsANumberAndNothingIsOverwritten() =>
        Assert.Equal(["foto (3).jpg"], Names([new(@"D:\a\foto.png", null, 1, ".jpg")], "foto.jpg", "Foto (2).JPG"));

    [Fact]
    public void TwoFilesWithTheSameNameDoNotShareAResult() =>
        Assert.Equal(["foto.jpg", "foto (2).jpg"], Names([new(@"D:\a\foto.png", null, 1, ".jpg"), new(@"D:\b\foto.heic", null, 1, ".jpg")]));

    [Fact]
    public void ASourceIsNeverAResult()
    {
        // A smaller MP4 next to its source gets the same-format suffix, as in the save dialog.
        var same = OutputNames.Plan([new(Path.Combine(Folder, "video.mp4"), null, 1, ".mp4")], Folder, " (hasil)", _ => false);
        Assert.Equal(Path.Combine(Folder, "video (hasil).mp4"), same[0]);

        // Without a suffix the source's own name is still taken, even if File.Exists said no (a file being moved).
        var bare = OutputNames.Plan([new(Path.Combine(Folder, "video.mp4"), null, 1, ".mp4")], Folder, string.Empty, _ => false);
        Assert.Equal(Path.Combine(Folder, "video (2).mp4"), bare[0]);
    }

    [Fact]
    public void FreeLeavesAFreeNameAlone() =>
        Assert.Equal(Path.Combine(Folder, "a.png"), OutputNames.Free(Path.Combine(Folder, "a.png"), _ => false));
}

public sealed class ConversionBatchTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly byte[] _payload = TestData.Bytes(300);

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ConversionPipeline Pipeline(ConvertBody body)
    {
        var registry = new ConverterRegistry([new FakeConverter(".docx", ".pdf") { Body = body }], [new FakeValidator(".pdf")]);
        return new ConversionPipeline(registry, new TempFileJournal(_dir.File("journal")));
    }

    private string Source(string name)
    {
        var path = _dir.File(name);
        File.WriteAllBytes(path, TestData.Bytes(16));
        return path;
    }

    private List<BatchJob> Jobs(ConversionPipeline pipeline, params string[] names) =>
        [.. names.Select(n => new BatchJob(new ConversionJob(Source(n + ".docx"), ".pdf", _dir.File(n + ".pdf")), pipeline))];

    [Fact]
    public async Task AFailedFileDoesNotStopTheOthers()
    {
        var pipeline = Pipeline(async (request, progress, ct) =>
        {
            if (request.SourcePath.Contains("rusak", StringComparison.Ordinal))
            {
                throw new InvalidDataException("rusak");
            }

            await FakeConverter.Writing(_payload)(request, progress, ct);
        });
        var finished = new List<int>();

        var results = await ConversionBatch.RunAsync(
            Jobs(pipeline, "satu", "rusak", "tiga"),
            null,
            r =>
            {
                finished.Add(r.Index);
                return Task.CompletedTask;
            },
            File.Exists,
            Ct);

        Assert.Equal([BatchItemOutcome.Done, BatchItemOutcome.Failed, BatchItemOutcome.Done], results.Select(r => r.Outcome));
        Assert.Equal([0, 1, 2], finished);
        Assert.IsType<InvalidDataException>(results[1].Error);
        Assert.Equal(_payload, File.ReadAllBytes(_dir.File("satu.pdf")));
        Assert.Equal(_payload, File.ReadAllBytes(_dir.File("tiga.pdf")));
        Assert.False(File.Exists(_dir.File("rusak.pdf")));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.condec-tmp"));
    }

    [Fact]
    public async Task AFileThatAppearedMeanwhileIsNotOverwritten()
    {
        var jobs = Jobs(Pipeline(FakeConverter.Writing(_payload)), "satu");
        var other = TestData.Bytes(10, seed: 9);
        File.WriteAllBytes(_dir.File("satu.pdf"), other);

        var results = await ConversionBatch.RunAsync(jobs, null, null, File.Exists, Ct);

        Assert.Equal(other, File.ReadAllBytes(_dir.File("satu.pdf")));
        Assert.Equal(_dir.File("satu (2).pdf"), results[0].Result!.OutputPath);
        Assert.Equal(_payload, File.ReadAllBytes(_dir.File("satu (2).pdf")));
    }

    [Fact]
    public async Task AMovedNameDoesNotTakeALaterFilesName()
    {
        // "satu" is taken on disk, so it moves to "satu (2)", which is the planned name of the second file.
        var pipeline = Pipeline(FakeConverter.Writing(_payload));
        var jobs = new List<BatchJob>
        {
            new(new ConversionJob(Source("satu.docx"), ".pdf", _dir.File("satu.pdf")), pipeline),
            new(new ConversionJob(Source("lain.docx"), ".pdf", _dir.File("satu (2).pdf")), pipeline),
        };
        File.WriteAllBytes(_dir.File("satu.pdf"), [1]);

        var results = await ConversionBatch.RunAsync(jobs, null, null, File.Exists, Ct);

        Assert.Equal(_dir.File("satu (3).pdf"), results[0].Result!.OutputPath);
        Assert.Equal(_dir.File("satu (2).pdf"), results[1].Result!.OutputPath);
    }

    [Fact]
    public async Task CancelStopsTheFileBeingMadeAndTheRest()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pipeline = Pipeline(async (request, progress, ct) =>
        {
            if (request.SourcePath.Contains("dua", StringComparison.Ordinal))
            {
                await request.Output.WriteAsync(_payload.AsMemory(0, 10), ct);
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
            }

            await FakeConverter.Writing(_payload)(request, progress, ct);
        });

        var results = await ConversionBatch.RunAsync(Jobs(pipeline, "satu", "dua", "tiga"), null, null, File.Exists, cts.Token);

        Assert.Equal([BatchItemOutcome.Done, BatchItemOutcome.Cancelled, BatchItemOutcome.Cancelled], results.Select(r => r.Outcome));
        Assert.True(File.Exists(_dir.File("satu.pdf")));
        Assert.False(File.Exists(_dir.File("dua.pdf")));
        Assert.False(File.Exists(_dir.File("tiga.pdf")));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.condec-tmp"));
    }

    [Fact]
    public async Task ProgressCoversTheWholeBatch()
    {
        var reports = new List<BatchProgress>();

        await ConversionBatch.RunAsync(Jobs(Pipeline(FakeConverter.Writing(_payload)), "satu", "dua"), new SyncProgress<BatchProgress>(reports.Add), null, File.Exists, Ct);

        Assert.Equal(0, reports[0].Index);
        Assert.Equal(1, reports[^1].Index);
        Assert.Equal(1, reports[^1].OverallFraction, 9);
        var fractions = reports.Select(r => r.OverallFraction).ToList();
        Assert.True(fractions.Zip(fractions.Skip(1)).All(p => p.Second >= p.First - 1e-9));
        Assert.All(reports, r => Assert.Equal(2, r.Count));
    }
}

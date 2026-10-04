// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp.Entities;
using ACadSharp.Types.Units;
using Condec.Core.Architecture;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Localization;
using Condec.Core.Pdf;

namespace Condec.Tests.Architecture;

/// <summary>Several pages of one PDF in one drawing (owner decision 2026-10-03).</summary>
public sealed class CombinedCadTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    /// <summary>One drawing unit per point.</summary>
    private static readonly CadOptions Points = new(1, CadUnit.Inches, 72);

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Pdf(params string[] pages) => TestPdf.WritePages(_dir.File(Guid.NewGuid().ToString("N") + ".pdf"), pages);

    /// <summary>A 1000 × 500 pixel picture with one wall line from (100, 400) to (600, 400).</summary>
    private static ArchitectureCadOptions Picture(double millimetersPerPixel)
    {
        var wall = new PolylinePrimitive([(100, 400), (600, 400)], false);
        var analysis = new DrawingAnalysis(
            1000,
            500,
            1,
            96,
            96,
            [new DrawingGroup(DrawingObjectKind.Walls, [DrawingItem.OfShapes(new PixelRect(100, 400, 600, 401), [wall])])],
            [],
            1,
            true);
        return new ArchitectureCadOptions(analysis, new HashSet<DrawingObjectKind> { DrawingObjectKind.Walls }, millimetersPerPixel);
    }

    [Fact]
    public async Task PagesSitSideBySideWithAGap()
    {
        var path = Pdf("10 10 m 60 10 l S\n", "10 10 m 60 10 l S\n", "10 10 m 60 10 l S\n");

        var (cad, count) = await CombinedCadBuilder.BuildAsync(path, new CombinedCadOptions([Points with { PageNumber = 1 }, Points with { PageNumber = 2 }, Points with { PageNumber = 3 }]), null, null, Ct);

        Assert.Equal(3, count);
        var starts = cad.Entities.OfType<Line>().Select(l => l.StartPoint.X).Order().ToList();

        // Each page is 200 pt wide, then a gap of 10% of it.
        Assert.Equal([10, 230, 450], starts.Select(x => Math.Round(x, 6)));
        Assert.All(cad.Entities.OfType<Line>(), l => Assert.Equal(10, l.StartPoint.Y, 6));
    }

    [Fact]
    public async Task OnlyTheChosenPagesInTheChosenOrder()
    {
        var path = Pdf("10 10 m 60 10 l S\n", "10 20 m 60 20 l S\n", "10 30 m 60 30 l S\n");

        var (cad, _) = await CombinedCadBuilder.BuildAsync(path, new CombinedCadOptions([Points with { PageNumber = 3 }, Points with { PageNumber = 1 }]), null, null, Ct);

        var lines = cad.Entities.OfType<Line>().OrderBy(l => l.StartPoint.X).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(30, lines[0].StartPoint.Y, 6);
        Assert.Equal(10, lines[1].StartPoint.Y, 6);
    }

    [Fact]
    public async Task AScannedPageKeepsItsRealSizeInTheDrawingsUnit()
    {
        var path = Pdf("10 10 m 60 10 l S\n");
        var centimeters = new CadOptions(1, CadUnit.Centimeters);

        var (cad, _) = await CombinedCadBuilder.BuildAsync(path, new CombinedCadOptions([centimeters, Picture(millimetersPerPixel: 0.5)]), null, null, Ct);

        Assert.Equal(UnitsType.Centimeters, cad.Header.InsUnits);

        // Page 1 is 200 pt = 7.0556 cm wide, plus 10%: the picture starts at 7.7611 cm. Its wall starts 100 px × 0.5 mm = 5 cm in.
        var pageWidth = 200 * centimeters.UnitsPerPoint;
        var wall = cad.Entities.OfType<Line>().OrderBy(l => l.StartPoint.X).Last();
        Assert.Equal((pageWidth * 1.1) + 5, wall.StartPoint.X, 6);
        Assert.Equal((pageWidth * 1.1) + 30, wall.EndPoint.X, 6);
        Assert.Equal("WALLS", wall.Layer.Name);
    }

    [Fact]
    public async Task TheDrawingSurvivesBeingWrittenAndReadBack()
    {
        var path = Pdf("10 10 m 60 10 l S\nBT /F1 12 Tf 10 50 Td (Denah) Tj ET\n", "10 10 m 60 10 l S\n");
        var (cad, count) = await CombinedCadBuilder.BuildAsync(path, new CombinedCadOptions([Points with { PageNumber = 1 }, Picture(1), Points with { PageNumber = 2 }]), null, null, Ct);

        var read = CadFiles.ReadDxf(CadFiles.Write(cad, ".dxf"));

        Assert.Equal(count, read.Entities.Count);
        Assert.Contains(read.Layers, l => l.Name == "WALLS");
        Assert.Single(read.Entities.OfType<TextEntity>(), t => t.Value == "Denah");
    }

    [Fact]
    public async Task PicturesOnTheVectorPages_AreNoted()
    {
        var path = TestPdf.Write(_dir.File("logo.pdf"), "q 20 0 0 20 150 60 cm /Im Do Q\n10 10 m 60 10 l S\n", image: true);
        var notes = new ConversionNotes();

        await CombinedCadBuilder.BuildAsync(path, new CombinedCadOptions([Points, Picture(1), Points]), notes, null, Ct);

        var note = Assert.Single(notes.Items);
        Assert.Equal(NoteSeverity.Informational, note.Severity);
        Assert.Equal(Loc.Format("Note.PicturesLeftOut", 2), note.Message);
    }

    [Fact]
    public async Task PicturesOnTheVectorPages_BecomeColorAreasOnTheirOwnPage()
    {
        var path = TestPdf.Write(_dir.File("logo.pdf"), "q 40 0 0 20 100 50 cm /Im Do Q\n10 10 m 60 10 l S\n", image: true);
        var notes = new ConversionNotes();
        var decoder = new PdfToCadTests.FakeDecoder(PdfToCadTests.Halves(), PdfToCadTests.Halves());

        var (cad, _) = await CombinedCadBuilder.BuildAsync(path, new CombinedCadOptions([Points, Points]), notes, decoder, Ct);

        // The second page starts 200 pt + 10% to the right, and its picture with it.
        var lefts = cad.Entities.OfType<Hatch>().Select(h => PdfToCadTests.BoundsOf(h).MinX).Order().ToList();
        Assert.Equal([100, 320], lefts.Select(x => Math.Round(x, 6)));
        Assert.Empty(notes.Items);
    }

    [Fact]
    public async Task NothingOnAnyPageIsRefused()
    {
        var path = Pdf("", "");

        await Assert.ThrowsAsync<NothingToConvertException>(() => CombinedCadBuilder.BuildAsync(path, new CombinedCadOptions([Points, Points with { PageNumber = 2 }]), null, null, Ct));
    }

    [Fact]
    public async Task APageThatIsNotThereIsRefused()
    {
        var path = Pdf("10 10 m 60 10 l S\n");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => CombinedCadBuilder.BuildAsync(path, new CombinedCadOptions([Points with { PageNumber = 2 }]), null, null, Ct));
    }
}

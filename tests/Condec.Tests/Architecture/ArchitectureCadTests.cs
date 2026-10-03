// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.Entities;
using Condec.Core.Architecture;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Pipeline;

namespace Condec.Tests.Architecture;

public sealed class ArchitectureCadTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlySet<DrawingObjectKind> Everything = new HashSet<DrawingObjectKind>(Enum.GetValues<DrawingObjectKind>());

    private static DrawingGroup Group(DrawingObjectKind kind, params DrawingItem[] items) => new(kind, items);

    /// <summary>A 1000 × 500 pixel picture holding one of each shape, drawn by hand so the answers are exact.</summary>
    private static DrawingAnalysis Analysis(double dpi = 254)
    {
        var wall = new PolylinePrimitive([(100, 400), (600, 400)], false);
        var room = new PolylinePrimitive([(100, 100), (500, 100), (500, 300), (100, 300)], true);
        var door = new ArcPrimitive(300, 400, 60, 0, Math.PI / 2);
        var column = new CirclePrimitive(800, 200, 25);
        var text = new TextLineItem("A-101", new PixelRect(200, 150, 320, 170));
        var logo = new PolylinePrimitive([(900, 400), (950, 400), (950, 450), (900, 450)], true);
        var cell = new PolylinePrimitive([(700, 50), (900, 50)], false);

        return new DrawingAnalysis(
            1000,
            500,
            1,
            dpi,
            dpi,
            [
                Group(DrawingObjectKind.Walls, DrawingItem.OfShapes(new PixelRect(100, 100, 600, 400), [wall, room, column])),
                Group(DrawingObjectKind.Openings, DrawingItem.OfShapes(new PixelRect(240, 40, 360, 100), [door])),
                Group(DrawingObjectKind.Text, new DrawingItem(text.Bounds, [], [text])),
                Group(DrawingObjectKind.Logo, DrawingItem.OfShapes(new PixelRect(900, 50, 950, 100), [logo])),
                Group(DrawingObjectKind.Table, DrawingItem.OfShapes(new PixelRect(700, 450, 900, 460), [cell])),
            ],
            [],
            1,
            true);
    }

    private static CadDocument Build(DrawingAnalysis analysis, IReadOnlySet<DrawingObjectKind> include, double? mmPerPixel = null) =>
        ArchitectureCadBuilder.Build(new ArchitectureCadOptions(analysis, include, mmPerPixel ?? ArchitectureCadBuilder.MillimetersPerPixelFromDpi(analysis))).Cad;

    [Fact]
    public void EachKindOfObjectGoesOnItsOwnLayer()
    {
        var cad = Build(Analysis(), Everything);

        Assert.Equal(["WALLS", "OPENINGS", "TEXT", "LOGO", "TABLE"], cad.Layers.Select(l => l.Name).Where(n => n != "0").ToArray());
        Assert.All(cad.Entities.OfType<Arc>(), e => Assert.Equal("OPENINGS", e.Layer.Name));
        Assert.All(cad.Entities.OfType<TextEntity>(), e => Assert.Equal("TEXT", e.Layer.Name));
        Assert.Equal(3, cad.Entities.Count(e => e.Layer.Name == "WALLS"));
    }

    [Fact]
    public void ShapesBecomeTheEntityThatFitsThem()
    {
        var cad = Build(Analysis(), Everything);

        Assert.Equal(2, cad.Entities.OfType<Line>().Count());
        Assert.Equal(2, cad.Entities.OfType<LwPolyline>().Count(p => p.IsClosed));
        Assert.Single(cad.Entities.OfType<Arc>());
        Assert.Single(cad.Entities.OfType<Circle>(), c => c is not Arc);
        Assert.Equal("A-101", Assert.Single(cad.Entities.OfType<TextEntity>()).Value);
    }

    [Fact]
    public void ThePictureSizeAndResolutionMakeMillimeters()
    {
        // 254 dpi: one pixel is 0.1 mm. The wall runs from x 100 to 600 at y 400, which is 10 to 60 mm and 40 mm up.
        var wall = Build(Analysis(254), Everything).Entities.OfType<Line>().First(l => l.Layer.Name == "WALLS");

        Assert.Equal(10, wall.StartPoint.X, 6);
        Assert.Equal(40, wall.StartPoint.Y, 6);
        Assert.Equal(60, wall.EndPoint.X, 6);
    }

    [Fact]
    public void ACalibratedScaleReplacesTheResolution()
    {
        // 0.5 mm per pixel, whatever the file says: the same wall is 250 mm long.
        var wall = Build(Analysis(96), Everything, mmPerPixel: 0.5).Entities.OfType<Line>().First(l => l.Layer.Name == "WALLS");

        Assert.Equal(250, wall.EndPoint.X - wall.StartPoint.X, 6);
    }

    [Fact]
    public void AnArcKeepsItsAnglesAndItsRadiusIsScaled()
    {
        var arc = Build(Analysis(254), Everything).Entities.OfType<Arc>().Single();

        Assert.Equal(6, arc.Radius, 6);
        Assert.Equal(0, arc.StartAngle, 6);
        Assert.Equal(Math.PI / 2, arc.EndAngle, 6);
        Assert.Equal(30, arc.Center.X, 6);
    }

    [Fact]
    public void TextIsPlacedAtTheBottomLeftOfItsLineAndAsHighAsIt()
    {
        var text = Build(Analysis(254), Everything).Entities.OfType<TextEntity>().Single();

        // The line spans rows 150 to 170 (y down) of a 500 row picture: 20 px = 2 mm high, its bottom 330 px = 33 mm up.
        Assert.Equal(2, text.Height, 6);
        Assert.Equal(20, text.InsertPoint.X, 6);
        Assert.Equal(33, text.InsertPoint.Y, 6);
    }

    [Fact]
    public void OnlyTheChosenKindsAreWritten()
    {
        var cad = Build(Analysis(), new HashSet<DrawingObjectKind> { DrawingObjectKind.Walls, DrawingObjectKind.Text });

        Assert.Empty(cad.Entities.OfType<Arc>());
        Assert.DoesNotContain(cad.Layers, l => l.Name is "OPENINGS" or "LOGO" or "TABLE");
        Assert.Contains(cad.Layers, l => l.Name == "WALLS");
        Assert.Single(cad.Entities.OfType<TextEntity>());
    }

    [Fact]
    public void NothingChosenIsAnError()
    {
        Assert.Throws<NothingToTraceException>(() => Build(Analysis(), new HashSet<DrawingObjectKind>()));
    }

    [Fact]
    public void WritingThatWasNotReadMakesNoTextEntity()
    {
        var unread = new TextLineItem(null, new PixelRect(10, 10, 100, 30));
        var analysis = new DrawingAnalysis(
            200,
            100,
            1,
            254,
            254,
            [Group(DrawingObjectKind.Text, new DrawingItem(unread.Bounds, [new PolylinePrimitive([(10, 80), (50, 80)], false)], [unread]))],
            [],
            1,
            false);

        var cad = Build(analysis, Everything);

        Assert.Empty(cad.Entities.OfType<TextEntity>());
        Assert.Single(cad.Entities.OfType<Line>());
    }

    [Fact]
    public void ABadScaleIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(Analysis(), Everything, mmPerPixel: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(Analysis(), Everything, mmPerPixel: double.NaN));
    }

    [Fact]
    public void WithoutAResolutionNinetySixDpiIsAssumedAndTheReductionIsKept()
    {
        // The picture was averaged down 2 times: one analysis pixel stands for two of the file's, so it is twice as big.
        var analysis = Analysis(dpi: 0) with { Reduction = 2 };

        Assert.Equal(25.4 / 48, ArchitectureCadBuilder.MillimetersPerPixelFromDpi(analysis), 9);
    }

    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    public async Task ThePipelineSavesAFileThatReadsBack(string target)
    {
        var registry = new ConverterRegistry([new ArchitectureToCadConverter()], [new CadOutputValidator()]);
        var pipeline = new ConversionPipeline(registry, new TempFileJournal(_dir.File("journal")));
        var source = _dir.File("denah.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3], Ct);
        var destination = _dir.File("denah" + target);
        var options = new ArchitectureCadOptions(Analysis(), Everything, 0.1);

        var result = await pipeline.RunAsync(new ConversionJob(source, target, destination, options), null, Ct);

        Assert.Equal(destination, result.OutputPath);
        var cad = CadFiles.Read(destination, target);
        Assert.Contains(cad.Layers, l => l.Name == "WALLS");
        Assert.Contains(cad.Entities.OfType<TextEntity>(), t => t.Value == "A-101");
        Assert.Equal(ACadSharp.Types.Units.UnitsType.Millimeters, cad.Header.InsUnits);
    }

    [Fact]
    public async Task WithoutTheAnalysisTheConverterRefusesToRun()
    {
        using var output = new MemoryStream();
        var request = new ConversionRequest("denah.png", ".png", ".dwg", output);

        await Assert.ThrowsAsync<ArgumentException>(() => new ArchitectureToCadConverter()
            .ConvertAsync(request, new SyncProgress<ConversionProgress>(_ => { }), Ct));
    }

    [Theory]
    [InlineData(".png", true)]
    [InlineData(".jpg", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".heic", true)]
    [InlineData(".heif", true)]
    [InlineData(".pdf", true)]
    [InlineData(".dxf", false)]
    [InlineData(".docx", false)]
    public void TheSourcesAreThePicturesAndPdf(string source, bool offered)
    {
        var targets = new ArchitectureToCadConverter().GetTargets(source);

        Assert.Equal(offered ? [".dwg", ".dxf"] : [], targets);
    }

    [Fact]
    public async Task APlanGoesAllTheWayFromPixelsToCadLayers()
    {
        var analysis = await DrawingAnalyzer.AnalyzeAsync(AnalysisTests.Plan().Picture(dpi: 254), null, null, Ct);

        var (cad, count) = ArchitectureCadBuilder.Build(new ArchitectureCadOptions(analysis, Everything, ArchitectureCadBuilder.MillimetersPerPixelFromDpi(analysis)));

        Assert.True(count > 5);
        Assert.Contains(cad.Layers, l => l.Name == "WALLS");
        Assert.Contains(cad.Layers, l => l.Name == "OPENINGS");
        Assert.Single(cad.Entities.OfType<Arc>(), a => a.Layer.Name == "OPENINGS");
    }
}

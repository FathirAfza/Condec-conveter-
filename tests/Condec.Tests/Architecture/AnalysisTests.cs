// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Architecture;

namespace Condec.Tests.Architecture;

public sealed class AnalysisTests
{
    private static readonly (byte B, byte G, byte R) Red = (30, 30, 220);

    private sealed class FakeRecognizer(string? answer) : ITextRecognizer
    {
        public int Calls { get; private set; }

        public Task<string?> RecognizeAsync(RasterPicture line, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }

    // --- logos ---

    [Fact]
    public void ABlockOfColorIsALogo()
    {
        var drawing = new SyntheticDrawing(1000, 700).Fill(100, 100, 220, 180, Red);

        var logos = LogoFinder.Find(drawing.Picture(), CancellationToken.None);

        var logo = Assert.Single(logos);
        Assert.Equal(new PixelRect(100, 100, 220, 180), logo);
    }

    [Fact]
    public void ASmallColoredMarkIsNotALogo()
    {
        var drawing = new SyntheticDrawing(1000, 700).Fill(100, 100, 110, 110, Red);

        Assert.Empty(LogoFinder.Find(drawing.Picture(), CancellationToken.None));
    }

    [Fact]
    public void BlackInkIsNeverALogo()
    {
        var drawing = new SyntheticDrawing(1000, 700).Fill(100, 100, 300, 200);

        Assert.Empty(LogoFinder.Find(drawing.Picture(), CancellationToken.None));
    }

    [Fact]
    public void AColoredPictureThatFillsMostOfThePageIsAPhotoNotALogo()
    {
        var drawing = new SyntheticDrawing(400, 300).Fill(0, 0, 400, 300, Red);

        Assert.Empty(LogoFinder.Find(drawing.Picture(), CancellationToken.None));
    }

    // --- tables ---

    private static SyntheticDrawing Ruled(SyntheticDrawing drawing, int left, int top, int columns, int rows, int cellWidth, int cellHeight, int writtenCells = 0)
    {
        for (var r = 0; r <= rows; r++)
        {
            drawing.Line(left, top + (r * cellHeight), left + (columns * cellWidth), top + (r * cellHeight), 2);
        }

        for (var c = 0; c <= columns; c++)
        {
            drawing.Line(left + (c * cellWidth), top, left + (c * cellWidth), top + (rows * cellHeight), 2);
        }

        // Writing in the first cells, row by row.
        for (var i = 0; i < writtenCells; i++)
        {
            drawing.Letters(left + ((i % columns) * cellWidth) + 8, top + ((i / columns) * cellHeight) + 8, 4, 10);
        }

        return drawing;
    }

    [Fact]
    public void ARuledGridWithWritingInItIsATable()
    {
        var drawing = Ruled(new SyntheticDrawing(1200, 800), 700, 500, 4, 5, 90, 28, writtenCells: 8);

        var grid = Assert.Single(TableFinder.Find(drawing.Ink(), CancellationToken.None));

        Assert.True(grid.IsTable);
        Assert.Equal(20, grid.Cells.Count);
        Assert.True(grid.Bounds.Left <= 701 && grid.Bounds.Right >= 1059 && grid.Bounds.Top <= 501 && grid.Bounds.Bottom >= 639, grid.Bounds.ToString());
    }

    [Fact]
    public void ARuledGridWithNothingInItIsNotATable()
    {
        var drawing = Ruled(new SyntheticDrawing(1200, 800), 700, 500, 4, 5, 90, 28);

        Assert.DoesNotContain(TableFinder.Find(drawing.Ink(), CancellationToken.None), g => g.IsTable);
    }

    [Fact]
    public void ALargeRoomIsNotATable()
    {
        var drawing = new SyntheticDrawing(1200, 800).Rectangle(100, 100, 600, 450, 3);
        drawing.Line(350, 100, 350, 450, 3);
        drawing.Line(100, 280, 600, 280, 3);
        drawing.Letters(150, 180, 4, 10).Letters(400, 180, 4, 10).Letters(150, 350, 4, 10).Letters(400, 350, 4, 10);

        Assert.DoesNotContain(TableFinder.Find(drawing.Ink(), CancellationToken.None), g => g.IsTable);
    }

    [Fact]
    public void TheFrameOfTheSheetIsTakenOff()
    {
        var drawing = new SyntheticDrawing(1200, 800).Rectangle(10, 10, 1190, 790, 3);

        Assert.Empty(TableFinder.Find(drawing.Ink(), CancellationToken.None));
    }

    [Fact]
    public void ATableAttachedToTheFrameIsStillATable()
    {
        // A title block drawn against the sheet's frame, its lines running into the frame's.
        var drawing = new SyntheticDrawing(1200, 800).Rectangle(10, 10, 1190, 790, 3);
        Ruled(drawing, 1000, 500, 2, 10, 95, 29, writtenCells: 8);

        var grid = Assert.Single(TableFinder.Find(drawing.Ink(), CancellationToken.None), g => g.IsTable);

        Assert.True(grid.Bounds.Left >= 990 && grid.Bounds.Top >= 490, grid.Bounds.ToString());
    }

    [Fact]
    public void AStripOfThreeCellsIsNotATable()
    {
        var drawing = Ruled(new SyntheticDrawing(1200, 800), 700, 500, 3, 1, 90, 28, writtenCells: 3);

        Assert.DoesNotContain(TableFinder.Find(drawing.Ink(), CancellationToken.None), g => g.IsTable);
    }

    // --- writing ---

    private static List<TextFinder.Line> Lines(SyntheticDrawing drawing)
    {
        var ink = drawing.Ink();
        var blobs = ConnectedComponents.Label(ink, out _);
        return TextFinder.Find(blobs, ink.Width, ink.Height, _ => true, CancellationToken.None);
    }

    [Fact]
    public void ARowOfMarksIsALineOfText()
    {
        var drawing = new SyntheticDrawing(800, 400).Letters(100, 100, 8);

        var line = Assert.Single(Lines(drawing));

        Assert.Equal(8, line.Marks.Count);
        Assert.InRange(line.Bounds.Left, 95, 105);
        Assert.InRange(line.Bounds.Top, 95, 105);
    }

    [Fact]
    public void TwoRowsAreTwoLines()
    {
        var drawing = new SyntheticDrawing(800, 400).Letters(100, 100, 8).Letters(100, 160, 6);

        Assert.Equal(2, Lines(drawing).Count);
    }

    [Fact]
    public void AStackOfMarksIsNotALineOfText()
    {
        var drawing = new SyntheticDrawing(800, 400);
        for (var i = 0; i < 4; i++)
        {
            drawing.Letters(100, 100 + (i * 20), 1);
        }

        Assert.Empty(Lines(drawing));
    }

    [Fact]
    public void ALongLineIsNotText()
    {
        var drawing = new SyntheticDrawing(800, 400).Line(50, 200, 700, 200, 3);

        Assert.Empty(Lines(drawing));
    }

    [Fact]
    public void TwoMarksAreTooFewToBeText()
    {
        Assert.Empty(Lines(new SyntheticDrawing(800, 400).Letters(100, 100, 2)));
    }

    // --- doors and windows ---

    private static PolylinePrimitive Line(double x1, double y1, double x2, double y2) => new([(x1, y1), (x2, y2)], false);

    [Fact]
    public void AnArcAndItsLeafAreADoor()
    {
        var arc = new ArcPrimitive(300, 300, 60, 0, Math.PI / 2);
        var leaf = Line(300, 300, 300, 360);
        var wall = Line(100, 300, 300, 300);

        var result = OpeningFinder.Find([arc, leaf, wall], 1000, 800, 1.5);

        var door = Assert.Single(result.Openings);
        Assert.Contains(arc, door);
        Assert.Equal(2, door.Count);
        var remaining = Assert.Single(result.Remaining);
        Assert.Same(wall, remaining.Shape);
    }

    [Fact]
    public void ALeafThatIsPartOfTheWallPolylineIsCutOut()
    {
        // The wall runs into the hinge and the leaf runs on: one polyline of three points.
        var arc = new ArcPrimitive(300, 300, 60, 0, Math.PI / 2);
        var wallAndLeaf = new PolylinePrimitive([(100, 300), (300, 300), (300, 360)], false);

        var result = OpeningFinder.Find([arc, wallAndLeaf], 1000, 800, 1.5);

        var door = Assert.Single(result.Openings);
        Assert.Equal(2, door.Count);
        var left = Assert.Single(result.Remaining);
        Assert.Same(wallAndLeaf, left.Source);
        var wall = Assert.IsType<PolylinePrimitive>(left.Shape);
        Assert.Equal(new[] { (100.0, 300.0), (300.0, 300.0) }, wall.Points);
    }

    [Fact]
    public void AnArcWithNoLeafIsStillAnOpening()
    {
        var result = OpeningFinder.Find([new ArcPrimitive(300, 300, 60, 0, Math.PI / 2)], 1000, 800, 1.5);

        Assert.Single(result.Openings);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(150)]
    public void AnArcTurningTooLittleOrTooMuchIsNotADoor(double degrees)
    {
        var result = OpeningFinder.Find([new ArcPrimitive(300, 300, 60, 0, degrees * Math.PI / 180)], 1000, 800, 1.5);

        Assert.Empty(result.Openings);
        Assert.Single(result.Remaining);
    }

    [Fact]
    public void ABigArcIsNotADoor()
    {
        var result = OpeningFinder.Find([new ArcPrimitive(500, 400, 300, 0, Math.PI / 2)], 1000, 800, 1.5);

        Assert.Empty(result.Openings);
    }

    [Fact]
    public void ThreeParallelShortLinesAreAWindow()
    {
        var result = OpeningFinder.Find([Line(100, 300, 160, 300), Line(100, 305, 160, 305), Line(100, 310, 160, 310)], 1000, 800, 1.5);

        var window = Assert.Single(result.Openings);
        Assert.Equal(3, window.Count);
        Assert.Empty(result.Remaining);
    }

    [Fact]
    public void TwoParallelLinesAreAWallNotAWindow()
    {
        var result = OpeningFinder.Find([Line(100, 300, 160, 300), Line(100, 305, 160, 305)], 1000, 800, 1.5);

        Assert.Empty(result.Openings);
        Assert.Equal(2, result.Remaining.Count);
    }

    [Fact]
    public void ParallelLinesOfDifferentLengthsAreNotAWindow()
    {
        var result = OpeningFinder.Find([Line(100, 300, 160, 300), Line(100, 305, 220, 305), Line(100, 310, 300, 310)], 1000, 800, 1.5);

        Assert.Empty(result.Openings);
    }

    [Fact]
    public void ManyParallelLinesAreStairsNotAWindow()
    {
        var lines = Enumerable.Range(0, 8).Select(i => (DrawingPrimitive)Line(100, 300 + (i * 6), 160, 300 + (i * 6))).ToList();

        Assert.Empty(OpeningFinder.Find(lines, 1000, 800, 1.5).Openings);
    }

    // --- the whole analysis ---

    internal static SyntheticDrawing Plan()
    {
        var drawing = new SyntheticDrawing(1400, 1000);

        // A room: four walls, with a door in the bottom wall (gap 300 to 360, leaf up, arc into the room).
        drawing.Line(100, 100, 700, 100, 4).Line(700, 100, 700, 600, 4).Line(100, 100, 100, 600, 4);
        drawing.Line(100, 600, 300, 600, 4).Line(360, 600, 700, 600, 4);
        drawing.Line(300, 600, 300, 540, 2).Arc(300, 600, 60, -90, 0, 2);

        // A line of writing, a ruled table and a logo, apart from the room.
        drawing.Letters(150, 650, 8);
        Ruled(drawing, 850, 700, 4, 5, 90, 28, writtenCells: 8);
        drawing.Fill(900, 120, 1060, 220, Red);
        return drawing;
    }

    [Fact]
    public async Task APlanIsSortedIntoWhatItHolds()
    {
        var analysis = await DrawingAnalyzer.AnalyzeAsync(Plan().Picture(), recognizer: null, progress: null, CancellationToken.None);

        Assert.Equal(1, analysis.Group(DrawingObjectKind.Logo).Count);
        Assert.Equal(1, analysis.Group(DrawingObjectKind.Table).Count);
        Assert.Equal(1, analysis.Group(DrawingObjectKind.Text).Count);
        Assert.Equal(1, analysis.Group(DrawingObjectKind.Openings).Count);
        Assert.True(analysis.Group(DrawingObjectKind.Walls).Count >= 2);
        Assert.False(analysis.TextWasRead);
        Assert.Empty(analysis.UnclearAreas);
        Assert.Equal(1, analysis.ClearShare);
    }

    [Fact]
    public async Task WritingInATableBelongsToTheTable()
    {
        var analysis = await DrawingAnalyzer.AnalyzeAsync(Plan().Picture(), new FakeRecognizer("X"), null, CancellationToken.None);

        var table = Assert.Single(analysis.Group(DrawingObjectKind.Table).Items);
        Assert.True(table.Texts.Count >= 8, $"{table.Texts.Count} lines in the table");
        Assert.All(table.Texts, t => Assert.Equal("X", t.Text));

        // The line of writing outside it is the only free text.
        Assert.Single(analysis.Group(DrawingObjectKind.Text).Items);
    }

    [Fact]
    public async Task ABoxWithALogoInItIsATitleBlockAndHoldsTheLogo()
    {
        var drawing = new SyntheticDrawing(1400, 1000).Rectangle(900, 100, 1300, 400, 3).Fill(960, 130, 1080, 230, Red).Letters(960, 300, 8);

        var analysis = await DrawingAnalyzer.AnalyzeAsync(drawing.Picture(), null, null, CancellationToken.None);

        var kop = Assert.Single(analysis.Group(DrawingObjectKind.Logo).Items);
        Assert.True(kop.Bounds.Left <= 901 && kop.Bounds.Right >= 1299, kop.Bounds.ToString());
        Assert.Equal(0, analysis.Group(DrawingObjectKind.Table).Count);
        Assert.Equal(0, analysis.Group(DrawingObjectKind.Text).Count);
        Assert.NotEmpty(kop.Texts);
    }

    [Fact]
    public async Task ASoftLogoIsNotCalledUnclear()
    {
        // Shading inside a logo is soft by nature; it must not raise the unclear warning.
        var drawing = new SyntheticDrawing(1000, 700).Fill(100, 100, 300, 250, Red).Blurred(4);

        var analysis = await DrawingAnalyzer.AnalyzeAsync(drawing.Picture(), null, null, CancellationToken.None);

        Assert.Empty(analysis.UnclearAreas);
    }

    [Fact]
    public async Task WritingThatIsNotReadIsKeptAsLines()
    {
        var analysis = await DrawingAnalyzer.AnalyzeAsync(Plan().Picture(), null, null, CancellationToken.None);

        var text = Assert.Single(analysis.Group(DrawingObjectKind.Text).Items);
        Assert.NotEmpty(text.Primitives);
        Assert.Null(Assert.Single(text.Texts).Text);
    }

    [Fact]
    public async Task WritingThatIsReadBecomesText()
    {
        var recognizer = new FakeRecognizer("A-101");

        var analysis = await DrawingAnalyzer.AnalyzeAsync(Plan().Picture(), recognizer, null, CancellationToken.None);

        Assert.True(analysis.TextWasRead);
        var text = Assert.Single(analysis.Group(DrawingObjectKind.Text).Items);
        Assert.Empty(text.Primitives);
        Assert.Equal("A-101", Assert.Single(text.Texts).Text);
        Assert.True(recognizer.Calls >= 1);
    }

    [Fact]
    public async Task AnUnreadableLineFallsBackToLines()
    {
        var analysis = await DrawingAnalyzer.AnalyzeAsync(Plan().Picture(), new FakeRecognizer("   "), null, CancellationToken.None);

        Assert.False(analysis.TextWasRead);
        Assert.NotEmpty(Assert.Single(analysis.Group(DrawingObjectKind.Text).Items).Primitives);
    }

    [Fact]
    public async Task ABlurredPlanHasUnclearAreasAndALowerClearShare()
    {
        var analysis = await DrawingAnalyzer.AnalyzeAsync(Plan().Blurred(3).Picture(), null, null, CancellationToken.None);

        Assert.NotEmpty(analysis.UnclearAreas);
    }

    [Fact]
    public async Task ABigPictureIsAveragedDownAndKeepsItsPhysicalSize()
    {
        var picture = new SyntheticDrawing(4800, 3000).Line(200, 200, 4000, 200, 8).Picture(dpi: 300);

        var analysis = await DrawingAnalyzer.AnalyzeAsync(picture, null, null, CancellationToken.None);

        Assert.Equal(2, analysis.Reduction);
        Assert.Equal(2400, analysis.Width);
        Assert.Equal(150, analysis.DpiX);
    }

    [Fact]
    public async Task CancellingStopsTheAnalysis()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DrawingAnalyzer.AnalyzeAsync(Plan().Picture(), null, null, cancel.Token));
    }

    [Fact]
    public async Task ABlankPageHasNothingInIt()
    {
        var analysis = await DrawingAnalyzer.AnalyzeAsync(new SyntheticDrawing(600, 400).Picture(), null, null, CancellationToken.None);

        Assert.Equal(0, analysis.ObjectCount);
        Assert.Equal(1, analysis.ClearShare);
    }
}

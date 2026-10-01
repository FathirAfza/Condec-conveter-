// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp.Entities;
using Condec.Core.Cad;
using Condec.Core.Pdf;
using CSMath;

namespace Condec.Tests;

/// <summary>PDF pages written by <see cref="TestPdf"/>, read through the real PdfPig path into entities. Units: 1 drawing unit = 1 pt.</summary>
public sealed class PdfToCadTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    /// <summary>One drawing unit per point, so coordinates can be compared with the content stream.</summary>
    private static readonly CadOptions PointUnits = new(1, CadUnit.Inches, 72);

    public void Dispose() => _temp.Dispose();

    private List<Entity> Read(string content, CadOptions? options = null, int rotate = 0, string? form = null, string formMatrix = "1 0 0 1 0 0", double width = 200, double height = 100)
    {
        var path = TestPdf.Write(_temp.File(Guid.NewGuid().ToString("N") + ".pdf"), content, width, height, rotate, form: form, formMatrix: formMatrix);
        using var pdf = PdfInspector.Open(path);
        return PdfToCadConverter.ReadVectors(pdf.GetPage(1), options ?? PointUnits, CancellationToken.None);
    }

    [Fact]
    public void RotatedPage_ComesOutTheWayAViewerShowsIt()
    {
        // PdfPig already turns the page for /Rotate; turning it again mirrored every landscape drawing.
        var entities = Read("0 0 m 200 0 l S\nBT /F1 12 Tf 10 10 Td (Bawah) Tj ET\n", rotate: 90);

        var line = Assert.Single(entities.OfType<Line>());
        Assert.Equal(0, line.StartPoint.X, 6);
        Assert.Equal(200, line.StartPoint.Y, 6);
        Assert.Equal(0, line.EndPoint.X, 6);
        Assert.Equal(0, line.EndPoint.Y, 6);

        var text = Assert.Single(entities.OfType<TextEntity>());
        Assert.Equal("Bawah", text.Value);
        Assert.Equal(10, text.InsertPoint.X, 6);
        Assert.Equal(190, text.InsertPoint.Y, 6);
        Assert.Equal(-Math.PI / 2, text.Rotation, 6);
    }

    [Fact]
    public void UpsideDownText_StaysOneWordInReadingOrder()
    {
        var text = Assert.Single(Read("BT /F1 12 Tf 10 10 Td (Bawah) Tj ET\n", rotate: 180).OfType<TextEntity>());

        Assert.Equal("Bawah", text.Value);
        Assert.Equal(Math.PI, Math.Abs(text.Rotation), 6);
    }

    [Fact]
    public void Circle_ClosedOrNot_BecomesOneCircle()
    {
        var entities = Read(TestPdf.Circle(100, 50, 20) + "S\n" + TestPdf.Circle(150, 50, 15, close: false) + "S\n");

        Assert.Equal(2, entities.Count);
        Assert.All(entities, e => Assert.IsType<Circle>(e, exactMatch: true));
        Assert.Contains(entities, e => e is Circle c && Math.Abs(c.Radius - 15) < 0.01 && Math.Abs(c.Center.X - 150) < 0.01);
    }

    [Fact]
    public void QuarterCircle_BecomesAnArc()
    {
        var arc = Assert.Single(Read("150 10 m 150 21.046 141.046 30 130 30 c S\n").OfType<Arc>());

        Assert.Equal(130, arc.Center.X, 1);
        Assert.Equal(10, arc.Center.Y, 1);
        Assert.Equal(20, arc.Radius, 1);
    }

    [Fact]
    public void Ellipse_IsOneSplineNotFour()
    {
        var spline = Assert.Single(Read("20 60 m 20 71 38 80 60 80 c 82 80 100 71 100 60 c 100 49 82 40 60 40 c 38 40 20 49 20 60 c h S\n").OfType<Spline>());

        Assert.Equal(3, spline.Degree);
        Assert.Equal(13, spline.ControlPoints.Count);
        Assert.Equal(17, spline.Knots.Count);
        Assert.Equal([0, 0, 0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 4], spline.Knots);
        Assert.Equal(spline.ControlPoints[0], spline.ControlPoints[^1]);
    }

    [Fact]
    public void FlatBezier_BecomesALine()
    {
        var line = Assert.Single(Read("10 10 m 40 10 70 10 100 10 c S\n").OfType<Line>());

        Assert.Equal(10, line.StartPoint.X, 6);
        Assert.Equal(100, line.EndPoint.X, 6);
    }

    [Fact]
    public void SegmentsPaintedOneByOne_JoinIntoPolylines()
    {
        var entities = Read(
            "10 10 m 50 10 l S\n50 10 m 50 40 l S\n50 40 m 90 40 l S\n"
            + "100 10 m 140 10 l S\n"
            + "150 50 m 190 50 l S\n190 50 m 190 90 l S\n190 90 m 150 90 l S\n150 90 m 150 50 l S\n");

        Assert.Equal(3, entities.Count);
        var open = Assert.Single(entities.OfType<LwPolyline>(), p => !p.IsClosed);
        Assert.Equal(4, open.Vertices.Count);
        var closed = Assert.Single(entities.OfType<LwPolyline>(), p => p.IsClosed);
        Assert.Equal(4, closed.Vertices.Count);
        Assert.Single(entities.OfType<Line>());
    }

    [Fact]
    public void WithoutJoining_EverySegmentIsALine()
    {
        var entities = Read("10 10 m 50 10 l 50 40 l S\n50 40 m 90 40 l S\n", PointUnits with { JoinLines = false });

        Assert.Equal(3, entities.Count);
        Assert.All(entities, e => Assert.IsType<Line>(e));
    }

    [Fact]
    public void Junction_OfThreeSegments_IsNotJoined()
    {
        var entities = Read("10 10 m 50 10 l S\n50 10 m 90 10 l S\n50 10 m 50 50 l S\n");

        Assert.Equal(3, entities.OfType<Line>().Count());
    }

    [Fact]
    public void ThinFilledSliver_BecomesItsCentreLine()
    {
        var entities = Read("10 10 m 100 10 l 100 10.6 l 10 10.6 l h f\n120 20 40 40 re f\n");

        var line = Assert.Single(entities.OfType<Line>());
        Assert.Equal(10.3, line.StartPoint.Y, 6);
        Assert.Equal(10.3, line.EndPoint.Y, 6);
        Assert.Equal(90, Math.Abs(line.EndPoint.X - line.StartPoint.X), 6);
        var square = Assert.Single(entities.OfType<LwPolyline>());
        Assert.True(square.IsClosed);
    }

    [Fact]
    public void InvisibleText_IsLeftOut()
    {
        var text = Assert.Single(Read("BT /F1 12 Tf 3 Tr 20 20 Td (Tersembunyi) Tj ET\nBT /F1 12 Tf 0 Tr 20 60 Td (Terlihat) Tj ET\n").OfType<TextEntity>());

        Assert.Equal("Terlihat", text.Value);
    }

    [Fact]
    public void ClipWindow_CutsLinesAndDropsWhatIsOutside()
    {
        var entities = Read(
            "q 20 20 100 60 re W n\n"
            + "0 50 m 200 50 l S\n"      // crosses the window: cut to it
            + "150 30 m 190 70 l S\n"    // outside the window: gone
            + "Q\n"
            + "300 300 m 400 400 l S\n"  // off the page: gone
            + "-50 50 m 250 50 l S\n");  // cut to the page

        var lines = entities.OfType<Line>().OrderBy(l => Math.Min(l.StartPoint.X, l.EndPoint.X)).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal((0, 200), (Math.Min(lines[0].StartPoint.X, lines[0].EndPoint.X), Math.Max(lines[0].StartPoint.X, lines[0].EndPoint.X)));
        Assert.Equal((20, 120), (Math.Min(lines[1].StartPoint.X, lines[1].EndPoint.X), Math.Max(lines[1].StartPoint.X, lines[1].EndPoint.X)));
    }

    [Fact]
    public void FrameDrawnOutsideItsViewport_IsDropped()
    {
        var entities = Read(
            "q 50 20 100 60 re W n\n"
            + "10 10 180 80 re S\n"   // frame around the viewport, invisible inside it
            + "60 30 m 140 70 l S\n"
            + "Q\n"
            + "50 20 100 60 re S\n"); // viewport border, drawn after the clip ends

        Assert.Equal(2, entities.Count);
        var border = Assert.Single(entities.OfType<LwPolyline>());
        Assert.Equal(50, border.Vertices.Min(v => v.Location.X), 6);
        Assert.Single(entities.OfType<Line>());
    }

    [Fact]
    public void FormXObject_FallsBackToClippingByThePage()
    {
        var entities = Read("/Fx Do\n", form: "0 0 m 30 0 l S\n-500 -500 m -400 -400 l S\n", formMatrix: "1 0 0 1 50 20");

        var line = Assert.Single(entities.OfType<Line>());
        Assert.Equal(50, line.StartPoint.X, 6);
        Assert.Equal(20, line.StartPoint.Y, 6);
        Assert.Equal(80, line.EndPoint.X, 6);
    }

    [Fact]
    public void StrokeColour_BecomesTheNearestIndexColour()
    {
        var entities = Read("1 0 0 RG 10 10 m 60 10 l S\n0 0 1 RG 10 20 m 60 20 l S\n0 G 10 30 m 60 30 l S\n");

        Assert.Equal([1, 5, 7], entities.OfType<Line>().OrderBy(l => l.StartPoint.Y).Select(l => l.Color.Index));
    }

    [Fact]
    public void FillAndStrokeOfTheSameShape_AndTheBackground_AreDrawnOnce()
    {
        var entities = Read("0 0 200 100 re f\n30 30 m 80 30 l 80 70 l h f\n30 30 m 80 30 l 80 70 l h S\n");

        var triangle = Assert.Single(entities);
        Assert.True(((LwPolyline)triangle).IsClosed);
    }

    [Fact]
    public void TriangulatedFill_BecomesOneOutline()
    {
        // Solid fills arrive as triangles sharing an edge; drawn one by one they put a diagonal across the shape.
        var entities = Read("0 g 10 10 m 90 10 l 90 60 l h 10 10 m 90 60 l 10 60 l h f\n");

        var outline = Assert.Single(entities);
        var polyline = Assert.IsType<LwPolyline>(outline);
        Assert.True(polyline.IsClosed);
        Assert.Equal(4, polyline.Vertices.Count);
    }

    [Fact]
    public void FillStrokedInItsOwnColour_IsASolidShape()
    {
        var entities = Read("0 g 0 G 10 10 m 90 10 l 90 60 l h 10 10 m 90 60 l 10 60 l h b\n");

        var polyline = Assert.IsType<LwPolyline>(Assert.Single(entities));
        Assert.Equal(4, polyline.Vertices.Count);
    }

    [Fact]
    public void WhitePaint_IsInvisibleOnPaper()
    {
        var entities = Read("1 g 1 G 10 10 m 90 10 l 90 60 l h 10 10 m 90 60 l 10 60 l h b\n1 G 5 5 190 90 re S\n0 G 20 20 m 40 20 l S\n");

        var line = Assert.IsType<Line>(Assert.Single(entities));
        Assert.Equal(20, line.StartPoint.X, 6);
    }

    [Fact]
    public void HiddenTextOverGlyphOutlines_BecomesTextAndTheOutlinesGo()
    {
        // The word "II" drawn as two filled bars, with the same word laid over it invisibly for searching.
        var entities = Read(
            "0 g 20 20 m 23 20 l 23 28 l 20 28 l h f\n26 20 m 29 20 l 29 28 l 26 28 l h f\n"
            + "BT /F1 12 Tf 3 Tr 20 20 Td (II) Tj ET\n");

        var text = Assert.IsType<TextEntity>(Assert.Single(entities));
        Assert.Equal("II", text.Value);
    }

    [Fact]
    public void HiddenTextWithNothingUnderIt_IsStillLeftOut() =>
        Assert.Empty(Read("BT /F1 12 Tf 3 Tr 20 20 Td (OCR) Tj ET\n"));

    [Fact]
    public void WordsOnOneBaseline_AreOneText_ButTheNextColumnIsNot()
    {
        var entities = Read("BT /F1 8 Tf 20 20 Td (NAMA JEMBATAN :) Tj ET\nBT /F1 8 Tf 120 20 Td (KERTAS :) Tj ET\nBT /F1 8 Tf 20 40 Td (NAMA TEAM) Tj ET\n");

        Assert.Equal(["KERTAS :", "NAMA JEMBATAN :", "NAMA TEAM"], entities.OfType<TextEntity>().Select(t => t.Value).Order());
    }

    [Fact]
    public void Extents_CoverTheDrawing()
    {
        var document = new ACadSharp.CadDocument(PdfToCadConverter.OutputVersion);
        foreach (var entity in Read("10 10 m 60 40 l S\n" + TestPdf.Circle(100, 50, 20) + "S\n"))
        {
            document.Entities.Add(entity);
        }

        PdfToCadConverter.SetExtents(document);

        Assert.Equal(10, document.Header.ModelSpaceExtMin.X, 6);
        Assert.Equal(10, document.Header.ModelSpaceExtMin.Y, 6);
        Assert.Equal(120, document.Header.ModelSpaceExtMax.X, 6);
        Assert.Equal(70, document.Header.ModelSpaceExtMax.Y, 6);
    }

    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    public void Output_ReadsBackWithEveryEntity(string extension)
    {
        var document = new ACadSharp.CadDocument(PdfToCadConverter.OutputVersion);
        var content = "10 10 m 60 10 l S\n" + TestPdf.Circle(100, 50, 20) + "S\n"
            + "150 10 m 150 21.046 141.046 30 130 30 c S\n"
            + "20 60 m 20 71 38 80 60 80 c 82 80 100 71 100 60 c S\n"
            + "BT /F1 12 Tf 120 15 Td (Denah) Tj ET\n";
        foreach (var entity in Read(content))
        {
            document.Entities.Add(entity);
        }

        var path = _temp.File("out" + extension);
        File.WriteAllBytes(path, CadFiles.Write(document, extension));
        var back = CadFiles.Read(path, extension);

        Assert.Equal(["ARC", "CIRCLE", "LINE", "SPLINE", "TEXT"], back.Entities.Select(e => e.ObjectName).Order());
    }
}

public sealed class PolygonMergerTests
{
    [Fact]
    public void TrianglesSharingAnEdge_MergeIntoARectangle()
    {
        var merged = PolygonMerger.Merge([new XY[] { new(0, 0), new(10, 0), new(10, 5) }, new XY[] { new(0, 0), new(10, 5), new(0, 5) }], 0.01);

        var ring = Assert.Single(merged);
        Assert.Equal(4, ring.Count);
        Assert.Contains(new XY(0, 5), ring);
    }

    [Fact]
    public void Fan_OfFourTriangles_MergesIntoOneRing_WithoutCollinearCorners()
    {
        XY c = new(5, 5);
        XY[] corners = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        var fan = Enumerable.Range(0, 4).Select(i => (IReadOnlyList<XY>)new[] { c, corners[i], corners[(i + 1) % 4] }).ToList();

        var merged = PolygonMerger.Merge(fan, 0.01);

        Assert.Equal(4, Assert.Single(merged).Count);
    }

    [Fact]
    public void SeparateShapes_StaySeparate()
    {
        var merged = PolygonMerger.Merge([new XY[] { new(0, 0), new(10, 0), new(10, 5) }, new XY[] { new(20, 0), new(30, 5), new(20, 5) }], 0.01);

        Assert.Equal(2, merged.Count);
    }
}

public sealed class PolylineJoinerTests
{
    [Fact]
    public void ChainsMeetingEndToEnd_BecomeOneRun()
    {
        var joiner = new PolylineJoiner(0.01);
        joiner.Add([new XY(0, 0), new XY(10, 0)], 0);
        joiner.Add([new XY(10, 10), new XY(10, 0)], 0);   // reversed: still joins
        joiner.Add([new XY(10, 10), new XY(20, 10)], 0);

        var (points, _, closed) = Assert.Single(joiner.Join());

        Assert.False(closed);
        Assert.Equal(4, points.Count);
        Assert.Equal(new XY(0, 0), points[0]);
        Assert.Equal(new XY(20, 10), points[^1]);
    }

    [Fact]
    public void Loop_ClosesWithoutRepeatingTheFirstPoint()
    {
        var joiner = new PolylineJoiner(0.01);
        joiner.Add([new XY(0, 0), new XY(10, 0)], 0);
        joiner.Add([new XY(10, 0), new XY(10, 10)], 0);
        joiner.Add([new XY(10, 10), new XY(0, 10)], 0);
        joiner.Add([new XY(0, 10), new XY(0, 0)], 0);

        var (points, _, closed) = Assert.Single(joiner.Join());

        Assert.True(closed);
        Assert.Equal(4, points.Count);
    }

    [Fact]
    public void DifferentGroups_DoNotJoin()
    {
        var joiner = new PolylineJoiner(0.01);
        joiner.Add([new XY(0, 0), new XY(10, 0)], 0);
        joiner.Add([new XY(10, 0), new XY(20, 0)], 1);

        Assert.Equal(2, joiner.Join().Count());
    }
}

public sealed class ClipAndColourTests
{
    [Theory]
    [InlineData(-50, 50, 250, 50, 0, 50, 200, 50)]
    [InlineData(50, -50, 50, 150, 50, 0, 50, 100)]
    [InlineData(10, 10, 90, 90, 10, 10, 90, 90)]
    public void ClipSegment_KeepsThePartInside(double x0, double y0, double x1, double y1, double cx0, double cy0, double cx1, double cy1)
    {
        var clipped = CadGeometry.ClipSegment(new XY(x0, y0), new XY(x1, y1), new Rect(0, 0, 200, 100));

        Assert.NotNull(clipped);
        Assert.Equal(cx0, clipped.Value.From.X, 6);
        Assert.Equal(cy0, clipped.Value.From.Y, 6);
        Assert.Equal(cx1, clipped.Value.To.X, 6);
        Assert.Equal(cy1, clipped.Value.To.Y, 6);
    }

    [Fact]
    public void ClipSegment_DropsWhatIsOutside() =>
        Assert.Null(CadGeometry.ClipSegment(new XY(300, 300), new XY(400, 400), new Rect(0, 0, 200, 100)));

    [Theory]
    [InlineData(0, 0, 0, 7)]
    [InlineData(255, 255, 255, 7)]
    [InlineData(255, 0, 0, 1)]
    [InlineData(0, 0, 255, 5)]
    [InlineData(0, 200, 0, 3)]
    [InlineData(128, 128, 128, 8)]
    [InlineData(255, 128, 0, 30)]
    public void Colours_MapToTheNearestIndex(int r, int g, int b, int expected) =>
        Assert.Equal(expected, CadColors.FromRgb((byte)r, (byte)g, (byte)b).Index);

    [Fact]
    public void FlatBezier_IsRecognized()
    {
        Assert.True(CadGeometry.IsFlat(new XY(0, 0), new XY(30, 0), new XY(60, 0), new XY(100, 0)));
        Assert.False(CadGeometry.IsFlat(new XY(0, 0), new XY(30, 20), new XY(60, 20), new XY(100, 0)));
    }
}

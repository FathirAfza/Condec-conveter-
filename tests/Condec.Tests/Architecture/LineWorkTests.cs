// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Architecture;

namespace Condec.Tests.Architecture;

/// <summary>Strokes become lines, arcs and circles; solid ink keeps its outline (DESIGN §6.3.1).</summary>
public class LineWorkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<DrawingPrimitive> Extract(SyntheticDrawing drawing)
    {
        var ink = drawing.Ink();
        var blobs = ConnectedComponents.Label(ink, out var labels);
        return [.. LineWorkExtractor.Extract(ink, labels, blobs, _ => true, 1.0, Ct).SelectMany(b => b.Primitives)];
    }

    /// <summary>A point as the drawing was drawn (y down).</summary>
    private static (double X, double Y) Page(SyntheticDrawing drawing, (double X, double Y) point) => (point.X, drawing.Height - point.Y);

    [Fact]
    public void AThickStraightStroke_IsOneLineAlongItsMiddle()
    {
        var drawing = new SyntheticDrawing(200, 60).Line(20, 30, 180, 30, thickness: 5);

        var line = Assert.IsType<PolylinePrimitive>(Assert.Single(Extract(drawing)));

        Assert.True(line.IsLine);
        var ends = line.Points.Select(p => Page(drawing, p)).OrderBy(p => p.X).ToList();
        Assert.InRange(ends[0].Y, 29, 31);
        Assert.InRange(ends[1].Y, 29, 31);
        Assert.InRange(ends[0].X, 18, 26);
        Assert.InRange(ends[1].X, 174, 182);
    }

    [Fact]
    public void ADiagonalStroke_IsOneLine()
    {
        var drawing = new SyntheticDrawing(160, 160).Line(20, 20, 140, 120, thickness: 3);

        var line = Assert.IsType<PolylinePrimitive>(Assert.Single(Extract(drawing)));

        Assert.True(line.IsLine);
    }

    [Fact]
    public void TwoLinesThatMeetAtACorner_AreOnePolylineWithThreePoints()
    {
        var drawing = new SyntheticDrawing(200, 160).Line(20, 20, 20, 130, 3).Line(20, 130, 170, 130, 3);

        var polyline = Assert.IsType<PolylinePrimitive>(Assert.Single(Extract(drawing)));

        Assert.False(polyline.IsClosed);
        Assert.Equal(3, polyline.Points.Count);
        var corner = Page(drawing, polyline.Points[1]);
        Assert.InRange(corner.X, 18, 24);
        Assert.InRange(corner.Y, 126, 132);
    }

    [Fact]
    public void ATJunction_IsThreeStrokes()
    {
        var drawing = new SyntheticDrawing(200, 160).Line(20, 80, 180, 80, 3).Line(100, 80, 100, 150, 3);

        var primitives = Extract(drawing);

        Assert.Equal(3, primitives.Count);
        Assert.All(primitives, p => Assert.True(Assert.IsType<PolylinePrimitive>(p).IsLine));
    }

    [Fact]
    public void ARectangleOfStrokes_IsOneClosedPolylineOfFourCorners()
    {
        var drawing = new SyntheticDrawing(220, 160).Rectangle(30, 30, 190, 120, 3);

        var polyline = Assert.IsType<PolylinePrimitive>(Assert.Single(Extract(drawing)));

        Assert.True(polyline.IsClosed);
        Assert.Equal(4, polyline.Points.Count);
    }

    [Fact]
    public void AnArcOfADoorSwing_IsAnArcWithItsRadiusAndSweep()
    {
        // A quarter circle of radius 60 about (40, 20), from straight down to straight right.
        var drawing = new SyntheticDrawing(160, 120).Arc(40, 20, 60, 0, 90, thickness: 2);

        var arc = Assert.IsType<ArcPrimitive>(Assert.Single(Extract(drawing)));

        Assert.InRange(arc.Radius, 58, 62);
        Assert.InRange(arc.Sweep * 180 / Math.PI, 80, 100);
        var center = Page(drawing, (arc.CenterX, arc.CenterY));
        Assert.InRange(center.X, 37, 43);
        Assert.InRange(center.Y, 17, 23);
    }

    [Fact]
    public void ACircleOfStrokes_IsACircle()
    {
        var drawing = new SyntheticDrawing(160, 160).Circle(80, 80, 50, thickness: 3);

        var circle = Assert.IsType<CirclePrimitive>(Assert.Single(Extract(drawing)));

        Assert.InRange(circle.Radius, 48, 52);
        var center = Page(drawing, (circle.CenterX, circle.CenterY));
        Assert.InRange(center.X, 78, 82);
        Assert.InRange(center.Y, 78, 82);
    }

    [Fact]
    public void ADoorLeafAndItsArc_AreALineAndAnArc()
    {
        // The leaf is the radius to the start of the swing: a line from the hinge, then the arc.
        var drawing = new SyntheticDrawing(160, 140)
            .Line(40, 20, 40, 80, 2)
            .Arc(40, 20, 60, 0, 90, 2);

        var primitives = Extract(drawing);

        Assert.Contains(primitives, p => p is ArcPrimitive { Radius: > 56 and < 64 });
        Assert.Contains(primitives, p => p is PolylinePrimitive { IsLine: true });
    }

    [Fact]
    public void SolidInk_KeepsItsOutline_AsAClosedPolyline()
    {
        var drawing = new SyntheticDrawing(120, 100).Fill(30, 20, 90, 80);

        var polyline = Assert.IsType<PolylinePrimitive>(Assert.Single(Extract(drawing)));

        Assert.True(polyline.IsClosed);
        Assert.Equal(4, polyline.Points.Count);
        var xs = polyline.Points.Select(p => p.X);
        Assert.InRange(xs.Min(), 29, 31);
        Assert.InRange(xs.Max(), 89, 91);
    }

    [Fact]
    public void ASpeck_IsNoise()
    {
        var drawing = new SyntheticDrawing(60, 60).Fill(20, 20, 22, 22);

        Assert.Empty(Extract(drawing));
    }

    [Fact]
    public void AShortStubAtACorner_IsNotAStroke()
    {
        // The thinning leaves a tiny spur where a thick stroke bends; it must not come out as a line of its own.
        var drawing = new SyntheticDrawing(200, 160).Line(20, 20, 20, 130, 7).Line(20, 130, 170, 130, 7);

        var primitives = Extract(drawing);

        var polyline = Assert.IsType<PolylinePrimitive>(Assert.Single(primitives));
        Assert.Equal(3, polyline.Points.Count);
    }
}

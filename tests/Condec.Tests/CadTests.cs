using Condec.Core.Cad;
using Condec.Core.Pdf;
using CSMath;

namespace Condec.Tests;

public sealed class CadGeometryTests
{
    /// <summary>The usual Bézier approximation of a quarter circle: control points at 0.5523 of the radius.</summary>
    private const double Kappa = 0.5522847498;

    [Fact]
    public void QuarterCircleBezier_BecomesAnArc()
    {
        var arc = CadGeometry.FitArc(new XY(10, 0), new XY(10, 10 * Kappa), new XY(10 * Kappa, 10), new XY(0, 10));

        Assert.NotNull(arc);
        Assert.Equal(0, arc.Value.Center.X, 3);
        Assert.Equal(0, arc.Value.Center.Y, 3);
        Assert.Equal(10, arc.Value.Radius, 3);
        Assert.Equal(0, arc.Value.StartAngle, 3);
        Assert.Equal(Math.PI / 2, arc.Value.EndAngle, 3);
    }

    [Fact]
    public void ClockwiseQuarter_IsStoredCounterClockwise()
    {
        var arc = CadGeometry.FitArc(new XY(0, 10), new XY(10 * Kappa, 10), new XY(10, 10 * Kappa), new XY(10, 0));

        Assert.NotNull(arc);
        Assert.Equal(0, arc.Value.StartAngle, 3);
        Assert.Equal(Math.PI / 2, arc.Value.EndAngle, 3);
    }

    [Fact]
    public void FourQuarters_WithSlightlyDifferentCenters_AreOneCircle()
    {
        ArcFit[] quarters =
        [
            new(new XY(0.02, 0), 10.01, 0, Math.PI / 2),
            new(new XY(0, 0.03), 9.98, Math.PI / 2, Math.PI),
            new(new XY(-0.02, 0), 10, Math.PI, 3 * Math.PI / 2),
            new(new XY(0, -0.01), 10.02, -Math.PI / 2, 0),
        ];

        var circle = CadGeometry.MergeCircle(quarters);

        Assert.NotNull(circle);
        Assert.Equal(10, circle.Value.Radius, 1);
    }

    [Fact]
    public void ThreeQuarters_AreNotACircle() =>
        Assert.Null(CadGeometry.MergeCircle([new(new XY(0, 0), 10, 0, Math.PI / 2), new(new XY(0, 0), 10, Math.PI / 2, Math.PI), new(new XY(0, 0), 10, Math.PI, 3 * Math.PI / 2)]));

    [Fact]
    public void NeighbouringArcs_Join_InBothDirections()
    {
        var first = new ArcFit(new XY(0, 0), 10, 0, Math.PI / 2);
        var second = new ArcFit(new XY(0, 0), 10, Math.PI / 2, Math.PI);

        Assert.Equal((0, Math.PI), CadGeometry.JoinArcs(first, second) is { } a ? (a.StartAngle, a.EndAngle) : default);
        Assert.Equal((0, Math.PI), CadGeometry.JoinArcs(second, first) is { } b ? (b.StartAngle, b.EndAngle) : default);
    }

    [Fact]
    public void ArcsOfDifferentCircles_StaySeparate() =>
        Assert.Null(CadGeometry.JoinArcs(new(new XY(0, 0), 10, 0, Math.PI / 2), new(new XY(0, 0), 12, Math.PI / 2, Math.PI)));

    [Theory]
    [InlineData(0, 0, 3, 5, 6, -5, 9, 0)]
    [InlineData(0, 0, 1, 0, 2, 0, 3, 0)]
    [InlineData(0, 0, 0, 10, 10, 10, 10, 20)]
    public void OtherCurves_StaySplines(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3) =>
        Assert.Null(CadGeometry.FitArc(new XY(x0, y0), new XY(x1, y1), new XY(x2, y2), new XY(x3, y3)));

    [Theory]
    [InlineData(0, 10, 20, 10, 20)]
    [InlineData(90, 10, 20, 20, 90)]
    [InlineData(180, 10, 20, 90, 180)]
    [InlineData(270, 10, 20, 180, 10)]
    public void PageRotation_FollowsTheViewer(int rotation, double x, double y, double expectedX, double expectedY)
    {
        // A 100 × 200 pt page. At 90 the page shows 200 wide and 100 high, turned clockwise.
        var transform = new PageTransform(0, 0, 100, 200, rotation, 1);

        var mapped = transform.Map(x, y);

        Assert.Equal(expectedX, mapped.X, 6);
        Assert.Equal(expectedY, mapped.Y, 6);
    }

    [Theory]
    [InlineData(CadUnit.Millimeters, 1, 25.4 / 72)]
    [InlineData(CadUnit.Centimeters, 1, 2.54 / 72)]
    [InlineData(CadUnit.Meters, 100, 2.54 / 72)]
    [InlineData(CadUnit.Inches, 50, 50.0 / 72)]
    public void UnitsPerPoint_CombinesUnitAndScale(CadUnit unit, double scale, double expected) =>
        Assert.Equal(expected, new CadOptions(1, unit, scale).UnitsPerPoint, 9);
}

public sealed class CadWriterTests
{
    /// <summary>ACadSharp takes text rotation in radians and writes degrees to DXF group 50.</summary>
    [Fact]
    public void TextRotation_IsRadians()
    {
        var document = new ACadSharp.CadDocument(PdfToCadConverter.OutputVersion);
        document.Entities.Add(new ACadSharp.Entities.TextEntity("Denah") { Rotation = Math.PI / 2, Height = 2.5 });

        var lines = System.Text.Encoding.ASCII.GetString(PdfToCadConverter.Write(document, ".dxf")).Split('\n').Select(l => l.Trim()).ToList();
        var text = lines.IndexOf("TEXT");
        var group50 = lines.FindIndex(text, l => l == "50");

        Assert.Equal(90, double.Parse(lines[group50 + 1], System.Globalization.CultureInfo.InvariantCulture), 6);
    }
}

public sealed class ScanVectorizerTests
{
    [Fact]
    public void Otsu_SplitsInkFromPaper()
    {
        var pixels = Enumerable.Repeat((byte)240, 900).Concat(Enumerable.Repeat((byte)20, 100)).ToArray();

        var threshold = ScanVectorizer.OtsuThreshold(pixels);

        Assert.InRange(threshold, 21, 240);
    }

    [Fact]
    public void BlankPage_HasNoOutlines() =>
        Assert.Empty(ScanVectorizer.Trace(Image(40, 40, (_, _) => false), 1, CancellationToken.None));

    [Fact]
    public void Square_BecomesOneOutlineWithFourCorners()
    {
        var outlines = ScanVectorizer.Trace(Image(40, 40, (x, y) => x is >= 10 and < 30 && y is >= 5 and < 25), 1, CancellationToken.None);

        var outline = Assert.Single(outlines);
        Assert.InRange(outline.Count, 4, 8);
        Assert.Equal(20, outline.Max(p => p.X) - outline.Min(p => p.X), 0);
        Assert.Equal(20, outline.Max(p => p.Y) - outline.Min(p => p.Y), 0);
    }

    [Fact]
    public void Ring_HasAnOuterAndAnInnerOutline()
    {
        var outlines = ScanVectorizer.Trace(Image(60, 60, (x, y) => Math.Sqrt(Math.Pow(x - 30, 2) + Math.Pow(y - 30, 2)) is >= 12 and <= 20), 1, CancellationToken.None);

        Assert.Equal(2, outlines.Count);
    }

    [Fact]
    public void Specks_AreDropped()
    {
        var outlines = ScanVectorizer.Trace(Image(40, 40, (x, y) => (x, y) is (5, 5) or (30, 12) || (x is >= 10 and < 30 && y is >= 20 and < 30)), 1, CancellationToken.None);

        Assert.Single(outlines);
    }

    [Fact]
    public void InkTouchingTheEdge_StillCloses()
    {
        var outlines = ScanVectorizer.Trace(Image(20, 20, (x, _) => x < 10), 1, CancellationToken.None);

        Assert.Single(outlines);
    }

    private static GrayImage Image(int width, int height, Func<int, int, bool> ink)
    {
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = ink(x, y) ? (byte)0 : (byte)255;
            }
        }

        return new GrayImage(width, height, pixels);
    }
}

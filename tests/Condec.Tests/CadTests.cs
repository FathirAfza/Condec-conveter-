// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

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

        var lines = System.Text.Encoding.ASCII.GetString(CadFiles.Write(document, ".dxf")).Split('\n').Select(l => l.Trim()).ToList();
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

/// <summary>DXF to DWG and back for drawings saved by other programs, often in old DXF versions without tables.</summary>
public sealed class CadFileConverterTests : IDisposable
{
    private const string Line = "0\nLINE\n8\n0\n10\n0.0\n20\n0.0\n30\n0.0\n11\n100.0\n21\n50.0\n31\n0.0\n";

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Only a header with the version and an ENTITIES section, the way many exporters write DXF.</summary>
    private static string Dxf(string version, string entities) =>
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n" + version + "\n0\nENDSEC\n" +
        "0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n";

    private async Task<ACadSharp.CadDocument> ConvertAsync(string sourceText, string sourceExtension, string targetExtension)
    {
        var source = _dir.File("source" + sourceExtension);
        await File.WriteAllTextAsync(source, sourceText, Ct);
        return await ConvertFileAsync(source, sourceExtension, targetExtension);
    }

    private async Task<ACadSharp.CadDocument> ConvertFileAsync(string source, string sourceExtension, string targetExtension)
    {
        var target = _dir.File("target" + targetExtension);
        using (var output = new MemoryStream())
        {
            var request = new Condec.Core.Conversion.ConversionRequest(source, sourceExtension, targetExtension, output);
            await new CadFileConverter().ConvertAsync(request, new SyncProgress<Condec.Core.Conversion.ConversionProgress>(_ => { }), Ct);
            await File.WriteAllBytesAsync(target, output.ToArray(), Ct);
        }

        return CadFiles.Read(target, targetExtension);
    }

    /// <summary>ACadSharp can't write DWG R12, R13 or R2007, so those move to a version it can write.</summary>
    [Theory]
    [InlineData("AC1006", ACadSharp.ACadVersion.AC1015)]
    [InlineData("AC1009", ACadSharp.ACadVersion.AC1015)]
    [InlineData("AC1012", ACadSharp.ACadVersion.AC1015)]
    [InlineData("AC1014", ACadSharp.ACadVersion.AC1014)]
    [InlineData("AC1018", ACadSharp.ACadVersion.AC1018)]
    [InlineData("AC1021", ACadSharp.ACadVersion.AC1024)]
    [InlineData("AC1032", ACadSharp.ACadVersion.AC1032)]
    public async Task Dxf_ToDwg_WritesAVersionDwgSupports(string dxfVersion, ACadSharp.ACadVersion expected)
    {
        var dwg = await ConvertAsync(Dxf(dxfVersion, Line), ".dxf", ".dwg");

        Assert.Equal(expected, dwg.Header.Version);
        var line = Assert.IsType<ACadSharp.Entities.Line>(Assert.Single(dwg.Entities));
        Assert.Equal(100, line.EndPoint.X, 9);
        Assert.Equal(50, line.EndPoint.Y, 9);
    }

    /// <summary>
    /// An R12 DXF without tables whose entities name a layer, a linetype and a text style it never defines:
    /// DwgWriter needs the standard entries, so they are created on read.
    /// </summary>
    [Fact]
    public async Task R12Dxf_WithoutTables_ConvertsToDwg()
    {
        const string entities =
            "0\nLINE\n8\nTRACE\n6\nDASHED\n62\n3\n10\n0\n20\n0\n11\n10\n21\n10\n" +
            "0\nPOLYLINE\n8\nTRACE\n66\n1\n70\n1\n0\nVERTEX\n8\nTRACE\n10\n0\n20\n0\n0\nVERTEX\n8\nTRACE\n10\n5\n20\n0\n" +
            "0\nVERTEX\n8\nTRACE\n10\n5\n20\n5\n0\nSEQEND\n8\nTRACE\n" +
            "0\nTEXT\n8\nTRACE\n10\n1\n20\n1\n40\n2.5\n1\nHalo\n7\nROMANS\n";

        var dwg = await ConvertAsync(Dxf("AC1009", entities), ".dxf", ".dwg");

        Assert.Equal(3, dwg.Entities.Count);
        Assert.All(dwg.Entities, e => Assert.Equal("TRACE", e.Layer.Name));
        Assert.Contains(dwg.Entities, e => e is ACadSharp.Entities.Polyline2D);
        Assert.Equal("Halo", Assert.Single(dwg.Entities.OfType<ACadSharp.Entities.TextEntity>()).Value);
    }

    /// <summary>
    /// An INSERT with ATTRIBs closed by a SEQEND, a common DXF layout. ACadSharp's reader leaves the SEQEND in the
    /// drawing as an entity, which its writers reject with NotImplementedException ("Entity not implemented: Seqend").
    /// </summary>
    [Theory]
    [InlineData("AC1009")]
    [InlineData("AC1015")]
    [InlineData("AC1027")]
    public async Task Dxf_WithAStraySeqend_ConvertsToDwg(string version)
    {
        const string entities =
            "0\nLINE\n8\n0\n10\n0\n20\n0\n11\n10\n21\n10\n" +
            "0\nINSERT\n8\n0\n2\nB\n66\n1\n10\n0\n20\n0\n" +
            "0\nATTRIB\n8\n0\n10\n0\n20\n0\n40\n1\n1\nX\n2\nT\n70\n0\n0\nSEQEND\n8\n0\n" +
            "0\nLINE\n8\n0\n10\n0\n20\n0\n11\n1\n21\n1\n0\nSEQEND\n8\n0\n";

        var dwg = await ConvertAsync(Dxf(version, entities), ".dxf", ".dwg");

        Assert.DoesNotContain(dwg.Entities, e => e is ACadSharp.Entities.Seqend);
        Assert.Equal(2, dwg.Entities.OfType<ACadSharp.Entities.Line>().Count());
    }

    /// <summary>
    /// An R10 DXF (AC1006) of POLYLINEs, what image-to-DXF tools write. ACadSharp reads an AC1006 POLYLINE as
    /// placeholders, so Condec relabels the file as R12 before reading it.
    /// </summary>
    [Fact]
    public async Task R10Dxf_Polylines_ConvertToDwg()
    {
        const string polyline =
            "0\nPOLYLINE\n8\n0\n66\n1\n70\n1\n" +
            "0\nVERTEX\n8\n0\n10\n0.0\n20\n0.0\n42\n0.25\n0\nVERTEX\n8\n0\n10\n5.0\n20\n0.0\n42\n0.0\n" +
            "0\nVERTEX\n8\n0\n10\n5.0\n20\n5.0\n42\n0.0\n0\nSEQEND\n";

        var dwg = await ConvertAsync(Dxf("AC1006", polyline + polyline), ".dxf", ".dwg");

        Assert.Equal(2, dwg.Entities.OfType<ACadSharp.Entities.Polyline2D>().Count());
        Assert.All(dwg.Entities.OfType<ACadSharp.Entities.Polyline2D>(), p => Assert.Equal(3, p.Vertices.Count));
        Assert.Equal(2, dwg.Entities.Count);
    }

    [Theory]
    [InlineData("AC1006", "AC1009")]
    [InlineData("AC1004", "AC1009")]
    [InlineData("AC1009", "AC1009")]
    [InlineData("AC1015", "AC1015")]
    public void AsR12_RelabelsOnlyDxfOlderThanR12(string from, string to)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(Dxf(from, Line));

        var result = System.Text.Encoding.ASCII.GetString(CadFiles.AsR12(bytes));

        Assert.Equal(Dxf(to, Line), result);
    }

    [Fact]
    public async Task Dwg_ToDxf_KeepsTheDrawing()
    {
        var dwg = _dir.File("drawing.dwg");
        await File.WriteAllBytesAsync(dwg, CadFiles.Write(CadFiles.Read(await WriteSourceAsync(Dxf("AC1018", Line)), ".dxf"), ".dwg"), Ct);

        var dxf = await ConvertFileAsync(dwg, ".dwg", ".dxf");

        Assert.Equal(ACadSharp.ACadVersion.AC1018, dxf.Header.Version);
        Assert.IsType<ACadSharp.Entities.Line>(Assert.Single(dxf.Entities));
    }

    [Fact]
    public void DxfWriter_GetsAVersionItSupports() =>
        Assert.Equal(PdfToCadConverter.OutputVersion, CadFiles.WritableVersion(ACadSharp.ACadVersion.AC1009, ".dxf"));

    /// <summary>DwgReader starts at R14; an R12 DWG gets a message the app can explain, not a raw library exception.</summary>
    [Fact]
    public async Task OldDwg_IsReportedAsAnUnsupportedVersion()
    {
        var path = _dir.File("old.dwg");
        var bytes = new byte[256];
        System.Text.Encoding.ASCII.GetBytes("AC1009").CopyTo(bytes, 0);
        await File.WriteAllBytesAsync(path, bytes, Ct);

        Assert.Throws<UnsupportedCadVersionException>(() => CadFiles.Read(path, ".dwg"));
    }

    private async Task<string> WriteSourceAsync(string text)
    {
        var path = _dir.File("source.dxf");
        await File.WriteAllTextAsync(path, text, Ct);
        return path;
    }
}

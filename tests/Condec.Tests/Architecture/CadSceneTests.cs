// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Condec.Core.Architecture;
using Condec.Core.Cad;
using CSMath;

namespace Condec.Tests.Architecture;

public sealed class CadSceneTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CadScene Flatten(CadDocument cad) => CadFlattener.Flatten(cad, Ct);

    private static CadDocument NewDrawing() => new(ACadVersion.AC1015);

    private static Layer AddLayer(CadDocument cad, string name, short color = 1, bool on = true)
    {
        var layer = new Layer(name) { Color = new Color(color), IsOn = on };
        cad.Layers.Add(layer);
        return layer;
    }

    [Fact]
    public void ALineBecomesAPathOfTwoPoints()
    {
        var cad = NewDrawing();
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(100, 50, 0)));

        var path = Assert.Single(Flatten(cad).Paths);

        Assert.Equal([(0.0, 0.0), (100.0, 50.0)], path.Points);
        Assert.False(path.IsClosed);
    }

    [Fact]
    public void AClosedPolylineIsAClosedPath()
    {
        var cad = NewDrawing();
        cad.Entities.Add(new LwPolyline([new XY(0, 0), new XY(10, 0), new XY(10, 10)]) { IsClosed = true });

        var path = Assert.Single(Flatten(cad).Paths);

        Assert.True(path.IsClosed);
        Assert.Equal(3, path.Points.Count);
    }

    [Fact]
    public void ABulgeBecomesAnArc()
    {
        // A bulge of 1 is a half circle: from (0,0) to (10,0) bulging to the left of the way it runs, so down.
        var cad = NewDrawing();
        var polyline = new LwPolyline([new XY(0, 0), new XY(10, 0)]);
        polyline.Vertices[0].Bulge = 1;
        cad.Entities.Add(polyline);

        var path = Assert.Single(Flatten(cad).Paths);

        Assert.True(path.Points.Count > 10);
        var lowest = path.Points.Min(p => p.Y);
        Assert.Equal(-5, lowest, 1);
        Assert.All(path.Points, p => Assert.Equal(5, Math.Sqrt(Math.Pow(p.X - 5, 2) + (p.Y * p.Y)), 6));
    }

    [Fact]
    public void AnArcStaysOnItsCircleAndKeepsItsEnds()
    {
        var cad = NewDrawing();
        cad.Entities.Add(new Arc { Center = new XYZ(10, 20, 0), Radius = 5, StartAngle = 0, EndAngle = Math.PI / 2 });

        var points = Assert.Single(Flatten(cad).Paths).Points;

        Assert.Equal((15.0, 20.0), (Math.Round(points[0].X, 6), Math.Round(points[0].Y, 6)));
        Assert.Equal((10.0, 25.0), (Math.Round(points[^1].X, 6), Math.Round(points[^1].Y, 6)));
        Assert.All(points, p => Assert.Equal(5, Math.Sqrt(Math.Pow(p.X - 10, 2) + Math.Pow(p.Y - 20, 2)), 6));
    }

    [Fact]
    public void ACircleIsClosed()
    {
        var cad = NewDrawing();
        cad.Entities.Add(new Circle { Center = new XYZ(0, 0, 0), Radius = 3 });

        Assert.True(Assert.Single(Flatten(cad).Paths).IsClosed);
    }

    [Fact]
    public void ABlockInsertIsExpandedWithItsScaleRotationAndPosition()
    {
        var cad = NewDrawing();
        var block = new BlockRecord("DOOR");
        block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)));
        cad.BlockRecords.Add(block);
        cad.Entities.Add(new Insert(block) { InsertPoint = new XYZ(100, 200, 0), XScale = 2, YScale = 2, Rotation = Math.PI / 2 });

        var points = Assert.Single(Flatten(cad).Paths).Points;

        // The unit line, doubled and turned a quarter: from the insertion point straight up by 2.
        Assert.Equal(100, points[0].X, 6);
        Assert.Equal(200, points[0].Y, 6);
        Assert.Equal(100, points[1].X, 6);
        Assert.Equal(202, points[1].Y, 6);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(40, 0)]
    public void NestedBlocksAreFollowedOnlyAsDeepAsIsSane(int levels, int expectedPaths)
    {
        // Each block holds an insert of the one before; the innermost holds the line. A file that nests blocks into
        // each other without end must not take the program down with it.
        var cad = NewDrawing();
        var inner = new BlockRecord("LEVEL0");
        inner.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)));
        cad.BlockRecords.Add(inner);
        for (var level = 1; level < levels; level++)
        {
            var outer = new BlockRecord("LEVEL" + level);
            outer.Entities.Add(new Insert(inner));
            cad.BlockRecords.Add(outer);
            inner = outer;
        }

        cad.Entities.Add(new Insert(inner));

        Assert.Equal(expectedPaths, Flatten(cad).Paths.Count);
    }

    [Fact]
    public void ArrayInsertsRepeatTheBlock()
    {
        var cad = NewDrawing();
        var block = new BlockRecord("TILE");
        block.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)));
        cad.BlockRecords.Add(block);
        cad.Entities.Add(new Insert(block) { RowCount = 2, ColumnCount = 3, RowSpacing = 10, ColumnSpacing = 5 });

        Assert.Equal(6, Flatten(cad).Paths.Count);
    }

    [Fact]
    public void TextKeepsItsValueHeightAndPlace()
    {
        var cad = NewDrawing();
        cad.Entities.Add(new TextEntity("Kamar") { InsertPoint = new XYZ(5, 6, 0), Height = 2.5, Rotation = 0.5 });

        var label = Assert.Single(Flatten(cad).Labels);

        Assert.Equal("Kamar", label.Text);
        Assert.Equal((5.0, 6.0), (label.X, label.Y));
        Assert.Equal(2.5, label.Height);
        Assert.Equal(0.5, label.Rotation, 6);
    }

    [Fact]
    public void TextInAPlainCadFontIsSetNarrowerThanHelvetica()
    {
        var cad = NewDrawing();
        cad.Entities.Add(new TextEntity("Kamar") { InsertPoint = new XYZ(0, 0, 0), Height = 2 });
        cad.Entities.Add(new TextEntity("Kamar") { InsertPoint = new XYZ(0, 5, 0), Height = 2, WidthFactor = 0.5 });

        var labels = Flatten(cad).Labels;

        Assert.Equal(CadFlattener.PlainFontWidthFactor, labels[0].WidthFactor);
        Assert.Equal(CadFlattener.PlainFontWidthFactor * 0.5, labels[1].WidthFactor, 9);
    }

    [Fact]
    public void TextInATrueTypeFontKeepsHelveticaWidth()
    {
        var cad = NewDrawing();
        var style = new TextStyle("ARIAL") { Filename = "arial.ttf" };
        cad.TextStyles.Add(style);
        cad.Entities.Add(new TextEntity("Kamar") { InsertPoint = new XYZ(0, 0, 0), Height = 2, Style = style });

        Assert.Equal(1, Assert.Single(Flatten(cad).Labels).WidthFactor);
    }

    [Fact]
    public void ThePdfSetsTheTextWidthInPercent()
    {
        var pdf = Pdf(OneLine(100, 100));

        Assert.Contains($"{CadFlattener.PlainFontWidthFactor * 100} Tz", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void TextInAScaledBlockIsScaled()
    {
        var cad = NewDrawing();
        var block = new BlockRecord("TAG");
        block.Entities.Add(new TextEntity("A") { InsertPoint = new XYZ(1, 0, 0), Height = 2 });
        cad.BlockRecords.Add(block);
        cad.Entities.Add(new Insert(block) { XScale = 3, YScale = 3 });

        var label = Assert.Single(Flatten(cad).Labels);

        Assert.Equal(6, label.Height, 6);
        Assert.Equal(3, label.X, 6);
    }

    [Fact]
    public void AnEntityTakesItsLayersColorUnlessItHasItsOwn()
    {
        var cad = NewDrawing();
        var layer = AddLayer(cad, "WALLS", color: 1);
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)) { Layer = layer });
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)) { Layer = layer, Color = new Color(3) });

        var paths = Flatten(cad).Paths;

        Assert.Equal(new Rgb(255, 0, 0), paths[0].Color);
        Assert.NotEqual(paths[0].Color, paths[1].Color);
        Assert.All(paths, p => Assert.Equal("WALLS", p.Layer));
    }

    [Fact]
    public void WhiteIsDrawnBlackOnPaper()
    {
        var cad = NewDrawing();
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)) { Color = new Color(7) });
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)) { Color = new Color(255, 255, 255) });

        Assert.All(Flatten(cad).Paths, p => Assert.Equal(Rgb.Black, p.Color));
    }

    [Fact]
    public void ALayerThatIsOffIsReportedOffAndItsShapesAreKept()
    {
        var cad = NewDrawing();
        var layer = AddLayer(cad, "HIDDEN", on: false);
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1, 0, 0)) { Layer = layer });

        var scene = Flatten(cad);

        Assert.False(scene.Layers.Single(l => l.Name == "HIDDEN").IsOn);
        Assert.Single(scene.Paths);
    }

    [Fact]
    public void ASolidIsAFilledShape()
    {
        var cad = NewDrawing();
        cad.Entities.Add(new Solid(new XYZ(0, 0, 0), new XYZ(10, 0, 0), new XYZ(0, 10, 0), new XYZ(10, 10, 0)));

        var path = Assert.Single(Flatten(cad).Paths);

        Assert.True(path.IsFilled);
        Assert.Equal(4, path.Points.Count);
    }

    /// <summary>A solid 10 × 10 square with a 4 × 4 square island, as a logo is written.</summary>
    private static Hatch SquareWithAHole(HatchStyleType style)
    {
        var hatch = new Hatch { IsSolid = true, Style = style };
        hatch.Paths.Add(new Hatch.BoundaryPath([new Hatch.BoundaryPath.Polyline([new XYZ(0, 0, 0), new XYZ(10, 0, 0), new XYZ(10, 10, 0), new XYZ(0, 10, 0)], true)])
        {
            Flags = BoundaryPathFlags.Polyline | BoundaryPathFlags.External,
        });
        hatch.Paths.Add(new Hatch.BoundaryPath([new Hatch.BoundaryPath.Polyline([new XYZ(3, 3, 0), new XYZ(7, 3, 0), new XYZ(7, 7, 0), new XYZ(3, 7, 0)], true)])
        {
            Flags = BoundaryPathFlags.Polyline,
        });
        return hatch;
    }

    [Fact]
    public void ASolidHatchWithAnIslandIsOneFillWithAHole()
    {
        var cad = NewDrawing();
        cad.Entities.Add(SquareWithAHole(HatchStyleType.Normal));

        var path = Assert.Single(Flatten(cad).Paths);

        Assert.True(path.IsFilled);
        Assert.Contains((10.0, 10.0), path.Points);
        var hole = Assert.Single(path.Holes!);
        Assert.Contains((3.0, 3.0), hole);
        Assert.Contains((7.0, 7.0), hole);
    }

    [Fact]
    public void AHatchThatIgnoresItsIslandsFillsEveryOutline()
    {
        var cad = NewDrawing();
        cad.Entities.Add(SquareWithAHole(HatchStyleType.Ignore));

        var paths = Flatten(cad).Paths;

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.True(p.IsFilled && p.Holes is null));
    }

    [Fact]
    public void BoundsCoverWhatIsShown()
    {
        var cad = NewDrawing();
        var shown = AddLayer(cad, "SHOWN");
        var hidden = AddLayer(cad, "HIDDEN");
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(10, 5, 0)) { Layer = shown });
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(1000, 1000, 0)) { Layer = hidden });

        var scene = Flatten(cad);

        Assert.Equal((0.0, 0.0, 10.0, 5.0), scene.BoundsOf(name => name == "SHOWN"));
        Assert.Null(scene.BoundsOf(_ => false));
    }

    // --- the PDF ---

    private static CadScene OneLine(double x2, double y2, string text = "Denah")
    {
        var cad = NewDrawing();
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(x2, y2, 0)));
        cad.Entities.Add(new TextEntity(text) { InsertPoint = new XYZ(0, 0, 0), Height = 2 });
        return Flatten(cad);
    }

    private static string Pdf(CadScene scene, PaperSize paper = PaperSize.A3) =>
        Encoding.Latin1.GetString(CadPdfWriter.Write(scene, _ => true, paper));

    [Fact]
    public void ThePdfIsAOnePagePdfOnTheChosenSheet()
    {
        var pdf = Pdf(OneLine(1000, 500), PaperSize.A3);

        Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
        Assert.Contains("/Count 1", pdf, StringComparison.Ordinal);
        Assert.Contains("/MediaBox [0 0 1190.551 841.89]", pdf, StringComparison.Ordinal);
        Assert.Contains("/MediaBox [0 0 841.89 595.276]", Pdf(OneLine(1000, 500), PaperSize.A4), StringComparison.Ordinal);
    }

    [Fact]
    public void ATallDrawingGetsAPortraitSheet()
    {
        Assert.Contains("/MediaBox [0 0 841.89 1190.551]", Pdf(OneLine(500, 1000)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDrawingIsFittedInsideTheMarginsAndCentered()
    {
        var pdf = Pdf(OneLine(1000, 500));
        var numbers = Regex.Matches(pdf, @"([\d.]+) ([\d.]+) [ml]\n")
            .Select(m => (X: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), Y: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();
        Assert.Equal(2, numbers.Count);

        // 420 × 297 mm sheet, 10 mm margin: a 2:1 drawing is limited by the width, so it spans 400 mm and is 200 mm high, centered.
        var margin = CadPdfWriter.MarginMm * CadPdfWriter.PointsPerMillimeter;
        Assert.Equal(margin, numbers[0].X, 2);
        Assert.Equal(1190.551 - margin, numbers[1].X, 2);
        Assert.Equal((841.89 / 2) - (200 * CadPdfWriter.PointsPerMillimeter / 2), numbers[0].Y, 2);
    }

    [Fact]
    public void TextIsWrittenInHelveticaWithItsSpecialCharactersEscaped()
    {
        var pdf = Pdf(OneLine(100, 100, "Kamar (A) \\ é ✓"));

        Assert.Contains("/BaseFont /Helvetica", pdf, StringComparison.Ordinal);
        Assert.Contains("(Kamar \\(A\\) \\\\ é ?) Tj", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void CurlyQuotesDashesAndTheEuroSign_KeepTheirWinAnsiCodes()
    {
        // Before, every letter past U+00FF became "?", so the curly quotes of the owner's drawing (2026-10-04) showed as "?".
        var pdf = Pdf(OneLine(100, 100, "“Denah” – ‘A’ 5 €"));

        Assert.Contains("(\u0093Denah\u0094 \u0096 \u0091A\u0092 5 \u0080) Tj", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCharacters_BecomeSpaces() =>
        Assert.Equal("a b c", CadPdfWriter.Escape("a\tb\u0085c"));

    [Fact]
    public void ALayerThatIsNotShownIsNotInThePdf()
    {
        var cad = NewDrawing();
        var layer = AddLayer(cad, "OFF");
        cad.Entities.Add(new Line(new XYZ(0, 0, 0), new XYZ(100, 0, 0)));
        cad.Entities.Add(new Line(new XYZ(0, 50, 0), new XYZ(100, 50, 0)) { Layer = layer });
        var scene = Flatten(cad);

        var pdf = Encoding.Latin1.GetString(CadPdfWriter.Write(scene, name => name != "OFF", PaperSize.A4));

        Assert.Single(Regex.Matches(pdf, @"\nS\n"));
    }

    [Fact]
    public void TheHoleOfAFillStaysOpenInThePdf()
    {
        var cad = NewDrawing();
        cad.Entities.Add(SquareWithAHole(HatchStyleType.Normal));

        var pdf = Pdf(Flatten(cad));

        // Both outlines in one path, filled even-odd.
        Assert.Equal(2, Regex.Matches(pdf, @" m\n").Count);
        Assert.Single(Regex.Matches(pdf, @"\nf\*\n"));
        Assert.DoesNotMatch(@"\nf\n", pdf);
    }

    [Fact]
    public void NothingShownIsAnError()
    {
        var scene = OneLine(100, 100);

        Assert.Throws<NothingToConvertException>(() => CadPdfWriter.Write(scene, _ => false, PaperSize.A4));
    }

    [Fact]
    public void ThePdfCrossReferenceTablePointsAtItsObjects()
    {
        var bytes = CadPdfWriter.Write(OneLine(100, 100), _ => true, PaperSize.A3);
        var text = Encoding.Latin1.GetString(bytes);
        var tableAt = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);

        Assert.StartsWith("xref", text[tableAt..], StringComparison.Ordinal);
        foreach (Match entry in Regex.Matches(text[tableAt..], @"(\d{10}) 00000 n"))
        {
            var offset = int.Parse(entry.Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.Matches(@"^\d+ 0 obj", text[offset..]);
        }
    }
}

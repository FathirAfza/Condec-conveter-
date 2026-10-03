// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using Condec.Core.Architecture;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Documents;
using Condec.Core.Imaging;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using CSMath;

namespace Condec.Tests.Imaging;

/// <summary>A real DXF drawn to a PDF, PNG and JPG through the Windows PDF renderer and imaging component.</summary>
public sealed class CadRenderWindowsTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Drawing()
    {
        // A 400 × 200 mm drawing: a red frame on layer FRAME and a thick blue bar on layer BAR.
        var cad = new CadDocument(ACadVersion.AC1015);
        var frame = new Layer("FRAME") { Color = new Color(1) };
        var bar = new Layer("BAR") { Color = new Color(5) };
        cad.Layers.Add(frame);
        cad.Layers.Add(bar);
        cad.Entities.Add(new LwPolyline([new XY(0, 0), new XY(400, 0), new XY(400, 200), new XY(0, 200)]) { IsClosed = true, Layer = frame, LineWeight = LineWeightType.W100 });
        cad.Entities.Add(new Solid(new XYZ(100, 80, 0), new XYZ(300, 80, 0), new XYZ(100, 120, 0), new XYZ(300, 120, 0)) { Layer = bar });
        cad.Entities.Add(new TextEntity("Denah lantai") { InsertPoint = new XYZ(20, 20, 0), Height = 10, Layer = frame });
        var path = _dir.File("gambar.dxf");
        File.WriteAllBytes(path, CadFiles.Write(cad, ".dxf"));
        return path;
    }

    private ConversionPipeline Pipeline() => new(
        new ConverterRegistry([new CadRenderConverter()], [new PdfOutputValidator(), new ImageOutputValidator()]),
        new TempFileJournal(_dir.File("journal")));

    private async Task<string> RunAsync(string target, CadRenderOptions options)
    {
        var destination = _dir.File("hasil" + Guid.NewGuid().ToString("N")[..6] + target);
        await Pipeline().RunAsync(new ConversionJob(Drawing(), target, destination, options), null, Ct);
        return destination;
    }

    private static CadRenderOptions Options(PaperSize paper = PaperSize.A3, int dpi = 150, bool transparent = false, params string[] hidden) =>
        new(paper, dpi, transparent, new HashSet<string>(hidden));

    private static async Task<ImageConverter.DecodedImage> DecodeAsync(string path) => await ImageConverter.DecodeAsync(path, Ct);

    private static (byte B, byte G, byte R, byte A) Pixel(ImageConverter.DecodedImage image, int x, int y)
    {
        var at = (int)((y * image.Width) + x) * 4;
        return (image.Pixels[at], image.Pixels[at + 1], image.Pixels[at + 2], image.Pixels[at + 3]);
    }

    private static int DarkPixels(ImageConverter.DecodedImage image)
    {
        var count = 0;
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            if (image.Pixels[i + 3] > 0 && (image.Pixels[i] + image.Pixels[i + 1] + image.Pixels[i + 2]) / 3 < 160)
            {
                count++;
            }
        }

        return count;
    }

    [Fact]
    public async Task ThePdfIsOnePageOfTheChosenSheet()
    {
        var path = await RunAsync(".pdf", Options(PaperSize.A4));

        Assert.Equal(1, PdfInspector.CountPages(path));
    }

    [Theory]
    [InlineData(PaperSize.A3, 150, 2480, 1754)]
    [InlineData(PaperSize.A4, 150, 1754, 1240)]
    [InlineData(PaperSize.A3, 96, 1587, 1123)]
    public async Task ThePictureHasThePixelsOfTheSheetAtTheResolution(PaperSize paper, int dpi, int width, int height)
    {
        var image = await DecodeAsync(await RunAsync(".png", Options(paper, dpi)));

        Assert.InRange((int)image.Width, width - 2, width + 2);
        Assert.InRange((int)image.Height, height - 2, height + 2);
        Assert.Equal(dpi, image.DpiX, 1);
    }

    [Fact]
    public async Task ThePaperIsWhiteAndTheDrawingIsDrawnOnIt()
    {
        var image = await DecodeAsync(await RunAsync(".png", Options()));

        Assert.Equal((255, 255, 255, 255), Pixel(image, 2, 2));
        Assert.True(DarkPixels(image) > 1000);

        // The blue bar is in the middle of the sheet.
        var (b, g, r, a) = Pixel(image, (int)image.Width / 2, (int)image.Height / 2);
        Assert.True(b > r + 60, $"expected blue, got r{r} g{g} b{b}");
        Assert.Equal(255, a);
    }

    [Fact]
    public async Task ATransparentBackgroundIsTransparentWhereNothingIsDrawn()
    {
        var image = await DecodeAsync(await RunAsync(".png", Options(transparent: true)));

        Assert.Equal(0, Pixel(image, 2, 2).A);
        Assert.Equal(255, Pixel(image, (int)image.Width / 2, (int)image.Height / 2).A);
    }

    [Fact]
    public async Task AJpgCannotBeTransparentSoItIsWhite()
    {
        var image = await DecodeAsync(await RunAsync(".jpg", Options(transparent: true)));

        var (b, g, r, a) = Pixel(image, 2, 2);
        Assert.True(r > 250 && g > 250 && b > 250 && a == 255, $"{r},{g},{b},{a}");
    }

    [Fact]
    public async Task ALayerThatIsHiddenIsNotDrawn()
    {
        var all = DarkPixels(await DecodeAsync(await RunAsync(".png", Options())));
        var withoutBar = await DecodeAsync(await RunAsync(".png", Options(hidden: "BAR")));

        Assert.True(DarkPixels(withoutBar) < all * 0.9, $"{DarkPixels(withoutBar)} of {all}");
        var (b, g, r, _) = Pixel(withoutBar, (int)withoutBar.Width / 2, (int)withoutBar.Height / 2);
        Assert.True(r > 240 && g > 240 && b > 240, "the middle should be paper again");
    }

    [Fact]
    public async Task EveryLayerHiddenIsAnErrorNotABlankSheet()
    {
        await Assert.ThrowsAsync<NothingToConvertException>(() => RunAsync(".pdf", Options(hidden: ["FRAME", "BAR", "0"])));
    }

    [Fact]
    public async Task NoTemporaryPdfIsLeftBehind()
    {
        var before = Directory.GetFiles(Path.GetTempPath(), "condec-*.pdf").Length;

        await RunAsync(".png", Options());

        Assert.Equal(before, Directory.GetFiles(Path.GetTempPath(), "condec-*.pdf").Length);
    }

    [Theory]
    [InlineData(".dwg", true)]
    [InlineData(".dxf", true)]
    [InlineData(".png", false)]
    public void TheSourcesAreTheCadFiles(string source, bool offered)
    {
        Assert.Equal(offered ? [".pdf", ".png", ".jpg"] : [], new CadRenderConverter().GetTargets(source));
    }
}

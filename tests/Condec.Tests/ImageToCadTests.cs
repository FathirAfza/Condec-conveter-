// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.Entities;
using Condec.Core.Cad;
using Condec.Core.Conversion;

namespace Condec.Tests;

public sealed class ImageToCadTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeRasterizer(GrayPicture image, params string[] extensions) : IImageRasterizer
    {
        public bool CanDecode(string extension) => extensions.Contains(extension);

        public Task<GrayPicture> ReadAsync(string path, CancellationToken ct) => Task.FromResult(image);
    }

    private sealed class RecordedProgress : IProgress<ConversionProgress>
    {
        public List<ConversionProgress> Reports { get; } = [];

        public void Report(ConversionProgress value) => Reports.Add(value);
    }

    /// <summary>White paper with black filled rectangles, each given as x, y, width, height in pixels.</summary>
    private static GrayImage Paper(int width, int height, byte background, byte ink, params (int X, int Y, int W, int H)[] rectangles)
    {
        var pixels = new byte[width * height];
        Array.Fill(pixels, background);
        foreach (var (x0, y0, w, h) in rectangles)
        {
            for (var y = y0; y < y0 + h; y++)
            {
                for (var x = x0; x < x0 + w; x++)
                {
                    pixels[(y * width) + x] = ink;
                }
            }
        }

        return new GrayImage(width, height, pixels);
    }

    private async Task<(CadDocument Cad, List<ConversionProgress> Progress)> ConvertAsync(
        GrayPicture image, string target, string source = ".png")
    {
        var converter = new ImageToCadConverter(new FakeRasterizer(image, source));
        using var output = new MemoryStream();
        var progress = new RecordedProgress();
        await converter.ConvertAsync(new ConversionRequest("picture" + source, source, target, output), progress, Ct);

        var path = _dir.File("result" + target);
        await File.WriteAllBytesAsync(path, output.ToArray(), Ct);
        return (CadFiles.Read(path, target), progress.Reports);
    }

    private static (double Left, double Bottom, double Right, double Top) Bounds(LwPolyline outline)
    {
        var xs = outline.Vertices.Select(v => v.Location.X).ToList();
        var ys = outline.Vertices.Select(v => v.Location.Y).ToList();
        return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    [Fact]
    public async Task Rectangles_BecomeClosedOutlinesInMillimeters()
    {
        // 254 dpi: one pixel is 0.1 mm.
        var image = new GrayPicture(Paper(200, 100, 255, 0, (20, 10, 60, 40), (120, 50, 30, 30)), 254, 254);

        var (cad, _) = await ConvertAsync(image, ".dxf");

        var outlines = cad.Entities.OfType<LwPolyline>().ToList();
        Assert.Equal(2, outlines.Count);
        Assert.All(outlines, o => Assert.True(o.IsClosed));
        Assert.Equal(ACadSharp.Types.Units.UnitsType.Millimeters, cad.Header.InsUnits);

        // The first rectangle: 60 × 40 pixels is 6 × 4 mm, its left edge 2 mm in. Y runs up in the drawing,
        // so its top (pixel row 10) sits 100 - 10 = 90 pixels, 9 mm, above the bottom of the picture.
        var first = outlines.Select(Bounds).Single(b => Math.Abs(b.Right - b.Left - 6) < 0.3);
        Assert.InRange(first.Left, 1.8, 2.2);
        Assert.InRange(first.Top - first.Bottom, 3.7, 4.3);
        Assert.InRange(first.Top, 8.7, 9.3);
    }

    [Fact]
    public async Task Dwg_IsMadeFromTheDxf()
    {
        var image = new GrayPicture(Paper(200, 100, 255, 0, (20, 10, 60, 40), (120, 50, 30, 30)), 254, 254);

        var (cad, progress) = await ConvertAsync(image, ".dwg");

        Assert.Equal(2, cad.Entities.OfType<LwPolyline>().Count());
        Assert.Equal(ACadVersion.AC1015, cad.Header.Version);
        Assert.Contains(progress, p => p.Detail == Core.Localization.Loc.Get("Progress.ConvertingDxfToDwg", System.Globalization.CultureInfo.CurrentUICulture));
    }

    [Fact]
    public async Task Dxf_SkipsTheDwgStep()
    {
        var image = new GrayPicture(Paper(100, 100, 255, 0, (10, 10, 30, 30)), 96, 96);

        var (_, progress) = await ConvertAsync(image, ".dxf");

        Assert.DoesNotContain(progress, p => p.Detail == Core.Localization.Loc.Get("Progress.ConvertingDxfToDwg", System.Globalization.CultureInfo.CurrentUICulture));
    }

    [Fact]
    public async Task LightShapesOnADarkBackground_AreTracedNotTheBackground()
    {
        var image = new GrayPicture(Paper(200, 100, 0, 255, (60, 30, 80, 40)), 254, 254);

        var (cad, _) = await ConvertAsync(image, ".dxf");

        // One outline around the light rectangle (8 × 4 mm), not the picture's frame plus a hole.
        var outline = Assert.Single(cad.Entities.OfType<LwPolyline>());
        var bounds = Bounds(outline);
        Assert.InRange(bounds.Right - bounds.Left, 7.7, 8.3);
        Assert.InRange(bounds.Top - bounds.Bottom, 3.7, 4.3);
    }

    [Fact]
    public async Task ALargePicture_KeepsItsSize()
    {
        // 6000 × 400 pixels at 600 dpi is 254 × 17 mm; tracing works on a copy reduced by a factor of 2.
        var image = new GrayPicture(Paper(6000, 400, 255, 0, (600, 100, 3000, 200)), 600, 600);

        var (cad, _) = await ConvertAsync(image, ".dxf");

        var bounds = Bounds(Assert.Single(cad.Entities.OfType<LwPolyline>()));
        Assert.InRange(bounds.Right - bounds.Left, 126, 128);
        Assert.InRange(bounds.Top - bounds.Bottom, 8, 8.8);
    }

    [Theory]
    [InlineData(0.0, 96.0)]
    [InlineData(-5.0, 96.0)]
    [InlineData(double.NaN, 96.0)]
    [InlineData(150.0, 150.0)]
    [InlineData(1.0, 10.0)]
    [InlineData(100000.0, 2400.0)]
    public void Resolution_FallsBackWhenTheFileDoesNotSayOrIsAbsurd(double dpi, double expected) =>
        Assert.Equal(expected, ImageToCadConverter.UsableDpi(dpi));

    [Theory]
    [InlineData(255)]
    [InlineData(0)]
    [InlineData(128)]
    public async Task AFlatPicture_HasNothingToTrace(byte shade)
    {
        var image = new GrayPicture(new GrayImage(50, 50, Enumerable.Repeat(shade, 2500).ToArray()), 96, 96);

        await Assert.ThrowsAsync<NothingToTraceException>(() => ConvertAsync(image, ".dxf"));
    }

    [Fact]
    public void Targets_AreOfferedOnlyForPicturesThisMachineCanDecode()
    {
        var converter = new ImageToCadConverter(new FakeRasterizer(new GrayPicture(new GrayImage(1, 1, [0]), 96, 96), ".png", ".heic"));

        Assert.Equal([".dxf", ".dwg"], converter.GetTargets(".png"));
        Assert.Equal([".dxf", ".dwg"], converter.GetTargets(".HEIC"));
        Assert.Empty(converter.GetTargets(".jpg"));
        Assert.Empty(converter.GetTargets(".pdf"));
    }

    [Fact]
    public void ReduceTo_AveragesWholeBlocks()
    {
        var image = new GrayImage(4, 2, [0, 100, 200, 200, 100, 200, 200, 200]);

        var (reduced, factor) = image.ReduceTo(2);

        Assert.Equal(2, factor);
        Assert.Equal((2, 1), (reduced.Width, reduced.Height));
        Assert.Equal([100, 200], reduced.Pixels);
    }

    [Fact]
    public void ReduceTo_LeavesASmallPictureAlone()
    {
        var image = new GrayImage(4, 2, new byte[8]);

        var (reduced, factor) = image.ReduceTo(10);

        Assert.Same(image, reduced);
        Assert.Equal(1, factor);
    }
}

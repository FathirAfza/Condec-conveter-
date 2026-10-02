// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp.Entities;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Imaging;
using Condec.Core.Pipeline;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Tests.Imaging;

/// <summary>A real PNG and JPEG through the Windows Imaging Component, the tracer and the whole pipeline.</summary>
public sealed class ImageToCadWindowsTests : IDisposable
{
    private const uint Width = 200;
    private const uint Height = 100;
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(".png", ".dxf")]
    [InlineData(".png", ".dwg")]
    [InlineData(".jpg", ".dxf")]
    [InlineData(".jpg", ".dwg")]
    public async Task BlackRectangleOnWhite_BecomesOneOutline(string source, string target)
    {
        var sourcePath = _dir.File("gambar" + source);
        await WriteRectangleAsync(sourcePath, source == ".png" ? BitmapEncoder.PngEncoderId : BitmapEncoder.JpegEncoderId);
        var destination = _dir.File("hasil" + target);

        await CreatePipeline().RunAsync(new ConversionJob(sourcePath, target, destination), null, Ct);

        var outline = Assert.Single(CadFiles.Read(destination, target).Entities.OfType<LwPolyline>());
        Assert.True(outline.IsClosed);

        // 96 dpi: the 80 × 40 pixel rectangle is about 21.2 × 10.6 mm.
        var xs = outline.Vertices.Select(v => v.Location.X).ToList();
        var ys = outline.Vertices.Select(v => v.Location.Y).ToList();
        Assert.InRange(xs.Max() - xs.Min(), 20.5, 22);
        Assert.InRange(ys.Max() - ys.Min(), 10, 11.2);
    }

    [Fact]
    public void Targets_AreOfferedForEveryPictureTypeWindowsCanDecode()
    {
        var converter = new ImageToCadConverter(new WicImageRasterizer());

        Assert.Equal([".dxf", ".dwg"], converter.GetTargets(".png"));
        Assert.Equal([".dxf", ".dwg"], converter.GetTargets(".jpg"));
        Assert.Empty(converter.GetTargets(".pdf"));
        Assert.Empty(converter.GetTargets(".docx"));
    }

    [Fact]
    public async Task ABlankPicture_FailsWithNothingToTrace()
    {
        var path = _dir.File("kosong.png");
        await WriteRectangleAsync(path, BitmapEncoder.PngEncoderId, drawRectangle: false);

        await Assert.ThrowsAsync<NothingToTraceException>(() =>
            CreatePipeline().RunAsync(new ConversionJob(path, ".dxf", _dir.File("kosong.dxf")), null, Ct));
        Assert.False(File.Exists(_dir.File("kosong.dxf")));
    }

    private ConversionPipeline CreatePipeline() => new(
        new ConverterRegistry([new ImageToCadConverter(new WicImageRasterizer())], [new CadOutputValidator()]),
        new TempFileJournal(_dir.File("journal")));

    /// <summary>White picture, 200 × 100 pixels at 96 dpi, with an opaque black rectangle of 80 × 40 pixels.</summary>
    private static async Task WriteRectangleAsync(string path, Guid encoderId, bool drawRectangle = true)
    {
        var pixels = new byte[Width * Height * 4];
        Array.Fill(pixels, (byte)255);
        if (drawRectangle)
        {
            for (var y = 30; y < 70; y++)
            {
                for (var x = 60; x < 140; x++)
                {
                    var i = (int)((y * Width) + x) * 4;
                    pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
                }
            }
        }

        using var staging = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, staging);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, Width, Height, 96, 96, pixels);
        await encoder.FlushAsync();

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        using var file = File.Create(path);
        await encoded.CopyToAsync(file, Ct);
    }
}

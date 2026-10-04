// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.IO.Compression;
using ACadSharp;
using ACadSharp.Entities;
using Condec.Core.Architecture;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Imaging;
using Condec.Core.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Tests;

/// <summary>The pictures of a PDF read with the Windows Imaging Component, as the app reads them (owner decision 2026-10-04).</summary>
public sealed class PdfPictureWindowsTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>One drawing unit per point.</summary>
    private static readonly CadOptions Points = new(1, CadUnit.Inches, 72);

    private static async Task<byte[]> Jpeg(RasterPicture picture)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream).AsTask(Ct);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)picture.Width, (uint)picture.Height, 96, 96, picture.Bgra);
        await encoder.FlushAsync().AsTask(Ct);
        var bytes = new byte[stream.Size];
        using var read = stream.GetInputStreamAt(0).AsStreamForRead();
        await read.ReadExactlyAsync(bytes, Ct);
        return bytes;
    }

    private static byte[] Packed(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private async Task<List<Hatch>> Hatches(string path)
    {
        using var output = new MemoryStream();
        var request = new ConversionRequest(path, ".pdf", ".dxf", output, Points);
        await new PdfToCadConverter(null, new WindowsPictureDecoder()).ConvertAsync(request, new Progress<ConversionProgress>(), Ct);
        Assert.Empty(request.Notes.Items);
        return CadFiles.ReadDxf(output.ToArray()).Entities.OfType<Hatch>().ToList();
    }

    private static double Away(Color color, int r, int g, int b)
    {
        var rgb = Color.GetIndexRGB((byte)color.Index);
        return Math.Sqrt(((rgb[0] - r) * (rgb[0] - r)) + ((rgb[1] - g) * (rgb[1] - g)) + ((rgb[2] - b) * (rgb[2] - b)));
    }

    [Fact]
    public async Task TheDecoderReadsAJpegAndShrinksItWhileReading()
    {
        var jpeg = await Jpeg(PdfToCadTests.Halves(400, 200));

        var picture = await new WindowsPictureDecoder().DecodeAsync(jpeg, 20_000, Ct);

        Assert.NotNull(picture);
        Assert.Equal((200, 100), (picture.Width, picture.Height));
        var left = ((50 * 200) + 10) * 4;
        var right = ((50 * 200) + 190) * 4;
        Assert.True(picture.Bgra[left + 2] > 180 && picture.Bgra[left + 1] < 80, "the left half is red");
        Assert.True(picture.Bgra[right] > 230 && picture.Bgra[right + 1] > 230 && picture.Bgra[right + 2] > 230, "the right half is white");
    }

    [Fact]
    public async Task BytesThatAreNoPicture_GiveNothing()
    {
        Assert.Null(await new WindowsPictureDecoder().DecodeAsync([1, 2, 3, 4], 20_000, Ct));
    }

    [Fact]
    public async Task AJpegPackedInFlate_BecomesARedArea()
    {
        // The way the owner's drawing (2026-10-04) stores its corner logos: a JPEG, packed once more with Flate.
        var jpeg = await Jpeg(PdfToCadTests.Halves(40, 20));
        var path = TestPdf.Write(
            _dir.File("logo.pdf"),
            "q 40 0 0 20 100 50 cm /Im Do Q\n10 10 m 60 10 l S\n",
            picture: ("/Width 40 /Height 20 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter [/FlateDecode /DCTDecode]", Packed(jpeg)));

        var hatches = await Hatches(path);

        Assert.NotEmpty(hatches);
        Assert.Contains(hatches, h => Away(h.Color, 220, 30, 30) < 60);
        var bounds = hatches.Select(PdfToCadTests.BoundsOf).ToList();
        Assert.InRange(bounds.Min(b => b.MinX), 99, 101);
        Assert.InRange(bounds.Max(b => b.MaxX), 119, 122);
    }

    [Fact]
    public async Task APictureWithASoftMask_LeavesWhatTheMaskHidesOpen()
    {
        // 20 × 10 red pixels; the mask hides the left half.
        var red = Enumerable.Range(0, 200).SelectMany(_ => new byte[] { 220, 30, 30 }).ToArray();
        var mask = Enumerable.Range(0, 200).Select(i => i % 20 < 10 ? (byte)0 : (byte)255).ToArray();
        var path = TestPdf.Write(
            _dir.File("masked.pdf"),
            "q 40 0 0 20 100 50 cm /Im Do Q\n10 10 m 60 10 l S\n",
            picture: ("/Width 20 /Height 10 /ColorSpace /DeviceRGB /BitsPerComponent 8", red),
            softMask: (20, 10, mask));

        var hatch = Assert.Single(await Hatches(path));

        var (minX, _, maxX, _) = PdfToCadTests.BoundsOf(hatch);
        Assert.Equal(120, minX, 6);
        Assert.Equal(140, maxX, 6);
    }
}

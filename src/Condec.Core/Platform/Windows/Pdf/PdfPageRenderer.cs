// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Condec.Core.Pdf;

/// <summary>Renders PDF pages with the Windows PDF renderer (Windows.Data.Pdf).</summary>
public sealed class PdfPageRenderer : IPdfPageRasterizer
{
    /// <summary>A rendered page as straight BGRA pixels on a white background.</summary>
    internal sealed record RenderedPage(byte[] Bgra, uint Width, uint Height);

    /// <param name="pageNumber">1-based.</param>
    /// <param name="pixelsPerPoint">Resolution: 1 point is 1/72 inch, so 200 dpi is 200 / 72.</param>
    internal static async Task<RenderedPage> RenderAsync(string path, int pageNumber, double pixelsPerPoint, CancellationToken ct)
    {
        // Through StorageFile, not a wrapped .NET stream: see PdfOutputValidator.
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(ct).ConfigureAwait(false);
        var document = await PdfDocument.LoadFromFileAsync(file).AsTask(ct).ConfigureAwait(false);
        if (document.IsPasswordProtected)
        {
            throw new LockedPdfException("The PDF is password protected.");
        }

        using var page = document.GetPage((uint)(pageNumber - 1));

        // PdfPage.Size is in DIPs (1/96 inch; an A4 page at 200 dpi renders 1653 × 2339) and follows /Rotate.
        var pixelsPerDip = pixelsPerPoint * 72 / 96;
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * pixelsPerDip)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * pixelsPerDip)),
        };

        // A page is rendered whole, at 4 bytes per pixel. Say so before the minutes it would take, not after.
        var pixelCount = (long)options.DestinationWidth * options.DestinationHeight;
        if (pixelCount > Imaging.ImageTooLargeException.MaximumPixels)
        {
            throw new Imaging.ImageTooLargeException(pixelCount);
        }

        try
        {
            return await RenderPageAsync(page, options, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (Imaging.ImageConverter.IsOutOfMemory(ex))
        {
            throw new Imaging.ImageTooLargeException(pixelCount, ex);
        }
    }

    private static async Task<RenderedPage> RenderPageAsync(PdfPage page, PdfPageRenderOptions options, CancellationToken ct)
    {
        using var rendered = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(rendered, options).AsTask(ct).ConfigureAwait(false);

        var decoder = await BitmapDecoder.CreateAsync(rendered).AsTask(ct).ConfigureAwait(false);

        // Some Windows builds render larger than the destination size asked for (seen on 10.0.26340 with the display
        // at 175%: 1.4×). The page is then scaled to the size that was asked for, so the resolution stays as chosen.
        var transform = new BitmapTransform();
        if (decoder.PixelWidth != options.DestinationWidth || decoder.PixelHeight != options.DestinationHeight)
        {
            transform.ScaledWidth = options.DestinationWidth;
            transform.ScaledHeight = options.DestinationHeight;
            transform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);

        var bgra = pixels.DetachPixelData();
        Imaging.ImageConverter.FlattenOntoWhite(bgra);
        return new RenderedPage(bgra, options.DestinationWidth, options.DestinationHeight);
    }

    async Task<GrayImage> IPdfPageRasterizer.RenderAsync(string path, int pageNumber, double pixelsPerPoint, CancellationToken ct)
    {
        var page = await RenderAsync(path, pageNumber, pixelsPerPoint, ct).ConfigureAwait(false);
        var gray = new byte[page.Width * page.Height];
        for (var i = 0; i < gray.Length; i++)
        {
            var b = page.Bgra[i * 4];
            var g = page.Bgra[(i * 4) + 1];
            var r = page.Bgra[(i * 4) + 2];
            gray[i] = (byte)(((299 * r) + (587 * g) + (114 * b) + 500) / 1000);
        }

        return new GrayImage((int)page.Width, (int)page.Height, gray);
    }
}

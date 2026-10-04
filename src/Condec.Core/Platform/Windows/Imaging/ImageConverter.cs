// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Core.Imaging;

/// <summary>
/// JPG, PNG, BMP, GIF and TIFF to each other, plus HEIC and WebP as sources when their Windows
/// decoders are installed. A multi-page TIFF converts the page that <see cref="PageOptions"/> names (one result per page,
/// DESIGN §6.1.1); without it, and for an animated GIF, the first frame is used and the result says so. A source that is cut off (see <see cref="ImageStructure"/>) is converted as far as Windows can read it,
/// and the result says that too.
/// </summary>
public sealed class ImageConverter : IConverter
{
    private readonly Lazy<HashSet<Guid>> _installedDecoders = new(() =>
        [.. BitmapDecoder.GetDecoderInformationEnumerator().Select(codec => codec.CodecId)]);

    public IReadOnlyList<string> GetTargets(string sourceExtension)
    {
        var decoderId = ImageFormats.FindDecoder(FileExtension.Normalize(sourceExtension));
        if (decoderId is null || !_installedDecoders.Value.Contains(decoderId.Value))
        {
            return [];
        }

        return [.. ImageFormats.Targets.Where(target => target.DecoderId != decoderId).Select(target => target.Extension)];
    }

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var target = ImageFormats.FindTarget(FileExtension.Normalize(request.TargetExtension))
            ?? throw new NotSupportedException($"'{request.TargetExtension}' is not an image target.");

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0));
        if (ImageStructure.IsComplete(request.SourcePath) == false)
        {
            request.Notes.Add(NoteSeverity.Warning, Loc.Get("Note.SourceIncomplete"));
        }

        var page = (request.Options as PageOptions)?.PageNumber;
        var image = await DecodeAsync(request.SourcePath, ct, page is { } number ? number - 1 : 0).ConfigureAwait(false);
        if (page is null && image.FrameCount > 1)
        {
            request.Notes.Add(NoteSeverity.Informational, Loc.Format("Note.FirstFrameOnly", image.FrameCount));
        }

        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));

        if (!target.KeepsTransparency)
        {
            FlattenOntoWhite(image.Pixels);
        }

        ct.ThrowIfCancellationRequested();
        progress.Report(new ConversionProgress(ConversionStage.Encode, 0));

        using var staging = await EncodeAsync(target, image.Pixels, image.Width, image.Height, image.DpiX, image.DpiY, ct).ConfigureAwait(false);

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0.6));

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        await encoded.CopyToAsync(request.Output, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }

    /// <summary>
    /// Encodes BGRA pixels in memory. BitmapEncoder needs a seekable stream, so callers copy the finished image to the
    /// pipeline's forward-only output in one pass, where it is hashed.
    /// </summary>
    internal static async Task<InMemoryRandomAccessStream> EncodeAsync(
        ImageFormats.Target target, byte[] pixels, uint width, uint height, double dpiX, double dpiY, CancellationToken ct)
    {
        var staging = new InMemoryRandomAccessStream();
        try
        {
            var encoder = await BitmapEncoder.CreateAsync(target.EncoderId, staging).AsTask(ct).ConfigureAwait(false);
            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                target.KeepsTransparency ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
                width,
                height,
                dpiX,
                dpiY,
                pixels);
            await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
            return staging;
        }
        catch (Exception ex)
        {
            staging.Dispose();
            if (IsOutOfMemory(ex))
            {
                throw new ImageTooLargeException((long)width * height, ex);
            }

            throw;
        }
    }

    /// <param name="frameIndex">The frame (TIFF page) to read, from 0.</param>
    internal static async Task<DecodedImage> DecodeAsync(string path, CancellationToken ct, int frameIndex = 0)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
        if (frameIndex < 0 || frameIndex >= decoder.FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex), $"Page {frameIndex + 1} is outside 1..{decoder.FrameCount}.");
        }

        IBitmapFrame frame = frameIndex == 0 ? decoder : await decoder.GetFrameAsync((uint)frameIndex).AsTask(ct).ConfigureAwait(false);

        // The pixels are held as a byte array of 4 bytes each; refuse before asking Windows for more than that can be.
        var pixels = (long)frame.OrientedPixelWidth * frame.OrientedPixelHeight;
        if (pixels > ImageTooLargeException.MaximumPixels)
        {
            throw new ImageTooLargeException(pixels);
        }

        try
        {
            // Straight alpha keeps the color of semi-transparent pixels intact for FlattenOntoWhite.
            // EXIF rotation is applied, so photos keep the orientation they are shown with.
            var pixelData = await frame.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb).AsTask(ct).ConfigureAwait(false);

            return new DecodedImage(
                pixelData.DetachPixelData(),
                frame.OrientedPixelWidth,
                frame.OrientedPixelHeight,
                frame.DpiX,
                frame.DpiY,
                decoder.FrameCount);
        }
        catch (Exception ex) when (IsOutOfMemory(ex))
        {
            throw new ImageTooLargeException(pixels, ex);
        }
    }

    /// <summary>E_OUTOFMEMORY (0x8007000E) comes back from Windows as a COMException, not as OutOfMemoryException.</summary>
    internal static bool IsOutOfMemory(Exception ex) =>
        ex is OutOfMemoryException || (ex is COMException { HResult: unchecked((int)0x8007000E) });

    /// <summary>Composites straight-alpha BGRA pixels onto white and makes them opaque.</summary>
    internal static void FlattenOntoWhite(byte[] bgra) => Bgra.FlattenOntoWhite(bgra);

    /// <summary>A decoded picture as straight-alpha BGRA, in the orientation it is shown with.</summary>
    internal sealed record DecodedImage(byte[] Pixels, uint Width, uint Height, double DpiX, double DpiY, uint FrameCount = 1);
}

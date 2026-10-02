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
/// decoders are installed. Multi-frame sources (animated GIF, multi-page TIFF) use the first frame, and the result
/// says so. A source that is cut off (see <see cref="ImageStructure"/>) is converted as far as Windows can read it,
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

        var image = await DecodeAsync(request.SourcePath, ct).ConfigureAwait(false);
        if (image.FrameCount > 1)
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

    internal static async Task<DecodedImage> DecodeAsync(string path, CancellationToken ct)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);

        // The pixels are held as a byte array of 4 bytes each; refuse before asking Windows for more than that can be.
        var pixels = (long)decoder.OrientedPixelWidth * decoder.OrientedPixelHeight;
        if (pixels > ImageTooLargeException.MaximumPixels)
        {
            throw new ImageTooLargeException(pixels);
        }

        try
        {
            // Straight alpha keeps the color of semi-transparent pixels intact for FlattenOntoWhite.
            // EXIF rotation is applied, so photos keep the orientation they are shown with.
            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb).AsTask(ct).ConfigureAwait(false);

            return new DecodedImage(
                pixelData.DetachPixelData(),
                decoder.OrientedPixelWidth,
                decoder.OrientedPixelHeight,
                decoder.DpiX,
                decoder.DpiY,
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
    internal static void FlattenOntoWhite(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            int alpha = bgra[i + 3];
            if (alpha == 255)
            {
                continue;
            }

            for (var channel = i; channel < i + 3; channel++)
            {
                bgra[channel] = (byte)(((bgra[channel] * alpha) + (255 * (255 - alpha)) + 127) / 255);
            }

            bgra[i + 3] = 255;
        }
    }

    /// <summary>A decoded picture as straight-alpha BGRA, in the orientation it is shown with.</summary>
    internal sealed record DecodedImage(byte[] Pixels, uint Width, uint Height, double DpiX, double DpiY, uint FrameCount = 1);
}

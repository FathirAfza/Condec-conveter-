// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Formats;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Core.Imaging;

/// <summary>
/// JPG, PNG, BMP, GIF and TIFF to each other, plus HEIC and WebP as sources when their Windows
/// decoders are installed. Multi-frame sources (animated GIF, multi-page TIFF) use the first frame.
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
        var image = await DecodeAsync(request.SourcePath, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));

        if (!target.KeepsTransparency)
        {
            FlattenOntoWhite(image.Pixels);
        }

        ct.ThrowIfCancellationRequested();
        progress.Report(new ConversionProgress(ConversionStage.Encode, 0));

        // BitmapEncoder needs a seekable stream, so it writes to memory first. The finished image is then
        // copied to the pipeline's forward-only output in one pass, where it is hashed.
        using var staging = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(target.EncoderId, staging).AsTask(ct).ConfigureAwait(false);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            target.KeepsTransparency ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore,
            image.Width,
            image.Height,
            image.DpiX,
            image.DpiY,
            image.Pixels);
        await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 0.6));

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        await encoded.CopyToAsync(request.Output, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }

    private static async Task<DecodedImage> DecodeAsync(string path, CancellationToken ct)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);

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
            decoder.DpiY);
    }

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

    private sealed record DecodedImage(byte[] Pixels, uint Width, uint Height, double DpiX, double DpiY);
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Core.Imaging;

/// <summary>The image formats Condec reads and writes through the Windows Imaging Component.</summary>
internal static class ImageFormats
{
    /// <param name="KeepsTransparency">
    /// False when the format has no real alpha channel (GIF only knows fully transparent or opaque).
    /// Transparent pixels are then placed on white, instead of turning black.
    /// </param>
    internal sealed record Target(string Extension, Guid EncoderId, Guid DecoderId, bool KeepsTransparency);

    private static readonly Target Heic = new(".heic", BitmapEncoder.HeifEncoderId, BitmapDecoder.HeifDecoderId, KeepsTransparency: false);

    private static readonly Target[] AlwaysAvailable =
    [
        new(".jpg", BitmapEncoder.JpegEncoderId, BitmapDecoder.JpegDecoderId, KeepsTransparency: false),
        new(".png", BitmapEncoder.PngEncoderId, BitmapDecoder.PngDecoderId, KeepsTransparency: true),
        new(".bmp", BitmapEncoder.BmpEncoderId, BitmapDecoder.BmpDecoderId, KeepsTransparency: false),
        new(".gif", BitmapEncoder.GifEncoderId, BitmapDecoder.GifDecoderId, KeepsTransparency: false),
        new(".tif", BitmapEncoder.TiffEncoderId, BitmapDecoder.TiffDecoderId, KeepsTransparency: true),
    ];

    private static readonly Lazy<bool> HeicWorks = new(() => Task.Run(ProbeHeicAsync).GetAwaiter().GetResult());

    /// <summary>The targets this machine can write and read back. HEIC is included only when it round-trips.</summary>
    public static IReadOnlyList<Target> Targets => HeicWorks.Value ? [.. AlwaysAvailable, Heic] : AlwaysAvailable;

    /// <summary>
    /// Windows lists a HEIF encoder even when the HEVC codec it needs (a Store extension) is missing, and
    /// encoding then fails. So HEIC is offered only after a small image has been written and decoded again.
    /// </summary>
    private static async Task<bool> ProbeHeicAsync()
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(Heic.EncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, 16, 16, 96, 96, new byte[16 * 16 * 4]);
            await encoder.FlushAsync();

            var decoder = await BitmapDecoder.CreateAsync(stream);
            _ = await decoder.GetPixelDataAsync();
            return decoder.DecoderInformation.CodecId == Heic.DecoderId;
        }
        catch (Exception)
        {
            // Any failure means the codec isn't usable here; the format simply isn't offered.
            return false;
        }
    }

    /// <summary>
    /// Source extension to decoder. The HEIF and WebP decoders are optional Windows extensions,
    /// so callers must check they are installed before offering those sources.
    /// </summary>
    private static readonly Dictionary<string, Guid> DecoderBySource = new(StringComparer.Ordinal)
    {
        [".jpg"] = BitmapDecoder.JpegDecoderId,
        [".jpeg"] = BitmapDecoder.JpegDecoderId,
        [".png"] = BitmapDecoder.PngDecoderId,
        [".bmp"] = BitmapDecoder.BmpDecoderId,
        [".gif"] = BitmapDecoder.GifDecoderId,
        [".tif"] = BitmapDecoder.TiffDecoderId,
        [".tiff"] = BitmapDecoder.TiffDecoderId,
        [".heic"] = BitmapDecoder.HeifDecoderId,
        [".heif"] = BitmapDecoder.HeifDecoderId,
        [".webp"] = BitmapDecoder.WebpDecoderId,
    };

    public static Target? FindTarget(string extension) =>
        Targets.FirstOrDefault(target => target.Extension == extension);

    public static Guid? FindDecoder(string sourceExtension) =>
        DecoderBySource.TryGetValue(sourceExtension, out var decoderId) ? decoderId : null;
}

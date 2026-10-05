// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Compression;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Core.Imaging;

/// <summary>
/// Compress Image (DESIGN §6.5): any picture Windows can read, camera RAW included when its codec is installed, becomes a
/// smaller JPG, PNG or HEIC. Its own registry and pipeline, so a JPG may become a JPG.
/// </summary>
public sealed class ImageCompressor : IConverter
{
    private static readonly Lazy<HashSet<string>> InstalledSources = new(() =>
        [.. BitmapDecoder.GetDecoderInformationEnumerator()
            .SelectMany(codec => codec.FileExtensions)
            .Select(FileExtension.Normalize)
            .Where(extension => extension.Length > 1)]);

    /// <summary>Every extension an installed Windows decoder claims, lower case, such as ".heic", ".cr2" or ".dng".</summary>
    public static IReadOnlySet<string> SourceExtensions => InstalledSources.Value;

    /// <summary>JPG, PNG, and HEIC when this PC can write it (DESIGN §6.1.1). Always the same order.</summary>
    public static IReadOnlyList<string> TargetExtensions =>
        [.. new[] { ".jpg", ".png", ".heic" }.Where(extension => ImageFormats.FindTarget(extension) is not null)];

    /// <summary>
    /// Whether the target has a quality setting: JPG and HEIC, whose Windows encoders take ImageQuality (Win32 WIC docs,
    /// "Encoding overview"; the WinRT list names only JPEG). PNG is lossless.
    /// </summary>
    public static bool HasQuality(string targetExtension) => FileExtension.Normalize(targetExtension) is ".jpg" or ".heic";

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        SourceExtensions.Contains(FileExtension.Normalize(sourceExtension)) ? TargetExtensions : [];

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var options = request.Options as CompressOptions
            ?? throw new ArgumentException("Compress Image needs CompressOptions.", nameof(request));

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0));
        var source = await CompressSource.LoadAsync(request.SourcePath, ct).ConfigureAwait(false);
        if (source.IsIncomplete)
        {
            request.Notes.Add(NoteSeverity.Warning, Loc.Get("Note.SourceIncomplete"));
        }

        if (source.FrameCount > 1)
        {
            request.Notes.Add(NoteSeverity.Informational, Loc.Format("Note.FirstFrameOnly", source.FrameCount));
        }

        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));
        progress.Report(new ConversionProgress(ConversionStage.Encode, 0));

        var result = options.TargetBytes is { } limit
            ? await source.FitAsync(request.TargetExtension, limit, ct).ConfigureAwait(false) ?? throw new CompressTargetTooSmallException(limit)
            : await source.EncodeAsync(request.TargetExtension, options.Quality, options.ResolutionPercent, ct).ConfigureAwait(false);

        if (result.Data.LongLength >= source.FileLength)
        {
            request.Notes.Add(NoteSeverity.Informational, Loc.Format(
                "Note.CompressNotSmaller",
                DisplayFormat.FormatFileSize(source.FileLength),
                DisplayFormat.FormatFileSize(result.Data.LongLength)));
        }

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0.6));
        await request.Output.WriteAsync(result.Data, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }
}

/// <summary>A compressed picture, still in memory: the file's bytes and the setting that made them.</summary>
public sealed record CompressedImage(byte[] Data, int Width, int Height, CompressSetting Setting);

/// <summary>
/// One decoded picture, kept in memory so it can be encoded again and again while the user moves a slider, without reading
/// the file each time. The first frame, in the orientation it is shown with, in sRGB (DESIGN §6.1.1).
/// </summary>
public sealed class CompressSource
{
    private readonly byte[] _pixels;
    private readonly double _dpiX;
    private readonly double _dpiY;
    private readonly Lazy<byte[]> _flattened;

    private CompressSource(ImageConverter.DecodedImage image, long fileLength, bool isIncomplete)
    {
        _pixels = image.Pixels;
        _dpiX = image.DpiX;
        _dpiY = image.DpiY;
        Width = (int)image.Width;
        Height = (int)image.Height;
        FrameCount = image.FrameCount;
        FileLength = fileLength;
        IsIncomplete = isIncomplete;
        HasTransparency = HasAlpha(_pixels);

        // An opaque photo is the same on white, so only a transparent picture needs a second copy.
        _flattened = new(() =>
        {
            if (!HasTransparency)
            {
                return _pixels;
            }

            var copy = (byte[])_pixels.Clone();
            Bgra.FlattenOntoWhite(copy);
            return copy;
        });
    }

    public int Width { get; }

    public int Height { get; }

    public uint FrameCount { get; }

    /// <summary>The source file's size in bytes.</summary>
    public long FileLength { get; }

    /// <summary>The file ends before its format says it should (DESIGN §6.1.1).</summary>
    public bool IsIncomplete { get; }

    /// <summary>Some pixel is not fully opaque.</summary>
    public bool HasTransparency { get; }

    public static async Task<CompressSource> LoadAsync(string path, CancellationToken ct)
    {
        var length = new FileInfo(path).Length;
        var incomplete = ImageStructure.IsComplete(path) == false;
        var image = await ImageConverter.DecodeAsync(path, ct).ConfigureAwait(false);
        return new CompressSource(image, length, incomplete);
    }

    /// <summary>Encodes the picture with a quality (JPG and HEIC) at a percentage of its resolution.</summary>
    public async Task<CompressedImage> EncodeAsync(string targetExtension, int quality, int resolutionPercent, CancellationToken ct)
    {
        var target = Target(targetExtension);
        quality = Math.Clamp(quality, CompressOptions.MinimumQuality, CompressOptions.MaximumQuality);
        resolutionPercent = Math.Clamp(resolutionPercent, 1, CompressOptions.MaximumPercent);
        var (width, height) = CompressOptions.ScaledSize(Width, Height, resolutionPercent);
        var data = await EncodeAsync(target, ImageCompressor.HasQuality(target.Extension) ? quality : null, width, height, ct).ConfigureAwait(false);
        return new CompressedImage(data, width, height, new CompressSetting(quality, resolutionPercent));
    }

    /// <summary>The best result no larger than <paramref name="targetBytes"/> (<see cref="CompressSearch"/>), or null when none fits.</summary>
    public async Task<CompressedImage?> FitAsync(string targetExtension, long targetBytes, CancellationToken ct)
    {
        var target = Target(targetExtension);
        var fitting = new Dictionary<CompressSetting, CompressedImage>();
        var found = await CompressSearch.FitAsync(
            ImageCompressor.HasQuality(target.Extension),
            targetBytes,
            async (setting, token) =>
            {
                var image = await EncodeAsync(target.Extension, setting.Quality, setting.ResolutionPercent, token).ConfigureAwait(false);
                if (image.Data.LongLength <= targetBytes)
                {
                    fitting[setting] = image;
                }

                return image.Data.LongLength;
            },
            ct).ConfigureAwait(false);
        return found is null ? null : fitting[found.Setting];
    }

    private static ImageFormats.Target Target(string extension) =>
        ImageFormats.FindTarget(FileExtension.Normalize(extension)) is { } target && ImageCompressor.TargetExtensions.Contains(target.Extension)
            ? target
            : throw new NotSupportedException($"'{extension}' is not a Compress Image target.");

    private async Task<byte[]> EncodeAsync(ImageFormats.Target target, int? quality, int width, int height, CancellationToken ct)
    {
        // Formats without alpha get the picture on white first (§6.1.1). The Windows scaler weighs straight alpha itself:
        // fully transparent pixels don't darken the edges (tested in ImageCompressorTests).
        var pixels = target.KeepsTransparency ? _pixels : _flattened.Value;
        var alpha = target.KeepsTransparency ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore;

        using var stream = new InMemoryRandomAccessStream();
        try
        {
            BitmapEncoder encoder;
            if (quality is { } q)
            {
                var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(q / 100f, PropertyType.Single) };
                encoder = await BitmapEncoder.CreateAsync(target.EncoderId, stream, options).AsTask(ct).ConfigureAwait(false);
            }
            else
            {
                encoder = await BitmapEncoder.CreateAsync(target.EncoderId, stream).AsTask(ct).ConfigureAwait(false);
            }

            encoder.SetPixelData(BitmapPixelFormat.Bgra8, alpha, (uint)Width, (uint)Height, _dpiX, _dpiY, pixels);
            if (width != Width || height != Height)
            {
                encoder.BitmapTransform.ScaledWidth = (uint)width;
                encoder.BitmapTransform.ScaledHeight = (uint)height;
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
            }

            await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ImageConverter.IsOutOfMemory(ex))
        {
            throw new ImageTooLargeException((long)Width * Height, ex);
        }

        var data = new byte[stream.Size];
        using var reader = stream.GetInputStreamAt(0).AsStreamForRead();
        await reader.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        return data;
    }

    private static bool HasAlpha(byte[] bgra)
    {
        for (var i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 255)
            {
                return true;
            }
        }

        return false;
    }
}

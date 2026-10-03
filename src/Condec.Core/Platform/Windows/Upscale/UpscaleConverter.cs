// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Devices;
using Condec.Core.Formats;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Core.Upscale;

/// <summary>The upscale model is not in the install folder, or is not the file that was released.</summary>
public sealed class UpscaleModelUnavailableException(UpscaleModelStatus status)
    : Exception($"The upscale model is {status}.")
{
    public UpscaleModelStatus Status { get; } = status;
}

/// <summary>
/// Enlarges a picture to the exact size in <see cref="UpscaleOptions"/>. The network always enlarges 4 times (tile by tile,
/// <see cref="TiledUpscaler"/>); the result is then resized to the asked size with Lanczos, so any scale from 1.5 to 16
/// works with the one model. Transparency (PNG) is resized on its own, because the network only sees color.
/// Reports Decode for loading the picture and the model, and Encode for the tiles
/// (up to <see cref="UpscaleSupport.TilesEnd"/>), then resizing and saving.
/// </summary>
public sealed class UpscaleConverter : IReencodingConverter
{
    private static readonly string[] OutputExtensions = [".png", ".jpg"];

    private readonly Lazy<HashSet<Guid>> _installedDecoders = new(() =>
        [.. BitmapDecoder.GetDecoderInformationEnumerator().Select(codec => codec.CodecId)]);

    private readonly DeviceProfile _device;
    private readonly Func<bool, IUpscaleModel>? _modelFactory;

    /// <param name="device">Tells which engine really renders (an NPU choice falls back to the GPU or CPU).</param>
    /// <param name="modelFactory">Opens the network; tests pass a stand-in. The argument says whether the GPU is wanted.</param>
    public UpscaleConverter(DeviceProfile device, Func<bool, IUpscaleModel>? modelFactory = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _modelFactory = modelFactory;
    }

    public IReadOnlyList<string> GetTargets(string sourceExtension)
    {
        var decoderId = ImageFormats.FindDecoder(FileExtension.Normalize(sourceExtension));
        return decoderId is null || !_installedDecoders.Value.Contains(decoderId.Value) ? [] : OutputExtensions;
    }

    public bool CanReencode(string extension) => GetTargets(extension).Count > 0;

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var options = request.Options as UpscaleOptions
            ?? throw new ArgumentException("An upscale needs UpscaleOptions.", nameof(request));
        if (!TiledUpscaler.TileSizes.Contains(options.TileSize))
        {
            throw new ArgumentException($"{options.TileSize} is not a tile size the upscaler uses.", nameof(request));
        }

        if (!(options.Duty > 0 && options.Duty <= 1))
        {
            throw new ArgumentException($"{options.Duty} is not a share of time working.", nameof(request));
        }

        var target = OutputExtensions.Contains(FileExtension.Normalize(request.TargetExtension))
            ? ImageFormats.FindTarget(FileExtension.Normalize(request.TargetExtension))
            : null;
        if (target is null)
        {
            throw new NotSupportedException($"'{request.TargetExtension}' is not an upscale target.");
        }

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0));
        if (ImageStructure.IsComplete(request.SourcePath) == false)
        {
            request.Notes.Add(NoteSeverity.Warning, Loc.Get("Note.SourceIncomplete"));
        }

        var image = await ImageConverter.DecodeAsync(request.SourcePath, ct).ConfigureAwait(false);
        if (image.FrameCount > 1)
        {
            request.Notes.Add(NoteSeverity.Informational, Loc.Format("Note.FirstFrameOnly", image.FrameCount));
        }

        var outputPixels = (long)options.OutputWidth * options.OutputHeight;
        var networkPixels = UpscaleSupport.NetworkPixels((int)image.Width, (int)image.Height);
        if (Math.Max(outputPixels, networkPixels) > UpscaleSupport.MaximumOutputPixels)
        {
            throw new ImageTooLargeException(Math.Max(outputPixels, networkPixels));
        }

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0.5));
        using var model = OpenModel(options, request.Notes, ct);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));

        var width = (int)image.Width;
        var height = (int)image.Height;
        var keepsAlpha = target.KeepsTransparency && HasTransparency(image.Pixels);
        var alpha = keepsAlpha ? ExtractAlpha(image.Pixels) : null;
        if (!target.KeepsTransparency)
        {
            // The network sees the picture as it will look on white, so a flattened JPG has no dark fringes.
            ImageConverter.FlattenOntoWhite(image.Pixels);
        }

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0));
        var tiles = new Progress<(int Done, int Total)>(t => progress.Report(new ConversionProgress(
            ConversionStage.Encode,
            UpscaleSupport.TilesEnd * t.Done / t.Total,
            UpscaleSupport.TileDetail(t.Done, t.Total))));
        byte[] pixels;
        try
        {
            pixels = await Task.Run(() => Render(model, image.Pixels, width, height, options, alpha, tiles, ct), ct).ConfigureAwait(false);
        }
        catch (OutOfMemoryException ex)
        {
            throw new ImageTooLargeException(outputPixels, ex);
        }

        // The network's own result (16 times the source) and the first pass of the resize are garbage now. Left to the collector
        // they would still be held while the picture is saved, and the memory limit counts them (DESIGN §7.5).
        GC.Collect(2, GCCollectionMode.Forced, blocking: true);

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0.9));
        using var staging = await ImageConverter.EncodeAsync(
            target, pixels, (uint)options.OutputWidth, (uint)options.OutputHeight, image.DpiX, image.DpiY, ct).ConfigureAwait(false);

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        await encoded.CopyToAsync(request.Output, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }

    private IUpscaleModel OpenModel(UpscaleOptions options, ConversionNotes notes, CancellationToken ct)
    {
        var engine = UpscaleSupport.EffectiveEngine(options.Engine, _device);
        if (options.Engine == RenderEngine.Npu)
        {
            notes.Add(NoteSeverity.Informational, Loc.Format("Note.UpscaleNpuFallback", DeviceText.ShortName(engine)));
        }

        ct.ThrowIfCancellationRequested();
        var gpu = engine == RenderEngine.Gpu;
        IUpscaleModel model;
        if (_modelFactory is { } factory)
        {
            model = factory(gpu);
        }
        else
        {
            var status = UpscaleModelLocator.GetStatus(options.Style);
            if (status != UpscaleModelStatus.Ready)
            {
                throw new UpscaleModelUnavailableException(status);
            }

            model = OnnxUpscaleModel.Open(UpscaleModelLocator.ModelPath(options.Style), gpu, options.TileSize);
        }

        if (gpu && model is OnnxUpscaleModel { Engine: RenderEngine.Cpu })
        {
            notes.Add(NoteSeverity.Informational, Loc.Get("Note.UpscaleGpuFallback"));
        }

        return model;
    }

    /// <summary>Runs the network, then resizes the result (and the transparency) to the asked size. Returns BGRA of that size.</summary>
    private static byte[] Render(
        IUpscaleModel model,
        byte[] source,
        int width,
        int height,
        UpscaleOptions options,
        byte[]? alpha,
        IProgress<(int Done, int Total)> tiles,
        CancellationToken ct)
    {
        byte[] enlarged;
        using (RenderPriority.Lower())
        using (var monitor = options.Adaptive ? WindowsLoadMonitor.Open() : null)
        {
            var pacer = options.Duty < 1 || monitor is not null ? new TilePacer(options.Duty, monitor: monitor) : null;
            enlarged = TiledUpscaler.Run(model, source, width, height, options.TileSize, tiles, ct, pacer);
        }

        var scale = model.Scale;
        var result = Resampler.Resize(enlarged, width * scale, height * scale, 4, options.OutputWidth, options.OutputHeight, ct);
        if (alpha is not null)
        {
            var resizedAlpha = Resampler.Resize(alpha, width, height, 1, options.OutputWidth, options.OutputHeight, ct);
            for (var i = 0; i < resizedAlpha.Length; i++)
            {
                result[(i * 4) + 3] = resizedAlpha[i];
            }
        }

        return result;
    }

    private static bool HasTransparency(byte[] bgra)
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

    private static byte[] ExtractAlpha(byte[] bgra)
    {
        var alpha = new byte[bgra.Length / 4];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = bgra[(i * 4) + 3];
        }

        return alpha;
    }
}

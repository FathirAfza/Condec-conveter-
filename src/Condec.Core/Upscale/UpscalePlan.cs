// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;

namespace Condec.Core.Upscale;

/// <summary>A named result size of the "Resolusi hasil" list (DESIGN §6.2). It is the size of the longer side that is named.</summary>
public sealed record ResolutionPreset(string NameKey, int LongSide);

/// <summary>What the Upscale page lets a person choose for one picture: the largest scale, and sizes in between (DESIGN §6.2, §7.3).</summary>
public static class UpscalePlan
{
    /// <summary>
    /// The named resolutions. Their sizes are those of a 16:9 picture; a picture of another shape gets the longer side
    /// of the name and the other side in proportion, so no preset changes the shape.
    /// </summary>
    public static IReadOnlyList<ResolutionPreset> Presets { get; } =
    [
        new("Upscale.Preset.FullHd", 1920),
        new("Upscale.Preset.TwoK", 2560),
        new("Upscale.Preset.FourK", 3840),
        new("Upscale.Preset.FiveK", 5120),
        new("Upscale.Preset.EightK", 7680),
    ];

    /// <summary>
    /// The limit of DESIGN §7.3, also held to what one picture in memory can be (<see cref="UpscaleSupport.MaximumOutputPixels"/>),
    /// in steps of 0.5 like the rest of it. A source too large for the network (<see cref="UpscaleSupport.MaximumSourcePixels"/>) has no scale.
    /// </summary>
    public static ScaleLimit EffectiveLimit(DeviceProfile device, int width, int height, double settingsLimit)
    {
        var limit = CapabilityPolicy.EffectiveMaxScale(device, width, height, settingsLimit);
        var memoryMax = (long)width * height > UpscaleSupport.MaximumSourcePixels
            ? 0
            : Math.Sqrt((double)UpscaleSupport.MaximumOutputPixels / ((double)width * height));
        var effective = Math.Min(limit.Effective, Math.Floor(memoryMax * 2) / 2);
        return effective == limit.Effective ? limit : limit with { Effective = effective };
    }

    /// <summary>The scale that makes the longer side <paramref name="longSide"/> pixels.</summary>
    public static double ScaleForLongSide(int width, int height, int longSide) => (double)longSide / Math.Max(width, height);

    /// <summary>The result size for a named resolution: the longer side exactly, the other in proportion.</summary>
    public static (int Width, int Height) SizeForLongSide(int width, int height, int longSide)
    {
        var scale = ScaleForLongSide(width, height, longSide);
        return width >= height
            ? (longSide, Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero)))
            : (Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero)), longSide);
    }

    /// <summary>Whether a named resolution can be chosen: it needs a scale between 1.5 and the limit.</summary>
    public static bool IsPresetAllowed(ResolutionPreset preset, int width, int height, ScaleLimit limit) =>
        CapabilityPolicy.IsScaleAllowed(ScaleForLongSide(width, height, preset.LongSide), limit);

    /// <summary>The named resolution with exactly this size, or null for a custom one.</summary>
    public static ResolutionPreset? MatchPreset(int sourceWidth, int sourceHeight, int outputWidth, int outputHeight) =>
        Presets.FirstOrDefault(p => SizeForLongSide(sourceWidth, sourceHeight, p.LongSide) == (outputWidth, outputHeight));

    /// <summary>The size of the result at a scale (<see cref="UpscaleEstimator.OutputSize"/>), as the options the converter takes.</summary>
    public static UpscaleOptions OptionsFor(int width, int height, double scale, RenderEngine engine)
    {
        var (w, h) = UpscaleEstimator.OutputSize(width, height, scale);
        return new UpscaleOptions(w, h, engine);
    }
}

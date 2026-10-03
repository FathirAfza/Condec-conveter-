// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Formats;
using Condec.Core.Localization;

namespace Condec.Core.Devices;

public enum LimitKind
{
    /// <summary>The graphics adapter's memory sets the limit.</summary>
    Device,

    /// <summary>The user's limit in Settings is lower than what the device can do.</summary>
    Settings,

    /// <summary>The installed RAM, for the size of this picture, sets the limit.</summary>
    Ram,

    /// <summary>Less than 8 GB of RAM: nothing can be upscaled.</summary>
    RamTooLow,
}

/// <summary>One reason a scale is locked (DESIGN §7.4). <see cref="Value"/> is the scale limit for Device and Settings.</summary>
public sealed record LimitReason(LimitKind Kind, double Value, int InstalledRamGb, string? RamLabelKey)
{
    public string Describe(CultureInfo? culture = null) => Kind switch
    {
        LimitKind.Device => Loc.Format(culture, "Limit.Device", ScaleText.Format(Value, culture)),
        LimitKind.Settings => Loc.Format(culture, "Limit.Settings", ScaleText.Format(Value, culture)),
        LimitKind.Ram => Loc.Format(culture, "Limit.Ram", InstalledRamGb, Loc.Get(RamLabelKey!, culture)),
        _ => Loc.Format(culture, "Limit.RamTooLow", InstalledRamGb, CapabilityPolicy.MinimumRamGb),
    };
}

/// <summary>The scale limits for one picture on one device (DESIGN §7.3).</summary>
public sealed record ScaleLimit(double GpuLimit, double SettingsLimit, double RamMax, double Raw, double Effective, IReadOnlyList<LimitReason> Reasons)
{
    /// <summary>False when even 1.5× can't be done; the screens then show the "device doesn't qualify" message.</summary>
    public bool IsAvailable => Effective >= CapabilityPolicy.MinimumScale;

    /// <summary>The reasons joined with " · ", as shown under the scale list.</summary>
    public string DescribeReasons(CultureInfo? culture = null) => string.Join(" · ", Reasons.Select(r => r.Describe(culture)));
}

/// <summary>One line of the RAM table in DESIGN §7.2.</summary>
public sealed record RamTier(int Gb, long? MaxPixels, string LabelKey);

/// <summary>
/// The only place that decides how far a picture can be upscaled on this device (DESIGN §7). The screens show what
/// it returns and never recompute a limit.
/// </summary>
public static class CapabilityPolicy
{
    /// <summary>The smallest scale worth offering; the slider starts here (150%).</summary>
    public const double MinimumScale = 1.5;

    /// <summary>The largest scale Condec offers.</summary>
    public const double MaximumScale = 16;

    /// <summary>Below this much installed RAM nothing is upscaled.</summary>
    public const int MinimumRamGb = 8;

    /// <summary>The scales in the ComboBox, in order.</summary>
    public static IReadOnlyList<int> Presets { get; } = [2, 4, 8, 16];

    /// <summary>
    /// Installed RAM needed for a result of a given size. 8 GB up to 4K was decided by the owner (2026-10-03: a 4000 × 3000
    /// result was measured at about 0.8 GB, and the memory limit guards the rest); 32 and 64 GB are an assumption (DESIGN §13 #1).
    /// </summary>
    public static IReadOnlyList<RamTier> RamTiers { get; } =
    [
        new(8, 3840L * 2160, "Ram.Tier.HdTo4K"),
        new(32, 7680L * 4320, "Ram.Tier.4KTo8K"),
        new(64, null, "Ram.Tier.Above8K"),
    ];

    /// <summary>
    /// The limit the graphics adapter sets (DESIGN §7.1, an assumption until measured): an integrated GPU or under
    /// 6 GB of VRAM allows 2×, 6–11 GB 4×, 12–15 GB 8×, 16 GB and more 16×. No GPU counts as an integrated one.
    /// </summary>
    public static int GpuScaleLimit(DeviceProfile device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.Gpu is null || device.Gpu.IsIntegrated)
        {
            return 2;
        }

        return device.GpuMemoryGb switch
        {
            < 6 => 2,
            < 12 => 4,
            < 16 => 8,
            _ => 16,
        };
    }

    /// <summary>
    /// The largest scale for a picture (DESIGN §7.3). <paramref name="settingsLimit"/> is the limit chosen in Settings.
    /// </summary>
    public static ScaleLimit EffectiveMaxScale(int sourceWidth, int sourceHeight, int installedRamGb, double gpuLimit, double settingsLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sourceHeight, 0);

        var tier = RamTiers.LastOrDefault(t => t.Gb <= installedRamGb);
        var ramMax = tier is null ? 0
            : tier.MaxPixels is null ? MaximumScale
            : Math.Sqrt(tier.MaxPixels.Value / ((double)sourceWidth * sourceHeight));

        var raw = Math.Min(Math.Min(gpuLimit, settingsLimit), Math.Min(ramMax, MaximumScale));
        var effective = Math.Floor(raw * 2) / 2;

        return new ScaleLimit(gpuLimit, settingsLimit, ramMax, raw, effective, Reasons(raw, gpuLimit, settingsLimit, ramMax, installedRamGb, tier));
    }

    /// <summary>Same as the explicit overload, with the GPU limit read from the device.</summary>
    public static ScaleLimit EffectiveMaxScale(DeviceProfile device, int sourceWidth, int sourceHeight, double settingsLimit) =>
        EffectiveMaxScale(sourceWidth, sourceHeight, device.InstalledRamGb, GpuScaleLimit(device), settingsLimit);

    /// <summary>The installed RAM a result of this many pixels needs (DESIGN §7.2).</summary>
    public static RamTier RequiredRam(long outputPixels) =>
        RamTiers.FirstOrDefault(t => t.MaxPixels is null || outputPixels <= t.MaxPixels) ?? RamTiers[^1];

    /// <summary>The scale a screen starts on: 4×, or the limit when it is lower (DESIGN §7.3).</summary>
    public static double DefaultScale(ScaleLimit limit) => Math.Min(4, limit.Effective);

    /// <summary>The upscale Architecture suggests: 2×, and only when the device can do at least that.</summary>
    public static int? SuggestedArchitectureScale(ScaleLimit limit) => limit.Effective >= 2 ? 2 : null;

    /// <summary>Whether a preset scale can be picked. Locked presets stay in the list, disabled.</summary>
    public static bool IsScaleAllowed(double scale, ScaleLimit limit) => scale >= MinimumScale && scale <= limit.Effective;

    /// <summary>The presets that are locked for this picture, in order.</summary>
    public static IEnumerable<int> LockedPresets(ScaleLimit limit) => Presets.Where(p => p > limit.Effective);

    private static List<LimitReason> Reasons(double raw, double gpuLimit, double settingsLimit, double ramMax, int ramGb, RamTier? tier)
    {
        var reasons = new List<LimitReason>();
        if (raw >= MaximumScale)
        {
            return reasons;
        }

        if (Same(gpuLimit, raw))
        {
            reasons.Add(new LimitReason(LimitKind.Device, gpuLimit, ramGb, null));
        }

        if (settingsLimit < gpuLimit && Same(settingsLimit, raw))
        {
            reasons.Add(new LimitReason(LimitKind.Settings, settingsLimit, ramGb, null));
        }

        if (Same(ramMax, raw))
        {
            reasons.Add(tier is null
                ? new LimitReason(LimitKind.RamTooLow, 0, ramGb, null)
                : new LimitReason(LimitKind.Ram, ramMax, ramGb, tier.LabelKey));
        }

        return reasons;
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-9;
}

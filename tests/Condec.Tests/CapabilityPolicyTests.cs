// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Devices;

namespace Condec.Tests;

/// <summary>The test vectors of DESIGN §9, plus the tables of §7.</summary>
public class CapabilityPolicyTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id");

    private const long Gib = 1024L * 1024 * 1024;

    private static GpuInfo Gpu(int gb, bool integrated = false) => new("Test GPU", gb * Gib, integrated);

    // Source, installed RAM (GB), GPU limit, Settings limit, expected ramMax, expected effective, expected available.
    [Theory]
    [InlineData(1280, 720, 32, 4, 4, 6.0, 4.0, true)]
    [InlineData(1280, 720, 64, 16, 16, 16.0, 16.0, true)]
    [InlineData(1280, 720, 8, 2, 2, 6.0, 2.0, true)]
    [InlineData(1280, 720, 16, 4, 4, 6.0, 4.0, true)]
    [InlineData(1280, 720, 32, 4, 2, 6.0, 2.0, true)]
    [InlineData(3840, 2160, 32, 4, 4, 2.0, 2.0, true)]
    [InlineData(3840, 2160, 16, 4, 4, 2.0, 2.0, true)]
    [InlineData(1600, 1200, 32, 4, 4, 4.157, 4.0, true)]
    [InlineData(1600, 1200, 8, 2, 2, 4.157, 2.0, true)]
    [InlineData(2000, 1500, 8, 2, 2, 3.326, 2.0, true)]
    [InlineData(3840, 2160, 8, 2, 2, 2.0, 2.0, true)]
    [InlineData(1536, 864, 8, 5, 5, 5.0, 5.0, true)]
    [InlineData(1920, 1080, 8, 5, 5, 4.0, 4.0, true)]
    [InlineData(1000, 600, 8, 5, 5, 7.436, 5.0, true)]
    [InlineData(3840, 2160, 8, 5, 5, 2.0, 2.0, true)]
    [InlineData(7680, 4320, 8, 5, 5, 1.0, 1.0, false)]
    [InlineData(1600, 1200, 4, 2, 2, 0.0, 0.0, false)]
    public void EffectiveMaxScale_MatchesTheDesignVectors(int width, int height, int ramGb, int gpuLimit, int settingsLimit, double ramMax, double effective, bool available)
    {
        var limit = CapabilityPolicy.EffectiveMaxScale(width, height, ramGb, gpuLimit, settingsLimit);

        Assert.Equal(ramMax, limit.RamMax, 3);
        Assert.Equal(effective, limit.Effective);
        Assert.Equal(available, limit.IsAvailable);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 5)]
    [InlineData(8, 5)]
    [InlineData(11, 5)]
    [InlineData(12, 8)]
    [InlineData(15, 8)]
    [InlineData(16, 16)]
    [InlineData(24, 16)]
    public void GpuScaleLimit_FollowsTheVideoMemory(int vramGb, int expected) =>
        Assert.Equal(expected, CapabilityPolicy.GpuScaleLimit(new DeviceProfile("cpu", 32 * Gib, Gpu(vramGb), null)));

    [Fact]
    public void GpuScaleLimit_IsFiveForAnIntegratedGpuOrNone()
    {
        Assert.Equal(5, CapabilityPolicy.GpuScaleLimit(new DeviceProfile("cpu", 32 * Gib, Gpu(16, integrated: true), null)));
        Assert.Equal(5, CapabilityPolicy.GpuScaleLimit(new DeviceProfile("cpu", 32 * Gib, null, null)));
    }

    [Fact]
    public void GpuScaleLimit_RoundsMemoryThatWindowsReportsJustUnderAWholeNumber()
    {
        // A 12 GB card reports 12282 MiB, which is 11.99 GB.
        var twelveGb = new GpuInfo("RTX", 12282L * 1024 * 1024, false);
        Assert.Equal(8, CapabilityPolicy.GpuScaleLimit(new DeviceProfile("cpu", 32 * Gib, twelveGb, null)));
    }

    [Fact]
    public void InstalledRam_IsRoundedToWholeGigabytes()
    {
        // Windows reports an 8 GB laptop as about 7.7 GB.
        Assert.Equal(8, new DeviceProfile("cpu", (long)(7.7 * Gib), null, null).InstalledRamGb);
        Assert.Equal(16, new DeviceProfile("cpu", (long)(15.6 * Gib), null, null).InstalledRamGb);
    }

    [Theory]
    [InlineData(2560 * 1440, 8, "Ram.Tier.HdTo8K")]
    [InlineData(3840 * 2160, 8, "Ram.Tier.HdTo8K")]
    [InlineData(3840 * 2160 + 1, 8, "Ram.Tier.HdTo8K")]
    [InlineData(5120 * 2880, 8, "Ram.Tier.HdTo8K")]
    [InlineData(7680 * 4320, 8, "Ram.Tier.HdTo8K")]
    [InlineData(7680 * 4320 + 1, 64, "Ram.Tier.Above8K")]
    [InlineData(100, 8, "Ram.Tier.HdTo8K")]
    public void RequiredRam_FollowsTheResultSize(long pixels, int gb, string label)
    {
        var tier = CapabilityPolicy.RequiredRam(pixels);
        Assert.Equal(gb, tier.Gb);
        Assert.Equal(label, tier.LabelKey);
    }

    [Fact]
    public void RequiredRam_MatchesTheWorkedExamplesOfTheDesign()
    {
        // 2x of 1600 x 1200 (3200 x 2400) and 5x of 1536 x 864 (exactly 8K) fit the 8 GB tier; 5x of 1600 x 1200 (8000 x 6000) needs 64 GB.
        Assert.Equal(8, CapabilityPolicy.RequiredRam(Core.Upscale.UpscaleEstimator.OutputPixels(1600, 1200, 2)).Gb);
        Assert.Equal(8, CapabilityPolicy.RequiredRam(Core.Upscale.UpscaleEstimator.OutputPixels(1536, 864, 5)).Gb);
        Assert.Equal(64, CapabilityPolicy.RequiredRam(Core.Upscale.UpscaleEstimator.OutputPixels(1600, 1200, 5)).Gb);
    }

    [Fact]
    public void Reasons_NameTheLimitThatDecides()
    {
        // 1280 x 720, 32 GB, GPU 4x, Settings 4x: the device decides.
        var device = CapabilityPolicy.EffectiveMaxScale(1280, 720, 32, 4, 4);
        Assert.Equal("Device limit 4×", device.DescribeReasons(English));
        Assert.Equal("Batas perangkat 4×", device.DescribeReasons(Indonesian));

        // Settings at 2x under a 4x device.
        Assert.Equal("Limit in Settings 2×", CapabilityPolicy.EffectiveMaxScale(1280, 720, 32, 4, 2).DescribeReasons(English));

        // 16 GB RAM, 1920 x 1080: ramMax 4 is lower than the 8x device limit.
        Assert.Equal("16 GB of RAM: results up to HD–8K", CapabilityPolicy.EffectiveMaxScale(1920, 1080, 16, 8, 8).DescribeReasons(English));
        Assert.Equal("RAM 16 GB: hasil maksimal HD–8K", CapabilityPolicy.EffectiveMaxScale(1920, 1080, 16, 8, 8).DescribeReasons(Indonesian));
    }

    [Fact]
    public void Reasons_CanNameSeveralLimitsAtOnce()
    {
        // 8 GB RAM, 1920 x 1080: ramMax 4 equals the 4x device limit and the 4x Settings limit.
        var limit = CapabilityPolicy.EffectiveMaxScale(1920, 1080, 8, 4, 4);
        Assert.Equal([LimitKind.Device, LimitKind.Ram], limit.Reasons.Select(r => r.Kind));
        Assert.Equal("Device limit 4× · 8 GB of RAM: results up to HD–8K", limit.DescribeReasons(English));
    }

    [Fact]
    public void Reasons_SayWhenThereIsTooLittleRam()
    {
        var limit = CapabilityPolicy.EffectiveMaxScale(1600, 1200, 4, 2, 2);
        Assert.Equal([LimitKind.RamTooLow], limit.Reasons.Select(r => r.Kind));
        Assert.Equal("4 GB of RAM: at least 8 GB is needed", limit.DescribeReasons(English));
    }

    [Fact]
    public void Reasons_AreEmptyWhenNothingIsLocked()
    {
        Assert.Empty(CapabilityPolicy.EffectiveMaxScale(1280, 720, 64, 16, 16).Reasons);
    }

    [Fact]
    public void EveryRamTierLabel_ExistsInEveryLanguage()
    {
        foreach (var tier in CapabilityPolicy.RamTiers)
        {
            Assert.False(string.IsNullOrEmpty(Core.Localization.Loc.Get(tier.LabelKey, English)));
            Assert.False(string.IsNullOrEmpty(Core.Localization.Loc.Get(tier.LabelKey, Indonesian)));
        }
    }

    [Fact]
    public void Presets_AreLockedAboveTheEffectiveLimit()
    {
        var limit = CapabilityPolicy.EffectiveMaxScale(1920, 1080, 8, 3, 3); // effective 3.0
        Assert.Equal([4, 5, 8, 16], CapabilityPolicy.LockedPresets(limit));
        Assert.True(CapabilityPolicy.IsScaleAllowed(2, limit));
        Assert.False(CapabilityPolicy.IsScaleAllowed(4, limit));
        Assert.Equal([8, 16], CapabilityPolicy.LockedPresets(CapabilityPolicy.EffectiveMaxScale(1280, 720, 8, 5, 5)));
        Assert.True(CapabilityPolicy.IsScaleAllowed(5, CapabilityPolicy.EffectiveMaxScale(1536, 864, 8, 5, 5)));
        Assert.False(CapabilityPolicy.IsScaleAllowed(1.0, limit));
    }

    [Fact]
    public void DefaultScale_IsFourOrTheLimit()
    {
        Assert.Equal(4, CapabilityPolicy.DefaultScale(CapabilityPolicy.EffectiveMaxScale(1280, 720, 64, 16, 16)));
        Assert.Equal(2, CapabilityPolicy.DefaultScale(CapabilityPolicy.EffectiveMaxScale(1280, 720, 8, 2, 2)));
    }

    [Fact]
    public void SuggestedArchitectureScale_NeedsAtLeastTwo()
    {
        Assert.Equal(2, CapabilityPolicy.SuggestedArchitectureScale(CapabilityPolicy.EffectiveMaxScale(1280, 720, 8, 2, 2)));
        Assert.Null(CapabilityPolicy.SuggestedArchitectureScale(CapabilityPolicy.EffectiveMaxScale(7680, 4320, 8, 5, 5)));
    }

    [Fact]
    public void TheThreeExampleDevices_GetTheLimitsOfTheDesign()
    {
        Assert.Equal(5, CapabilityPolicy.GpuScaleLimit(new DeviceProfile("Intel Core Ultra 7 155H", 32 * Gib, new GpuInfo("RTX 4060 Laptop", 8 * Gib, false), "Intel AI Boost")));
        Assert.Equal(16, CapabilityPolicy.GpuScaleLimit(new DeviceProfile("AMD Ryzen 9 7950X", 64 * Gib, new GpuInfo("RTX 4090", 24 * Gib, false), null)));
        Assert.Equal(5, CapabilityPolicy.GpuScaleLimit(new DeviceProfile("Intel Core i5-1135G7", 8 * Gib, new GpuInfo("Intel Iris Xe", 0, true), null)));
    }
}

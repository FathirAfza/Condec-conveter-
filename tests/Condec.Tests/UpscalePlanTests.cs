// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;
using Condec.Core.Upscale;

namespace Condec.Tests;

public class UpscalePlanTests
{
    private const long Gib = 1024L * 1024 * 1024;

    private static DeviceProfile Device(int ramGb, int vramGb) =>
        new("cpu", ramGb * Gib, new GpuInfo("gpu", vramGb * Gib, IsIntegrated: false), null);

    [Theory]
    [InlineData(1280, 720, 1920, 1920, 1080)]
    [InlineData(1280, 720, 7680, 7680, 4320)]
    [InlineData(1600, 1200, 1920, 1920, 1440)]
    [InlineData(1200, 1600, 1920, 1440, 1920)]
    [InlineData(1000, 1000, 2560, 2560, 2560)]
    public void ANamedResolution_KeepsTheLongerSideAndTheShape(int width, int height, int longSide, int expectedWidth, int expectedHeight) =>
        Assert.Equal((expectedWidth, expectedHeight), UpscalePlan.SizeForLongSide(width, height, longSide));

    [Fact]
    public void ANamedResolution_ForA16By9Picture_IsTheNameItIsKnownBy()
    {
        Assert.Equal("Upscale.Preset.FourK", UpscalePlan.MatchPreset(1280, 720, 3840, 2160)?.NameKey);
        Assert.Equal("Upscale.Preset.FullHd", UpscalePlan.MatchPreset(1280, 720, 1920, 1080)?.NameKey);
        Assert.Null(UpscalePlan.MatchPreset(1280, 720, 3840, 2161));
        Assert.Null(UpscalePlan.MatchPreset(1280, 720, 3000, 1688));
    }

    [Fact]
    public void ThePresetsTheScaleCannotReach_AreNotAllowed()
    {
        // 1280 x 720 on a 32 GB PC with a 16 GB card: scales 1.5 to 16, so 1920 (1.5x) up to 7680 (6x) are all reachable.
        var big = UpscalePlan.EffectiveLimit(Device(32, 16), 1280, 720, settingsLimit: 16);
        Assert.All(UpscalePlan.Presets, preset => Assert.True(UpscalePlan.IsPresetAllowed(preset, 1280, 720, big)));

        // A picture that is already 1920 wide can't be "enlarged" to 1920 (scale 1), and 2560 is 1.33x: both are under 1.5x.
        var full = UpscalePlan.EffectiveLimit(Device(32, 16), 1920, 1080, settingsLimit: 16);
        Assert.False(UpscalePlan.IsPresetAllowed(UpscalePlan.Presets[0], 1920, 1080, full));
        Assert.False(UpscalePlan.IsPresetAllowed(UpscalePlan.Presets[1], 1920, 1080, full));
        Assert.True(UpscalePlan.IsPresetAllowed(UpscalePlan.Presets[2], 1920, 1080, full));

        // A small device: 8 GB RAM and a weak card, 5x at most: 1920 (1.5x) up to 5120 (4x) are reachable, 7680 (6x) is not.
        var small = UpscalePlan.EffectiveLimit(Device(8, 2), 1280, 720, settingsLimit: 16);
        Assert.Equal(5, small.Effective);
        Assert.True(UpscalePlan.IsPresetAllowed(UpscalePlan.Presets[0], 1280, 720, small));
        Assert.True(UpscalePlan.IsPresetAllowed(UpscalePlan.Presets[3], 1280, 720, small));
        Assert.False(UpscalePlan.IsPresetAllowed(UpscalePlan.Presets[4], 1280, 720, small));

        // 8K is exactly 5x of 1536 x 864, so the 8 GB device reaches it from that size and from nothing smaller.
        Assert.True(UpscalePlan.IsPresetAllowed(UpscalePlan.Presets[4], 1536, 864, UpscalePlan.EffectiveLimit(Device(8, 2), 1536, 864, settingsLimit: 5)));
        Assert.False(UpscalePlan.IsPresetAllowed(UpscalePlan.Presets[4], 1500, 844, UpscalePlan.EffectiveLimit(Device(8, 2), 1500, 844, settingsLimit: 5)));
    }

    [Fact]
    public void TheLimit_IsTheDesignsLimit_WhenNothingElseHoldsItBack()
    {
        var expected = CapabilityPolicy.EffectiveMaxScale(Device(16, 8), 1280, 720, 16);
        var actual = UpscalePlan.EffectiveLimit(Device(16, 8), 1280, 720, 16);

        Assert.Equal((expected.Raw, expected.Effective), (actual.Raw, actual.Effective));
        Assert.Equal(expected.DescribeReasons(), actual.DescribeReasons());
    }

    [Fact]
    public void AResultThatCannotBeHeldInMemory_LowersTheLimit_AndAHugeSourceHasNone()
    {
        // 64 GB and a big card allow 16x of anything, but 16x of 6000 x 4000 (24 MP) is 6.1 billion pixels: no array holds that.
        var limit = UpscalePlan.EffectiveLimit(Device(64, 24), 6000, 4000, 16);
        Assert.True(limit.Effective < 16);
        Assert.True((6000 * limit.Effective) * (4000 * limit.Effective) <= UpscaleSupport.MaximumOutputPixels);

        var huge = UpscalePlan.EffectiveLimit(Device(64, 24), 8000, 5000, 16);
        Assert.False(huge.IsAvailable);
    }

    [Fact]
    public void Options_CarryTheRoundedSizeAndTheEngine()
    {
        var options = UpscalePlan.OptionsFor(1280, 720, 2.5, RenderEngine.Gpu);

        Assert.Equal(new UpscaleOptions(3200, 1800, RenderEngine.Gpu), options);
    }
}

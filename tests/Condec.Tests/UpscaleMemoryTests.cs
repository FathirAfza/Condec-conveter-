// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;
using Condec.Core.Upscale;

namespace Condec.Tests;

/// <summary>The memory limit made real (DESIGN §7.5): what an upscale needs, and which tile size the limit allows.</summary>
public class UpscaleMemoryTests
{
    private const long Mib = 1024L * 1024;
    private const long Gib = 1024L * Mib;

    /// <summary>
    /// What the whole pipeline really held on top of the idle process (peak private bytes, 2026-10-03, AMD Ryzen 5 5600H with
    /// its integrated Radeon, DirectML and CPU, Windows 11), against what <see cref="UpscaleMemory.PeakBytes"/> says.
    /// The estimate must not be below what was measured, and must not be far above it either: a limit that refuses
    /// pictures that would have fitted is no better than one that doesn't hold.
    /// </summary>
    [Theory]
    [InlineData(640, 360, 1280, 720, RenderEngine.Gpu, 128, true, 400)]
    [InlineData(2000, 1500, 4000, 3000, RenderEngine.Gpu, 128, true, 782)]
    [InlineData(2000, 1500, 4000, 3000, RenderEngine.Gpu, 128, false, 777)]
    [InlineData(1000, 600, 2000, 1200, RenderEngine.Gpu, 96, true, 436)]
    [InlineData(1000, 600, 2000, 1200, RenderEngine.Gpu, 48, true, 305)]
    [InlineData(640, 360, 1280, 720, RenderEngine.Cpu, 128, true, 300)]
    [InlineData(1000, 600, 2000, 1200, RenderEngine.Cpu, 128, true, 322)]
    [InlineData(1000, 600, 2000, 1200, RenderEngine.Cpu, 64, true, 191)]
    [InlineData(1000, 600, 2000, 1200, RenderEngine.Cpu, 48, false, 187)]
    public void TheEstimate_IsAboveWhatWasMeasured_AndNotFarAbove(
        int width, int height, int outputWidth, int outputHeight, RenderEngine engine, int tileSize, bool png, long measuredMib)
    {
        var estimated = UpscaleMemory.PeakBytes(width, height, outputWidth, outputHeight, tileSize, engine, png) - UpscaleMemory.AppBytes;

        Assert.True(estimated >= measuredMib * Mib, $"estimated {estimated / Mib} MiB, measured {measuredMib} MiB");
        Assert.True(estimated <= measuredMib * Mib * 3 / 2, $"estimated {estimated / Mib} MiB, measured {measuredMib} MiB");
    }

    [Fact]
    public void ASmallerTile_NeedsLessMemory_AMoreAlphaOrBiggerPictureNeedsMore()
    {
        long Peak(int width, int tile, bool alpha) => UpscaleMemory.PeakBytes(width, width * 3 / 4, width * 2, width * 3 / 2, tile, RenderEngine.Gpu, alpha);

        Assert.True(Peak(1000, 128, true) > Peak(1000, 96, true));
        Assert.True(Peak(1000, 96, true) > Peak(1000, 64, true));
        Assert.True(Peak(1000, 64, true) > Peak(1000, 48, true));
        Assert.True(Peak(1000, 128, true) > Peak(1000, 128, false));
        Assert.True(Peak(2000, 128, true) > Peak(1000, 128, true));
    }

    [Fact]
    public void WithRoomToSpare_TheBiggestTileIsUsed()
    {
        var plan = UpscaleMemory.Plan(1920, 1080, 3840, 2160, RenderEngine.Gpu, true, 8 * Gib);

        Assert.Equal(128, plan.TileSize);
        Assert.True(plan.Fits);
    }

    [Fact]
    public void ATightLimit_PicksTheLargestTileThatFits()
    {
        long Peak(int tile) => UpscaleMemory.PeakBytes(1920, 1080, 3840, 2160, tile, RenderEngine.Gpu, true);

        // Room for a tile of 64 but not of 96.
        var plan = UpscaleMemory.Plan(1920, 1080, 3840, 2160, RenderEngine.Gpu, true, Peak(64));
        Assert.Equal(64, plan.TileSize);
        Assert.True(plan.Fits);
        Assert.Equal(Peak(64), plan.PeakBytes);

        // One byte less and only the smallest is left.
        plan = UpscaleMemory.Plan(1920, 1080, 3840, 2160, RenderEngine.Gpu, true, Peak(64) - 1);
        Assert.Equal(48, plan.TileSize);
    }

    [Fact]
    public void APictureThatFitsWithNoTile_IsReportedAsNotFitting_WithWhatItNeeds()
    {
        var plan = UpscaleMemory.Plan(8000, 6000, 16000, 12000, RenderEngine.Cpu, true, 4 * Gib);

        Assert.False(plan.Fits);
        Assert.Equal(48, plan.TileSize);
        Assert.True(plan.PeakBytes > 4 * Gib);
    }

    [Fact]
    public void EveryTileSize_IsOneTheUpscalerCanRun()
    {
        // The planner only ever returns a size TiledUpscaler accepts.
        foreach (var limit in new[] { 1L, 2 * Gib, 64 * Gib })
        {
            var plan = UpscaleMemory.Plan(3000, 2000, 6000, 4000, RenderEngine.Gpu, false, limit);
            Assert.Contains(plan.TileSize, TiledUpscaler.TileSizes);
        }
    }
}

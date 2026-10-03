// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Diagnostics;
using System.Globalization;
using Condec.Core.Devices;
using Condec.Core.Settings;
using Condec.Core.Upscale;

namespace Condec.Tests;

/// <summary>The performance mode and Memory saver (DESIGN §7.6): rest between tiles, the tile cap, the memory cap, the estimate.</summary>
public class RenderPaceTests
{
    private const long Gib = 1024L * 1024 * 1024;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DeviceProfile Laptop => new("cpu", 8 * Gib, new GpuInfo("Radeon", 512L * 1024 * 1024, true), null);

    private sealed class MemoryStore : ISettingsStore
    {
        public Dictionary<string, string> Values { get; } = [];

        public string? Get(string key) => Values.GetValueOrDefault(key);

        public void Set(string key, string? value)
        {
            if (value is null)
            {
                Values.Remove(key);
            }
            else
            {
                Values[key] = value;
            }
        }
    }

    /// <summary>Returns a blank result of the right size and counts the tiles.</summary>
    private sealed class BlankModel : IUpscaleModel
    {
        public int Tiles { get; private set; }

        public int Scale => 4;

        public float[] RunTile(float[] input, int tileSize)
        {
            Tiles++;
            return new float[3 * tileSize * Scale * tileSize * Scale];
        }

        public void Dispose()
        {
        }
    }

    [Theory]
    [InlineData(PerformanceMode.ExtraHigh, 0.8, 128, 80)]
    [InlineData(PerformanceMode.High, 0.6, 128, 60)]
    [InlineData(PerformanceMode.Medium, 0.4, 128, 40)]
    [InlineData(PerformanceMode.Low, 0.2, 64, 20)]
    public void EachMode_WorksItsShareOfTheTime(PerformanceMode mode, double duty, int maxTile, int percent)
    {
        var pace = RenderPace.For(mode, memorySaver: false);

        Assert.Equal(duty, pace.Duty);
        Assert.Equal(maxTile, pace.MaxTileSize);
        Assert.Equal(percent, pace.Percent);
        Assert.Null(pace.MemoryLimitGb);
    }

    [Theory]
    [InlineData(PerformanceMode.ExtraHigh)]
    [InlineData(PerformanceMode.Low)]
    public void MemorySaver_OverridesEveryMode(PerformanceMode mode)
    {
        var pace = RenderPace.For(mode, memorySaver: true);

        Assert.Equal(new RenderPace(0.1, 48, 2), pace);
        Assert.Equal(10, pace.Percent);
    }

    [Fact]
    public void TheModes_AreListedFromHardestToLightest()
    {
        Assert.Equal([PerformanceMode.ExtraHigh, PerformanceMode.High, PerformanceMode.Medium, PerformanceMode.Low], RenderPace.Modes);
        Assert.All(RenderPace.Modes, mode => Assert.Contains(RenderPace.For(mode, false).MaxTileSize, TiledUpscaler.TileSizes));
    }

    [Theory]
    [InlineData(1000, 0.8, 250)]
    [InlineData(1000, 0.6, 666.667)]
    [InlineData(1000, 0.4, 1500)]
    [InlineData(1000, 0.2, 4000)]
    [InlineData(1000, 0.1, 9000)]
    [InlineData(1000, 1.0, 0)]
    [InlineData(0, 0.2, 0)]
    public void ThePause_LeavesTheEngineWorkingItsShare(double workMs, double duty, double pauseMs)
    {
        var pause = TilePacer.PauseAfter(TimeSpan.FromMilliseconds(workMs), duty);

        Assert.Equal(pauseMs, pause.TotalMilliseconds, 2);
        if (workMs > 0)
        {
            Assert.Equal(duty, workMs / (workMs + pause.TotalMilliseconds), 6);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void ADutyOutsideZeroToOne_IsRefused(double duty) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TilePacer(duty));

    [Fact]
    public void Rest_WaitsThePauseAfterTheWork()
    {
        var waits = new List<TimeSpan>();
        var pacer = new TilePacer(0.25, (pause, _) => waits.Add(pause));

        pacer.Rest(TimeSpan.FromMilliseconds(100), Ct);
        pacer.Rest(TimeSpan.Zero, Ct);

        Assert.Equal([TimeSpan.FromMilliseconds(300)], waits);
    }

    [Fact]
    public void ACancelledRender_StopsResting_AtOnce()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var pacer = new TilePacer(0.1);
        var clock = Stopwatch.StartNew();

        // Ten seconds of work would mean a 90 second rest.
        Assert.Throws<OperationCanceledException>(() => pacer.Rest(TimeSpan.FromSeconds(10), cts.Token));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Rested {clock.Elapsed} after the cancel.");
    }

    [Fact]
    public void Run_RestsBetweenTiles_NotAfterTheLast()
    {
        var waits = new List<TimeSpan>();
        var model = new BlankModel();
        var pacer = new TilePacer(0.5, (pause, _) => waits.Add(pause));

        // 230 × 150 at tile 128 is 3 × 2 tiles.
        var result = TiledUpscaler.Run(model, new byte[230 * 150 * 4], 230, 150, TiledUpscaler.DefaultTileSize, null, Ct, pacer);

        Assert.Equal(6, model.Tiles);
        Assert.Equal(5, waits.Count);
        Assert.Equal(230L * 4 * 150 * 4 * 4, result.LongLength);
    }

    [Fact]
    public void Run_StopsWhenCancelledWhileResting()
    {
        using var cts = new CancellationTokenSource();
        var model = new BlankModel();
        var pacer = new TilePacer(0.5, (_, _) => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() =>
            TiledUpscaler.Run(model, new byte[230 * 150 * 4], 230, 150, TiledUpscaler.DefaultTileSize, null, cts.Token, pacer));
        Assert.Equal(1, model.Tiles);
    }

    [Fact]
    public void ThePlan_NeverUsesATileAboveTheModesCap()
    {
        Assert.Equal(128, UpscaleMemory.Plan(640, 360, 1280, 720, RenderEngine.Gpu, true, 8 * Gib).TileSize);
        Assert.Equal(64, UpscaleMemory.Plan(640, 360, 1280, 720, RenderEngine.Gpu, true, 8 * Gib, maxTileSize: 64).TileSize);
        Assert.Equal(48, UpscaleMemory.Plan(640, 360, 1280, 720, RenderEngine.Gpu, true, 8 * Gib, maxTileSize: 48).TileSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => UpscaleMemory.Plan(640, 360, 1280, 720, RenderEngine.Gpu, true, 8 * Gib, maxTileSize: 100));
    }

    [Fact]
    public void ThePlan_StillSaysWhenEvenTheSmallestTileDoesNotFit()
    {
        var plan = UpscaleMemory.Plan(1920, 1080, 3840, 2160, RenderEngine.Gpu, true, 2 * Gib, maxTileSize: 48);

        Assert.Equal(48, plan.TileSize);
        Assert.Equal(plan.PeakBytes <= 2 * Gib, plan.Fits);
    }

    [Fact]
    public void TheEstimate_GrowsAsTheShareOfWorkShrinks()
    {
        Assert.Equal(10, UpscaleEstimator.EstimatedSeconds(3_000_000, 0.3)!.Value, 6);
        Assert.Equal(12.5, UpscaleEstimator.EstimatedSeconds(3_000_000, 0.3, 0.8)!.Value, 6);
        Assert.Equal(100, UpscaleEstimator.EstimatedSeconds(3_000_000, 0.3, 0.1)!.Value, 6);
        Assert.Null(UpscaleEstimator.EstimatedSeconds(3_000_000, null, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => UpscaleEstimator.EstimatedSeconds(3_000_000, 0.3, 0));
    }

    [Fact]
    public void Settings_StartAtExtraHigh_WithMemorySaverOff()
    {
        var settings = new AppSettings(new MemoryStore(), Laptop);

        Assert.Equal(PerformanceMode.ExtraHigh, settings.PerformanceMode);
        Assert.False(settings.MemorySaver);
        Assert.Equal(new RenderPace(0.8, 128, null), settings.Pace);
        Assert.Equal(settings.MemoryLimitGb, settings.EffectiveMemoryLimitGb);
    }

    [Fact]
    public void Settings_RememberTheMode_AndMemorySaverCapsTheMemory()
    {
        var store = new MemoryStore();
        var settings = new AppSettings(store, Laptop) { PerformanceMode = PerformanceMode.Medium, MemoryLimitGb = 6 };

        settings.MemorySaver = true;
        var reread = new AppSettings(store, Laptop);

        Assert.Equal("Medium", store.Values[AppSettings.PerformanceModeKey]);
        Assert.Equal(PerformanceMode.Medium, reread.PerformanceMode);
        Assert.True(reread.MemorySaver);
        Assert.Equal(RenderPace.MemorySaver, reread.Pace);
        Assert.Equal(2, reread.EffectiveMemoryLimitGb);
        Assert.Equal(6, reread.MemoryLimitGb);

        reread.MemorySaver = false;
        Assert.Equal(new RenderPace(0.4, 128, null), reread.Pace);
        Assert.Equal(6, reread.EffectiveMemoryLimitGb);
    }

    [Theory]
    [InlineData("Turbo")]
    [InlineData("7")]
    [InlineData("")]
    public void AStoredModeThatIsNotAMode_ReadsAsExtraHigh(string stored)
    {
        var store = new MemoryStore();
        store.Set(AppSettings.PerformanceModeKey, stored);

        Assert.Equal(PerformanceMode.ExtraHigh, new AppSettings(store, Laptop).PerformanceMode);
    }

    [Fact]
    public void ChangingTheMode_TellsOpenScreens()
    {
        var settings = new AppSettings(new MemoryStore(), Laptop);
        var changes = 0;
        settings.Changed += (_, _) => changes++;

        settings.PerformanceMode = PerformanceMode.Low;
        settings.MemorySaver = true;

        Assert.Equal(2, changes);
    }

    [Fact]
    public void TheModeNames_ReadInBothLanguages()
    {
        var indonesian = CultureInfo.GetCultureInfo("id");
        var english = CultureInfo.GetCultureInfo("en");

        Assert.Equal("Sangat tinggi (80%)", PerformanceText.Mode(PerformanceMode.ExtraHigh, indonesian));
        Assert.Equal("Rendah (20%)", PerformanceText.Describe(PerformanceMode.Low, memorySaver: false, indonesian));
        Assert.Equal("Hemat memori (10%)", PerformanceText.Describe(PerformanceMode.High, memorySaver: true, indonesian));
        Assert.Equal("Extra high (80%)", PerformanceText.Mode(PerformanceMode.ExtraHigh, english));
        Assert.Equal("Medium (40%)", PerformanceText.Mode(PerformanceMode.Medium, english));
        Assert.Equal("Memory saver (10%)", PerformanceText.Describe(PerformanceMode.Low, memorySaver: true, english));
    }
}

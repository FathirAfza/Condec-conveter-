// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Devices;
using Condec.Core.Formats;
using Condec.Core.Settings;
using Condec.Core.Upscale;

namespace Condec.Tests;

public class UpscaleEstimateTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id");

    [Fact]
    public void FourTimesOf720p_IsTheWorkedExampleOfTheDesign()
    {
        var (width, height) = UpscaleEstimator.OutputSize(1280, 720, 4);
        var pixels = UpscaleEstimator.OutputPixels(1280, 720, 4);

        Assert.Equal((5120, 2880), (width, height));
        Assert.Equal("5120 × 2880", ScaleText.Dimensions(width, height));
        Assert.Equal("14.7 MP", ScaleText.Megapixels(pixels, English));
        Assert.Equal("14,7 MP", ScaleText.Megapixels(pixels, Indonesian));
        Assert.Equal("± 23,9 MB", ScaleText.Approximately(DisplayFormat.FormatFileSize(UpscaleEstimator.EstimatedFileBytes(pixels, UpscaleFormat.Png), Indonesian)));
        Assert.Equal("23.9 MB", DisplayFormat.FormatFileSize(UpscaleEstimator.EstimatedFileBytes(pixels, UpscaleFormat.Png), English));
    }

    [Fact]
    public void Jpg_IsEstimatedSmallerThanPng()
    {
        var pixels = UpscaleEstimator.OutputPixels(1280, 720, 4);
        Assert.True(UpscaleEstimator.EstimatedFileBytes(pixels, UpscaleFormat.Jpg) < UpscaleEstimator.EstimatedFileBytes(pixels, UpscaleFormat.Png));
    }

    [Fact]
    public void OutputSize_RoundsEachSide()
    {
        Assert.Equal((4480, 2520), UpscaleEstimator.OutputSize(1280, 720, 3.5));
        Assert.Equal((2, 2), UpscaleEstimator.OutputSize(1, 1, 1.5));
    }

    [Fact]
    public void EstimatedSeconds_DividesByTheMeasuredSpeed()
    {
        var pixels = UpscaleEstimator.OutputPixels(1280, 720, 4); // 14.7456 MP
        Assert.Equal(14.7456 / 2, UpscaleEstimator.EstimatedSeconds(pixels, 2.0)!.Value, 6);
    }

    [Fact]
    public void EstimatedSeconds_IsUnknownWithoutAMeasurement()
    {
        Assert.Null(UpscaleEstimator.EstimatedSeconds(1_000_000, null));
        Assert.Null(UpscaleEstimator.EstimatedSeconds(1_000_000, 0));
    }

    [Theory]
    [InlineData(0.4, "< 1 second", "< 1 detik")]
    [InlineData(7, "7 seconds", "7 detik")]
    [InlineData(59.4, "59 seconds", "59 detik")]
    public void Duration_IsWrittenInTheDesignedUnits(double seconds, string english, string indonesian)
    {
        Assert.Equal(english, ScaleText.Duration(seconds, English));
        Assert.Equal(indonesian, ScaleText.Duration(seconds, Indonesian));
    }

    [Fact]
    public void Duration_RoundsUpToTheNextUnitInsteadOfPrintingSixtySeconds()
    {
        Assert.Equal("1 min 0 sec", ScaleText.Duration(59.6, English));
        Assert.Equal("1 menit 0 detik", ScaleText.Duration(59.6, Indonesian));
    }

    [Fact]
    public void Duration_UsesMinutesAndHoursForLongRuns()
    {
        Assert.Equal("2 min 5 sec", ScaleText.Duration(125, English));
        Assert.Equal("2 menit 5 detik", ScaleText.Duration(125, Indonesian));
        Assert.Equal("1 h 20 min", ScaleText.Duration(4800, English));
        Assert.Equal("1 jam 20 menit", ScaleText.Duration(4800, Indonesian));
    }

    [Fact]
    public void ScaleText_WritesScalesAndPercents()
    {
        Assert.Equal("4×", ScaleText.Format(4, English));
        Assert.Equal("3.5×", ScaleText.Format(3.5, English));
        Assert.Equal("3,5×", ScaleText.Format(3.5, Indonesian));
        Assert.Equal("400%", ScaleText.Percent(4));
        Assert.Equal("350%", ScaleText.Percent(3.5));
    }
}

public class AppSettingsTests : IDisposable
{
    private const long Gib = 1024L * 1024 * 1024;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "condec-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static DeviceProfile Medium => new("cpu", 32 * Gib, new GpuInfo("RTX 4060 Laptop", 8 * Gib, false), "Intel AI Boost");

    private static DeviceProfile Basic => new("cpu", 8 * Gib, new GpuInfo("Iris Xe", 0, true), null);

    private static DeviceProfile NoGpu => new("cpu", 16 * Gib, null, null);

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

    [Fact]
    public void Defaults_FollowTheDeviceAsTheDesignSays()
    {
        var settings = new AppSettings(new MemoryStore(), Medium);

        Assert.Equal(RenderEngine.Gpu, settings.RenderMode);
        Assert.Equal(4, settings.ScaleLimit);
        Assert.Equal(24, settings.MemoryLimitGb); // 75% of 32
        Assert.Equal(ThemePreference.System, settings.Theme);
        Assert.True(settings.MicaEnabled);
        Assert.True(settings.LogEnabled);
        Assert.Equal(LogLevel.Info, settings.LogLevel);
    }

    [Fact]
    public void Defaults_UseTheCpuWithoutAGpu()
    {
        Assert.Equal(RenderEngine.Cpu, new AppSettings(new MemoryStore(), NoGpu).RenderMode);
        Assert.Equal(12, new AppSettings(new MemoryStore(), NoGpu).MemoryLimitGb);
    }

    [Fact]
    public void RenderMode_FallsBackWhenTheStoredEngineIsGone()
    {
        var store = new MemoryStore();
        store.Values[AppSettings.RenderModeKey] = "Npu";
        Assert.Equal(RenderEngine.Gpu, new AppSettings(store, Basic).RenderMode);
        Assert.Equal(RenderEngine.Cpu, new AppSettings(store, NoGpu).RenderMode);

        store.Values[AppSettings.RenderModeKey] = "Gpu";
        Assert.Equal(RenderEngine.Cpu, new AppSettings(store, NoGpu).RenderMode);
    }

    [Fact]
    public void RenderMode_KeepsAnAvailableChoice()
    {
        var settings = new AppSettings(new MemoryStore(), Medium) { RenderMode = RenderEngine.Npu };
        Assert.Equal(RenderEngine.Npu, settings.RenderMode);
    }

    [Fact]
    public void ScaleLimit_IsClampedToTheDevice()
    {
        var store = new MemoryStore();
        store.Values[AppSettings.ScaleLimitKey] = "16";
        Assert.Equal(2, new AppSettings(store, Basic).ScaleLimit);
        Assert.Equal(4, new AppSettings(store, Medium).ScaleLimit);

        store.Values[AppSettings.ScaleLimitKey] = "2";
        Assert.Equal(2, new AppSettings(store, Medium).ScaleLimit);
    }

    [Fact]
    public void MemoryLimit_StaysBetweenFourGbAndTheInstalledRam()
    {
        var store = new MemoryStore();
        store.Values[AppSettings.MemoryGbKey] = "64";
        Assert.Equal(32, new AppSettings(store, Medium).MemoryLimitGb);
        store.Values[AppSettings.MemoryGbKey] = "1";
        Assert.Equal(4, new AppSettings(store, Medium).MemoryLimitGb);
    }

    [Fact]
    public void ChangingAValue_RaisesChangedAndIsReadBack()
    {
        var settings = new AppSettings(new MemoryStore(), Medium);
        var raised = 0;
        settings.Changed += (_, _) => raised++;

        settings.ScaleLimit = 2;
        settings.Theme = ThemePreference.Dark;
        settings.MicaEnabled = false;
        settings.LogLevel = LogLevel.Debug;

        Assert.Equal(4, raised);
        Assert.Equal(2, settings.ScaleLimit);
        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.False(settings.MicaEnabled);
        Assert.Equal(LogLevel.Debug, settings.LogLevel);
    }

    [Fact]
    public void Throughput_IsRememberedPerEngineAndForgottenForAnotherDevice()
    {
        var store = new MemoryStore();
        var settings = new AppSettings(store, Medium);
        Assert.Null(settings.GetThroughput(RenderEngine.Gpu));

        settings.SetThroughput(RenderEngine.Gpu, 2.5);
        Assert.Equal(2.5, settings.GetThroughput(RenderEngine.Gpu));
        Assert.Null(settings.GetThroughput(RenderEngine.Cpu));

        var otherGpu = Medium with { Gpu = new GpuInfo("RTX 4090", 24 * Gib, false) };
        Assert.Null(new AppSettings(store, otherGpu).GetThroughput(RenderEngine.Gpu));
    }

    [Fact]
    public void Throughput_IsKeptPerTileSize()
    {
        var settings = new AppSettings(new MemoryStore(), Medium);
        settings.SetThroughput(RenderEngine.Gpu, 0.4, 128);
        settings.SetThroughput(RenderEngine.Gpu, 0.2, 64);

        Assert.Equal(0.4, settings.GetThroughput(RenderEngine.Gpu, 128));
        Assert.Equal(0.4, settings.GetThroughput(RenderEngine.Gpu));
        Assert.Equal(0.2, settings.GetThroughput(RenderEngine.Gpu, 64));
        Assert.Null(settings.GetThroughput(RenderEngine.Gpu, 96));
    }

    [Fact]
    public void JsonStore_KeepsValuesAcrossRuns()
    {
        var path = Path.Combine(_directory, "settings.json");
        var first = new JsonFileSettingsStore(path);
        first.Set("a", "1");
        first.Set("b", "2");
        first.Set("b", null);

        var second = new JsonFileSettingsStore(path);
        Assert.Equal("1", second.Get("a"));
        Assert.Null(second.Get("b"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void JsonStore_StartsEmptyWhenTheFileIsDamaged()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "{ not json");

        var store = new JsonFileSettingsStore(path);
        Assert.Null(store.Get("a"));
        store.Set("a", "1");
        Assert.Equal("1", new JsonFileSettingsStore(path).Get("a"));
    }

    private sealed class FakeWorkload(double megapixelsPerTile) : IBenchmarkWorkload
    {
        public int Runs { get; private set; }

        public List<int> TileSizes { get; } = [];

        public Task<double> RunTileAsync(RenderEngine engine, int tileSize, CancellationToken ct)
        {
            Runs++;
            TileSizes.Add(tileSize);
            return Task.FromResult(megapixelsPerTile);
        }
    }

    [Fact]
    public async Task Benchmark_WarmsUpThenTimesTilesAndRemembersTheSpeed()
    {
        var settings = new AppSettings(new MemoryStore(), Medium);
        var benchmark = new EngineBenchmark(settings);
        var workload = new FakeWorkload(0.25);

        var speed = await benchmark.EnsureAsync(RenderEngine.Gpu, workload, ct: TestContext.Current.CancellationToken);

        Assert.Equal(1 + EngineBenchmark.TimedTiles, workload.Runs);
        Assert.True(speed > 0);
        Assert.Equal(speed, settings.GetThroughput(RenderEngine.Gpu));

        await benchmark.EnsureAsync(RenderEngine.Gpu, workload, ct: TestContext.Current.CancellationToken);
        Assert.Equal(1 + EngineBenchmark.TimedTiles, workload.Runs); // remembered, not measured again
    }

    [Fact]
    public async Task Benchmark_MeasuresAndRemembersEachTileSizeApart()
    {
        var settings = new AppSettings(new MemoryStore(), Medium);
        var benchmark = new EngineBenchmark(settings);
        var workload = new FakeWorkload(0.25);

        await benchmark.EnsureAsync(RenderEngine.Gpu, workload, 128, TestContext.Current.CancellationToken);
        await benchmark.EnsureAsync(RenderEngine.Gpu, workload, 64, TestContext.Current.CancellationToken);

        Assert.Equal(2 * (1 + EngineBenchmark.TimedTiles), workload.Runs);
        Assert.All(workload.TileSizes.Take(1 + EngineBenchmark.TimedTiles), size => Assert.Equal(128, size));
        Assert.All(workload.TileSizes.Skip(1 + EngineBenchmark.TimedTiles), size => Assert.Equal(64, size));
        Assert.NotNull(settings.GetThroughput(RenderEngine.Gpu, 64));
        Assert.Null(settings.GetThroughput(RenderEngine.Gpu, 96));
    }

    [Fact]
    public async Task Benchmark_RefusesAnEngineTheDeviceDoesNotHave()
    {
        var benchmark = new EngineBenchmark(new AppSettings(new MemoryStore(), NoGpu));
        await Assert.ThrowsAsync<InvalidOperationException>(() => benchmark.MeasureAsync(RenderEngine.Npu, new FakeWorkload(1), ct: TestContext.Current.CancellationToken));
    }
}

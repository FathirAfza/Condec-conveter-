// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Diagnostics;
using Condec.Core.Devices;
using Condec.Core.Settings;

namespace Condec.Core.Upscale;

/// <summary>Runs one small tile on an engine, as the real upscaler would. Returns the megapixels of the result.</summary>
public interface IBenchmarkWorkload
{
    Task<double> RunTileAsync(RenderEngine engine, CancellationToken ct);
}

/// <summary>
/// Measures how fast an engine is, once per engine and device, and remembers it (DESIGN §8). The time estimates use
/// this number; nothing else gives a speed.
/// </summary>
public sealed class EngineBenchmark
{
    /// <summary>Tiles timed per measurement, after one tile that is not timed (loading the model and warming up the engine).</summary>
    public const int TimedTiles = 3;

    private readonly AppSettings _settings;

    public EngineBenchmark(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>The remembered speed, or a fresh measurement when there is none for this engine and device.</summary>
    public async Task<double> EnsureAsync(RenderEngine engine, IBenchmarkWorkload workload, CancellationToken ct = default)
    {
        if (_settings.GetThroughput(engine) is { } known)
        {
            return known;
        }

        return await MeasureAsync(engine, workload, ct);
    }

    /// <summary>
    /// Measures again, and remembers the result. The result is stored on the caller's own context (no ConfigureAwait(false)):
    /// the settings raise their Changed event when it is stored, and the window reacts to that on its own thread.
    /// </summary>
    public async Task<double> MeasureAsync(RenderEngine engine, IBenchmarkWorkload workload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (!_settings.Device.IsAvailable(engine))
        {
            throw new InvalidOperationException($"This device has no {engine} to measure.");
        }

        await workload.RunTileAsync(engine, ct);

        double megapixels = 0;
        var clock = Stopwatch.StartNew();
        for (var i = 0; i < TimedTiles; i++)
        {
            megapixels += await workload.RunTileAsync(engine, ct);
        }

        clock.Stop();

        var speed = megapixels / Math.Max(clock.Elapsed.TotalSeconds, 1e-6);
        _settings.SetThroughput(engine, speed);
        return speed;
    }
}

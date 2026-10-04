// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Diagnostics;

namespace Condec.Core.Upscale;

/// <summary>How hard an upscale may work, chosen in Settings (DESIGN §7.6). Each mode is a share of the time spent working.</summary>
public enum PerformanceMode
{
    ExtraHigh,
    High,
    Medium,
    Low,
}

/// <summary>
/// What a performance mode means for a render (DESIGN §7.6): the share of the time the engine works (the rest it rests between
/// tiles), the largest tile it may use, and for Memory saver the memory Condec may use.
/// </summary>
/// <param name="Duty">Above 0 and at most 1: the share of the render's time spent working.</param>
/// <param name="MaxTileSize">One of <see cref="TiledUpscaler.TileSizes"/>. A smaller tile is a shorter burst of work.</param>
/// <param name="MemoryLimitGb">The memory limit Memory saver imposes; null when the limit in Settings applies.</param>
public sealed record RenderPace(double Duty, int MaxTileSize, int? MemoryLimitGb)
{
    /// <summary>The memory Condec may use with Memory saver on (owner's choice, 2026-10-03).</summary>
    public const int MemorySaverGb = 2;

    /// <summary>The modes in the order Settings lists them.</summary>
    public static IReadOnlyList<PerformanceMode> Modes { get; } =
        [PerformanceMode.ExtraHigh, PerformanceMode.High, PerformanceMode.Medium, PerformanceMode.Low];

    /// <summary>Memory saver: 10% of the time, the smallest tile, 2 GB. It overrides the mode.</summary>
    public static RenderPace MemorySaver { get; } = new(0.1, TiledUpscaler.TileSizes[^1], MemorySaverGb);

    /// <summary>The share of the time a mode works: 80, 60, 40 or 20 percent (owner's choice, 2026-10-03).</summary>
    public static double DutyOf(PerformanceMode mode) => mode switch
    {
        PerformanceMode.ExtraHigh => 0.8,
        PerformanceMode.High => 0.6,
        PerformanceMode.Medium => 0.4,
        PerformanceMode.Low => 0.2,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public static RenderPace For(PerformanceMode mode, bool memorySaver) => memorySaver
        ? MemorySaver
        : new RenderPace(DutyOf(mode), mode == PerformanceMode.Low ? 64 : TiledUpscaler.DefaultTileSize, null);

    /// <summary>The share of the time as a whole percent: 80, 60, 40, 20 or 10.</summary>
    public int Percent => (int)Math.Round(Duty * 100, MidpointRounding.AwayFromZero);
}

/// <summary>How busy the rest of the device is, as Adaptive reads it (DESIGN §7.6). Null where Windows couldn't say.</summary>
/// <param name="OtherGpuPercent">The 3D engine use of the GPU by every other process, 0 to 100.</param>
/// <param name="MemoryLoadPercent">The share of the physical memory in use, 0 to 100.</param>
public readonly record struct SystemLoad(double? OtherGpuPercent, double? MemoryLoadPercent);

/// <summary>Reads <see cref="SystemLoad"/>. Never throws: what can't be read is null.</summary>
public interface ILoadMonitor
{
    SystemLoad Read();
}

/// <summary>
/// Adaptive (DESIGN §7.6): the device's state lowers the share of the time a render works, never raises it above the mode.
/// While another app uses the GPU the share is at most <see cref="BusyDuty"/>; while memory is nearly full it is halved.
/// A render that works hard starves an app that shares the GPU, and a starved app shows hardly any use (DESIGN §13 #64), so
/// the busy state is held until the others have been quiet for a few readings, not dropped the moment they look quiet.
/// </summary>
public sealed class AdaptivePace
{
    /// <summary>Other apps use the GPU at least this much: Adaptive holds the render back.</summary>
    public const double BusyGpuPercent = 20;

    /// <summary>Held back until other apps use the GPU less than this, <see cref="QuietReads"/> readings in a row: a gap between 10 and 20 keeps it from flipping.</summary>
    public const double QuietGpuPercent = 10;

    /// <summary>How many quiet readings in a row end the hold.</summary>
    public const int QuietReads = 2;

    /// <summary>The most a render works while another app uses the GPU: Low's share. On the author's laptop a frame-paced app kept 96% of its frames beside it, against 43% beside a render at 80% (DESIGN §13 #64).</summary>
    public const double BusyDuty = 0.2;

    /// <summary>The memory is at least this full: the render works half as much. A laptop with 8 GB rests near 90% with a browser open, and a render adds a few points, so 90% would fire on every render.</summary>
    public const double FullMemoryPercent = 95;

    private bool _gpuBusy;
    private int _quietReads;

    /// <summary>Another app was using the GPU and has not been quiet since.</summary>
    public bool GpuBusy => _gpuBusy;

    /// <summary>The share of the time with the device as it is: the mode's share, at most <see cref="BusyDuty"/> (or half) for a busy GPU and halved again for full memory, never below 10% (or the mode's own share when that is lower).</summary>
    public double Adjust(double duty, SystemLoad load)
    {
        UpdateGpu(load.OtherGpuPercent);

        var adjusted = duty;
        if (_gpuBusy)
        {
            adjusted = Math.Min(adjusted / 2, BusyDuty);
        }

        if (load.MemoryLoadPercent >= FullMemoryPercent)
        {
            adjusted /= 2;
        }

        return Math.Max(adjusted, Math.Min(duty, RenderPace.MemorySaver.Duty));
    }

    private void UpdateGpu(double? percent)
    {
        if (percent is not { } gpu)
        {
            return;
        }

        if (!_gpuBusy)
        {
            _gpuBusy = gpu >= BusyGpuPercent;
            _quietReads = 0;
        }
        else if (gpu < QuietGpuPercent)
        {
            if (++_quietReads >= QuietReads)
            {
                _gpuBusy = false;
                _quietReads = 0;
            }
        }
        else
        {
            _quietReads = 0;
        }
    }
}

/// <summary>
/// Rests between tiles so the engine works only <see cref="Duty"/> of the time (DESIGN §7.6): after a tile that took t, it waits
/// t × (1 / duty − 1). A GPU or CPU that works in bursts with pauses runs cooler and leaves room for other apps, at the cost of a
/// longer render. With a load monitor (Adaptive), the share is read again every few seconds and lowered while the device is busy.
/// The wait ends at once when the render is cancelled.
/// </summary>
public sealed class TilePacer
{
    /// <summary>How often Adaptive reads the device. Each reading takes a rest of at least <see cref="ProbePause"/>, which costs a render at ExtraHigh (rests of 0.1 s) about 7%.</summary>
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often it reads while holding the render back: at every rest. The rests are long then (0.5 s and more), so a reading costs nothing, and the render is freed sooner when the other app is done.</summary>
    public static readonly TimeSpan HeldCheckInterval = TimeSpan.Zero;

    /// <summary>
    /// The shortest rest a reading is taken across. What other apps want of the GPU shows only while this render rests: while it
    /// works, they are starved and look idle (DESIGN §13 #64).
    /// </summary>
    public static readonly TimeSpan ProbePause = TimeSpan.FromMilliseconds(500);

    private readonly Action<TimeSpan, CancellationToken> _wait;
    private readonly ILoadMonitor? _monitor;
    private readonly AdaptivePace _adaptive = new();
    private readonly TimeSpan _checkInterval;
    private readonly Func<long> _timestamp;
    private long? _lastCheck;

    /// <param name="duty">Above 0 and at most 1.</param>
    /// <param name="wait">Waits the given time or until cancelled; tests pass a stand-in.</param>
    /// <param name="monitor">Reads the device for Adaptive; null keeps <paramref name="duty"/>.</param>
    /// <param name="checkInterval">How often the monitor is read; <see cref="DefaultCheckInterval"/> when null.</param>
    /// <param name="timestamp">The clock, in <see cref="Stopwatch"/> ticks; tests pass a stand-in.</param>
    public TilePacer(double duty, Action<TimeSpan, CancellationToken>? wait = null, ILoadMonitor? monitor = null, TimeSpan? checkInterval = null, Func<long>? timestamp = null)
    {
        if (!(duty > 0 && duty <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(duty), duty, "The share of time working must be above 0 and at most 1.");
        }

        Duty = duty;
        CurrentDuty = duty;
        _wait = wait ?? Wait;
        _monitor = monitor;
        _checkInterval = checkInterval ?? DefaultCheckInterval;
        _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    }

    /// <summary>The mode's share of the time.</summary>
    public double Duty { get; }

    /// <summary>The share in use now: <see cref="Duty"/>, or less while Adaptive finds the device busy.</summary>
    public double CurrentDuty { get; private set; }

    /// <summary>
    /// Reads the device now, so the first rest already goes by it. Called before the first tile, while the render has not yet
    /// started to use the GPU: from then on a render that works hard starves the apps beside it and hides how much they want.
    /// </summary>
    public void Check()
    {
        if (_monitor is not null)
        {
            ReadDevice();
        }
    }

    /// <summary>The rest after <paramref name="work"/> that leaves the engine working <paramref name="duty"/> of the time.</summary>
    public static TimeSpan PauseAfter(TimeSpan work, double duty) =>
        work <= TimeSpan.Zero || duty >= 1 ? TimeSpan.Zero : work * ((1 / duty) - 1);

    /// <summary>Rests after a tile that took <paramref name="work"/>. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
    public void Rest(TimeSpan work, CancellationToken ct)
    {
        var pause = PauseAfter(work, CurrentDuty);
        var interval = _adaptive.GpuBusy && HeldCheckInterval < _checkInterval ? HeldCheckInterval : _checkInterval;
        if (_monitor is not null && (_lastCheck is not { } last || Stopwatch.GetElapsedTime(last, _timestamp()) >= interval))
        {
            // The reading is the others' use of the GPU across a rest of this render, and the rest goes on when the reading asks for a longer one.
            _ = _monitor.Read();
            var waited = pause > ProbePause ? pause : ProbePause;
            _wait(waited, ct);
            ct.ThrowIfCancellationRequested();
            ReadDevice();
            var needed = PauseAfter(work, CurrentDuty);
            if (needed > waited)
            {
                _wait(needed - waited, ct);
            }
        }
        else if (pause > TimeSpan.Zero)
        {
            _wait(pause, ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    private void ReadDevice()
    {
        _lastCheck = _timestamp();
        CurrentDuty = _adaptive.Adjust(Duty, _monitor!.Read());
    }

    private static void Wait(TimeSpan pause, CancellationToken ct) => ct.WaitHandle.WaitOne(pause);
}

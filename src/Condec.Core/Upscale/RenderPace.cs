// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

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

/// <summary>
/// Rests between tiles so the engine works only <see cref="Duty"/> of the time (DESIGN §7.6): after a tile that took t, it waits
/// t × (1 / duty − 1). A GPU or CPU that works in bursts with pauses runs cooler and leaves room for other apps, at the cost of a
/// longer render. The wait ends at once when the render is cancelled.
/// </summary>
public sealed class TilePacer
{
    private readonly Action<TimeSpan, CancellationToken> _wait;

    /// <param name="duty">Above 0 and at most 1.</param>
    /// <param name="wait">Waits the given time or until cancelled; tests pass a stand-in.</param>
    public TilePacer(double duty, Action<TimeSpan, CancellationToken>? wait = null)
    {
        if (!(duty > 0 && duty <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(duty), duty, "The share of time working must be above 0 and at most 1.");
        }

        Duty = duty;
        _wait = wait ?? Wait;
    }

    public double Duty { get; }

    /// <summary>The rest after <paramref name="work"/> that leaves the engine working <paramref name="duty"/> of the time.</summary>
    public static TimeSpan PauseAfter(TimeSpan work, double duty) =>
        work <= TimeSpan.Zero || duty >= 1 ? TimeSpan.Zero : work * ((1 / duty) - 1);

    /// <summary>Rests after a tile that took <paramref name="work"/>. Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
    public void Rest(TimeSpan work, CancellationToken ct)
    {
        var pause = PauseAfter(work, Duty);
        if (pause > TimeSpan.Zero)
        {
            _wait(pause, ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    private static void Wait(TimeSpan pause, CancellationToken ct) => ct.WaitHandle.WaitOne(pause);
}

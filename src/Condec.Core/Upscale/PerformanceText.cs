// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Localization;

namespace Condec.Core.Upscale;

/// <summary>The names of the performance modes as Settings and Upscale Image show them (DESIGN §7.6).</summary>
public static class PerformanceText
{
    /// <summary>"Extra high (80%)".</summary>
    public static string Mode(PerformanceMode mode, CultureInfo? culture = null) =>
        Loc.Format(culture, "Performance.WithPercent", Loc.Get($"Performance.{mode}", culture), RenderPace.For(mode, memorySaver: false).Percent);

    /// <summary>"Adaptive (up to 80%)": what a render works at while the device is idle, which Adaptive may raise above the mode.</summary>
    public static string Adaptive(double idleDuty, CultureInfo? culture = null) =>
        Loc.Format(culture, "Performance.Adaptive", (int)Math.Round(idleDuty * 100, MidpointRounding.AwayFromZero));

    /// <summary>"Adaptive (up to 80%)" when Adaptive goes above the mode, else <see cref="Describe(PerformanceMode, bool, CultureInfo?)"/>.</summary>
    public static string Describe(PerformanceMode mode, bool memorySaver, double? adaptiveIdleDuty, CultureInfo? culture = null) =>
        adaptiveIdleDuty is { } idle ? Adaptive(idle, culture) : Describe(mode, memorySaver, culture);

    /// <summary>The mode, or "Memory saver (10%)" when that is on, because it overrides the mode.</summary>
    public static string Describe(PerformanceMode mode, bool memorySaver, CultureInfo? culture = null) => memorySaver
        ? Loc.Format(culture, "Performance.WithPercent", Loc.Get("Performance.MemorySaver", culture), RenderPace.MemorySaver.Percent)
        : Mode(mode, culture);
}

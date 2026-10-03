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

    /// <summary>The mode, or "Memory saver (10%)" when that is on, because it overrides the mode.</summary>
    public static string Describe(PerformanceMode mode, bool memorySaver, CultureInfo? culture = null) => memorySaver
        ? Loc.Format(culture, "Performance.WithPercent", Loc.Get("Performance.MemorySaver", culture), RenderPace.MemorySaver.Percent)
        : Mode(mode, culture);
}

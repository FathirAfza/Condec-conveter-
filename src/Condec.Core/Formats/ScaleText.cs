// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Localization;

namespace Condec.Core.Formats;

/// <summary>
/// The numbers of the upscale and CAD screens as DESIGN §2 writes them: "4×", "400%", "1280 × 720", "14,7 MP",
/// "± 7 detik". The decimal separator comes from the language's resources, so the text is the same on every machine.
/// </summary>
public static class ScaleText
{
    /// <summary>"4×" or "3,5×".</summary>
    public static string Format(double scale, CultureInfo? culture = null) => Number(scale, culture) + "×";

    /// <summary>"400%".</summary>
    public static string Percent(double scale) => ((long)Math.Round(scale * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>"1280 × 720" (a space on both sides of ×).</summary>
    public static string Dimensions(long width, long height) => string.Create(CultureInfo.InvariantCulture, $"{width} × {height}");

    /// <summary>"0,9 MP" or "14,7 MP": one decimal.</summary>
    public static string Megapixels(long pixels, CultureInfo? culture = null) => Number(pixels / 1_000_000d, culture, "0.0") + " MP";

    /// <summary>"5,2 GB": one decimal, rounded up, because it names what is needed.</summary>
    public static string Gigabytes(long bytes, CultureInfo? culture = null) =>
        Number(Math.Ceiling(bytes / (1024d * 1024 * 1024) * 10) / 10, culture, "0.0") + " GB";

    /// <summary>"± " in front of a figure that is a guess.</summary>
    public static string Approximately(string text) => "± " + text;

    /// <summary>"< 1 second", "7 seconds", "2 min 5 sec", "1 h 20 min".</summary>
    public static string Duration(double seconds, CultureInfo? culture = null)
    {
        if (seconds < 1)
        {
            return Loc.Get("Duration.LessThanSecond", culture);
        }

        var total = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
        if (total < 60)
        {
            return Loc.Format(culture, "Duration.Seconds", total);
        }

        if (total < 3600)
        {
            return Loc.Format(culture, "Duration.MinutesSeconds", total / 60, total % 60);
        }

        return Loc.Format(culture, "Duration.HoursMinutes", total / 3600, total % 3600 / 60);
    }

    private static string Number(double value, CultureInfo? culture, string format = "0.#") =>
        value.ToString(format, CultureInfo.InvariantCulture).Replace(".", Loc.Get("Size.DecimalSeparator", culture));
}

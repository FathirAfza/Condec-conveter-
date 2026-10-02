// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Localization;

namespace Condec.Core.Formats;

/// <summary>
/// Times and sizes in the app's language ("Yesterday, 7:40 PM" and "2.4 MB", or "Kemarin, 19.40" and "2,4 MB").
/// The patterns live in the string resources and are applied with the invariant culture, so the text is the
/// same whatever culture data (ICU or NLS) the machine has.
/// </summary>
public static class DisplayFormat
{
    private static readonly string[] SizeUnits = ["KB", "MB", "GB", "TB"];

    /// <summary>"Just now", "Today, 8:05 AM", "Yesterday, 7:40 PM", "Sep 22, 3:12 PM" or "Dec 31, 2025, 11:59 PM".</summary>
    /// <param name="local">The moment to describe, in local time.</param>
    /// <param name="nowLocal">The current local time.</param>
    /// <param name="culture">The language to write in; the app's language when null.</param>
    public static string FormatTimestamp(DateTime local, DateTime nowLocal, CultureInfo? culture = null)
    {
        var elapsed = nowLocal - local;
        if (elapsed.Duration() < TimeSpan.FromMinutes(1))
        {
            return Loc.Get("Time.JustNow", culture);
        }

        var time = local.ToString(Loc.Get("Time.ClockPattern", culture), CultureInfo.InvariantCulture);
        if (local.Date == nowLocal.Date)
        {
            return Loc.Format(culture, "Time.Today", time);
        }

        if (local.Date == nowLocal.Date.AddDays(-1))
        {
            return Loc.Format(culture, "Time.Yesterday", time);
        }

        var month = Loc.Get("Time.Months", culture).Split('|')[local.Month - 1];
        var date = local.Year == nowLocal.Year
            ? Loc.Format(culture, "Time.DayMonth", local.Day, month)
            : Loc.Format(culture, "Time.DayMonthYear", local.Day, month, local.Year);
        return Loc.Format(culture, "Time.WithTime", date, time);
    }

    /// <summary>"512 bytes", "2.4 MB", "245 MB" (1 KB = 1024 bytes, as in File Explorer).</summary>
    /// <param name="culture">The language to write in; the app's language when null.</param>
    public static string FormatFileSize(long bytes, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        if (bytes < 1024)
        {
            return Loc.Format(culture, "Size.Bytes", bytes);
        }

        double value = bytes;
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < SizeUnits.Length - 1);

        // One decimal below 100, whole numbers from there. Rounding 1023,96 must move up a unit, not print "1024 KB".
        var rounded = Round(value);
        if (rounded >= 1024 && unit < SizeUnits.Length - 1)
        {
            rounded = Round(value / 1024);
            unit++;
        }

        var number = rounded < 100
            ? rounded.ToString("0.0", CultureInfo.InvariantCulture).Replace(".", Loc.Get("Size.DecimalSeparator", culture))
            : rounded.ToString("0", CultureInfo.InvariantCulture);

        return $"{number} {SizeUnits[unit]}";
    }

    private static double Round(double value) =>
        Math.Round(value, value < 100 ? 1 : 0, MidpointRounding.AwayFromZero);
}

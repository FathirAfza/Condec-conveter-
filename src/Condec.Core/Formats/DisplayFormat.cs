using System.Globalization;

namespace Condec.Core.Formats;

/// <summary>
/// Times and sizes in Indonesian ("Kemarin, 19.40", "2,4 MB"), formatted explicitly so the text is the
/// same whatever culture data (ICU or NLS) the machine has.
/// </summary>
public static class DisplayFormat
{
    private static readonly string[] MonthAbbreviations =
        ["Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des"];

    private static readonly string[] SizeUnits = ["KB", "MB", "GB", "TB"];

    /// <summary>"Baru saja", "Hari ini, 08.05", "Kemarin, 19.40", "22 Sep, 15.12" or "31 Des 2025, 23.59".</summary>
    /// <param name="local">The moment to describe, in local time.</param>
    /// <param name="nowLocal">The current local time.</param>
    public static string FormatTimestamp(DateTime local, DateTime nowLocal)
    {
        var elapsed = nowLocal - local;
        if (elapsed.Duration() < TimeSpan.FromMinutes(1))
        {
            return "Baru saja";
        }

        var time = string.Create(CultureInfo.InvariantCulture, $"{local.Hour:D2}.{local.Minute:D2}");
        if (local.Date == nowLocal.Date)
        {
            return $"Hari ini, {time}";
        }

        if (local.Date == nowLocal.Date.AddDays(-1))
        {
            return $"Kemarin, {time}";
        }

        var day = string.Create(CultureInfo.InvariantCulture, $"{local.Day} {MonthAbbreviations[local.Month - 1]}");
        return local.Year == nowLocal.Year
            ? $"{day}, {time}"
            : string.Create(CultureInfo.InvariantCulture, $"{day} {local.Year}, {time}");
    }

    /// <summary>"512 byte", "2,4 MB", "245 MB" (1 KB = 1024 byte, as in File Explorer).</summary>
    public static string FormatFileSize(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} byte");
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
            ? rounded.ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')
            : rounded.ToString("0", CultureInfo.InvariantCulture);

        return $"{number} {SizeUnits[unit]}";
    }

    private static double Round(double value) =>
        Math.Round(value, value < 100 ? 1 : 0, MidpointRounding.AwayFromZero);
}

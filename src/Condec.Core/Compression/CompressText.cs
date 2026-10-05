// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Localization;

namespace Condec.Core.Compression;

public static class CompressText
{
    /// <summary>
    /// A size limit as it was typed, in the decimal units it is counted in (DESIGN §6.5): "1 MB", "0.5 MB", "500 KB".
    /// File sizes elsewhere use 1 KB = 1024 bytes, as File Explorer does; a limit doesn't.
    /// </summary>
    /// <param name="culture">The language to write in; the app's language when null.</param>
    public static string Limit(long bytes, CultureInfo? culture = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        var (value, unit) = bytes >= 1_000_000 ? (bytes / 1_000_000d, "MB") : (bytes / 1_000d, "KB");
        var number = Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture).Replace(".", Loc.Get("Size.DecimalSeparator", culture), StringComparison.Ordinal);
        return $"{number} {unit}";
    }

    /// <summary>The number in the limit box, written as the page writes numbers: "0.5" in English, "0,5" in Indonesian, never grouped.</summary>
    /// <param name="culture">The language to write in; the app's language when null.</param>
    public static string LimitNumber(double value, CultureInfo? culture = null) =>
        value.ToString("0.###", CultureInfo.InvariantCulture).Replace(".", Loc.Get("Size.DecimalSeparator", culture), StringComparison.Ordinal);

    /// <summary>
    /// Reads the limit box (DESIGN §6.5). One comma or one point is the decimal point in any language, because Windows
    /// can show English while its number format is Indonesian: "0,5" and "0.5" both mean a half. Neither is ever read
    /// as a thousands separator, so "1.000" is 1; that only makes a limit smaller, never lets a file through that is too big.
    /// </summary>
    /// <returns>Null when the text is not such a number: empty, a sign, letters, or two separators.</returns>
    public static double? ParseLimit(string text)
    {
        var trimmed = text.Trim();
        var digits = 0;
        var separators = 0;
        foreach (var c in trimmed)
        {
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c is ',' or '.')
            {
                separators++;
            }
            else
            {
                return null;
            }
        }

        if (digits == 0 || separators > 1)
        {
            return null;
        }

        return double.Parse(trimmed.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }
}

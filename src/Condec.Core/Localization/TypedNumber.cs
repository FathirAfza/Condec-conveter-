// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;

namespace Condec.Core.Localization;

/// <summary>
/// Numbers typed into a number box (DESIGN §2). The page writes numbers in the app's language, but people type the way
/// their Windows number format does, and the two can differ (an English display with an Indonesian format writes
/// "5.5 MB" and is used to typing "2,5"). A number with only one possible reading is read that way in any language;
/// only a real ambiguity is settled by the Windows number format.
/// </summary>
/// <summary>
/// One number box's text (DESIGN §2): written with <see cref="TypedNumber.Format"/>, read with <see cref="TypedNumber.Parse"/>.
/// A box reads its text again whenever it loses focus, so text it wrote itself is that number again:
/// "1.234" written in English would otherwise read as 1234 with an Indonesian number format.
/// </summary>
/// <param name="decimalSeparator">The Windows number format's decimal separator.</param>
/// <param name="groupSeparator">The Windows number format's thousands separator.</param>
/// <param name="culture">The language to write in; the app's language when null.</param>
public sealed class TypedNumberField(string decimalSeparator, string groupSeparator, CultureInfo? culture = null)
{
    private (string Text, double Value)? _written;

    public string Write(double value)
    {
        if (double.IsNaN(value))
        {
            return string.Empty;
        }

        var text = TypedNumber.Format(value, culture);
        _written = (text, value);
        return text;
    }

    public double? Read(string text) =>
        _written is { } written && text.Trim() == written.Text
            ? written.Value
            : TypedNumber.Parse(text, decimalSeparator, groupSeparator);
}

public static class TypedNumber
{
    /// <summary>Written as the page writes numbers: "0.5" in English, "0,5" in Indonesian, never grouped, at most six decimals.</summary>
    /// <param name="culture">The language to write in; the app's language when null.</param>
    public static string Format(double value, CultureInfo? culture = null) =>
        value.ToString("0.######", CultureInfo.InvariantCulture).Replace(".", Loc.Get("Size.DecimalSeparator", culture), StringComparison.Ordinal);

    /// <summary>
    /// Reads digits with points and commas.
    /// <list type="bullet">
    /// <item>One separator is the decimal point ("2.5", "2,5", "0,500"), unless the text also reads as thousands:
    /// one to three digits not starting with 0, then exactly three ("1.000", "12,500"). That one is decided by the Windows
    /// number format: thousands when the separator is its group separator, a decimal point otherwise.</item>
    /// <item>The same separator more than once is thousands ("1.000.000"); both kinds is thousands then a decimal
    /// point ("1.000,5", "1,000.5").</item>
    /// </list>
    /// </summary>
    /// <param name="decimalSeparator">The Windows number format's decimal separator.</param>
    /// <param name="groupSeparator">The Windows number format's thousands separator.</param>
    /// <returns>Null when the text is not such a number: empty, a sign, letters, or separators in impossible places.</returns>
    public static double? Parse(string text, string decimalSeparator, string groupSeparator)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.Any(c => !char.IsAsciiDigit(c) && c is not ('.' or ',')) || !trimmed.Any(char.IsAsciiDigit))
        {
            return null;
        }

        var separators = trimmed.Where(c => c is '.' or ',').Distinct().ToArray();
        var count = trimmed.Count(c => c is '.' or ',');
        switch (separators.Length)
        {
            case 0:
                return Number(trimmed);

            case 1 when count == 1:
                var separator = separators[0].ToString();
                if (IsGrouped(trimmed, separators[0]) && separator == groupSeparator && separator != decimalSeparator)
                {
                    return Number(trimmed.Replace(separator, string.Empty, StringComparison.Ordinal));
                }

                return Number(trimmed.Replace(separators[0], '.'));

            case 1:
                return IsGrouped(trimmed, separators[0]) ? Number(trimmed.Replace(separators[0].ToString(), string.Empty, StringComparison.Ordinal)) : null;

            default:
                // Both kinds: the last one is the decimal point, after thousands of the other kind.
                var point = trimmed[trimmed.LastIndexOfAny(['.', ','])];
                var whole = trimmed[..trimmed.LastIndexOf(point)];
                return IsGrouped(whole, point == '.' ? ',' : '.')
                    ? Number(whole.Replace(point == '.' ? "," : ".", string.Empty, StringComparison.Ordinal) + "." + trimmed[(trimmed.LastIndexOf(point) + 1)..])
                    : null;
        }
    }

    /// <summary>"1.000", "12,500", "1.000.000": one to three digits not starting with 0, then groups of exactly three.</summary>
    private static bool IsGrouped(string text, char separator)
    {
        var groups = text.Split(separator);
        return groups.Length > 1
            && groups[0].Length is >= 1 and <= 3 && groups[0][0] != '0' && groups[0].All(char.IsAsciiDigit)
            && groups.Skip(1).All(g => g.Length == 3 && g.All(char.IsAsciiDigit));
    }

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ? value : null;
}

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
}

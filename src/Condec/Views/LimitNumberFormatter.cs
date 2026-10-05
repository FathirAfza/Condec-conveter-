// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Compression;
using Windows.Globalization.NumberFormatting;

namespace Condec.Views;

/// <summary>
/// The NumberFormatter of the size limit box (DESIGN §6.5): written in the app's language, read with
/// <see cref="CompressText.ParseLimit"/>. The stock one follows the regional number format, so on an English
/// Windows with an Indonesian format "0.5" was put back to the number before without a word.
/// NumberBox requires the object to be an INumberParser too.
/// </summary>
public sealed partial class LimitNumberFormatter : INumberFormatter2, INumberParser
{
    public string FormatDouble(double value) => double.IsNaN(value) ? string.Empty : CompressText.LimitNumber(value);

    public string FormatInt(long value) => CompressText.LimitNumber(value);

    public string FormatUInt(ulong value) => CompressText.LimitNumber(value);

    public double? ParseDouble(string text) => CompressText.ParseLimit(text);

    public long? ParseInt(string text) =>
        CompressText.ParseLimit(text) is { } value && value == Math.Floor(value) && value <= long.MaxValue ? (long)value : null;

    public ulong? ParseUInt(string text) =>
        CompressText.ParseLimit(text) is { } value && value == Math.Floor(value) && value <= ulong.MaxValue ? (ulong)value : null;
}

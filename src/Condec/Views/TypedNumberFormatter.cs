// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Localization;
using Windows.Globalization.NumberFormatting;

namespace Condec.Views;

/// <summary>
/// The NumberFormatter of every NumberBox (DESIGN §2), one per box: a <see cref="TypedNumberField"/> with the Windows
/// number format. The stock one reads only that format, so on an English display with an Indonesian format "0.2" was
/// put back to the number before without a word. NumberBox requires an INumberParser too.
/// </summary>
public sealed partial class TypedNumberFormatter : INumberFormatter2, INumberParser
{
    private readonly TypedNumberField _field = new(
        CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator,
        CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator);

    public string FormatDouble(double value) => _field.Write(value);

    public string FormatInt(long value) => _field.Write(value);

    public string FormatUInt(ulong value) => _field.Write(value);

    public double? ParseDouble(string text) => _field.Read(text);

    public long? ParseInt(string text) =>
        _field.Read(text) is { } value && value == Math.Floor(value) && value <= long.MaxValue ? (long)value : null;

    public ulong? ParseUInt(string text) =>
        _field.Read(text) is { } value && value == Math.Floor(value) && value <= ulong.MaxValue ? (ulong)value : null;
}

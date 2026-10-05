// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Localization;

namespace Condec.Tests;

/// <summary>Numbers typed into a number box (DESIGN §2): one reading is read in any format, an ambiguity follows Windows.</summary>
public sealed class TypedNumberTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id");

    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData("0,5", 0.5)]
    [InlineData(" 2,5 ", 2.5)]
    [InlineData("2.25", 2.25)]
    [InlineData("12.3456", 12.3456)]
    [InlineData("500", 500)]
    [InlineData(".5", 0.5)]
    [InlineData("5.", 5)]
    [InlineData("0,500", 0.5)]
    [InlineData("1000000", 1_000_000)]
    [InlineData("1.000.000", 1_000_000)]
    [InlineData("1,000,000", 1_000_000)]
    [InlineData("1.000,5", 1000.5)]
    [InlineData("1,000.5", 1000.5)]
    public void OneReading_IsReadThatWay_WhateverTheWindowsFormat(string text, double expected)
    {
        Assert.Equal(expected, TypedNumber.Parse(text, ".", ","));
        Assert.Equal(expected, TypedNumber.Parse(text, ",", "."));
    }

    [Theory]
    [InlineData("1.000", ",", ".", 1000)]
    [InlineData("1.000", ".", ",", 1)]
    [InlineData("12,500", ".", ",", 12_500)]
    [InlineData("12,500", ",", ".", 12.5)]
    [InlineData("1,000", ",", " ", 1)]
    [InlineData("1.000", ",", " ", 1)]
    public void ThousandsOrDecimals_IsDecidedByTheWindowsFormat(string text, string decimalSeparator, string groupSeparator, double expected) =>
        Assert.Equal(expected, TypedNumber.Parse(text, decimalSeparator, groupSeparator));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData("1,5.2")]
    [InlineData("1.00.000")]
    [InlineData("1.000.00")]
    [InlineData("1,000.5.2")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("1e3")]
    [InlineData("5 MB")]
    [InlineData("abc")]
    public void WhatIsNotSuchANumber_IsRefused(string text)
    {
        Assert.Null(TypedNumber.Parse(text, ".", ","));
        Assert.Null(TypedNumber.Parse(text, ",", "."));
    }

    [Theory]
    [InlineData(0.5, "0.5", "0,5")]
    [InlineData(1, "1", "1")]
    [InlineData(2.25, "2.25", "2,25")]
    [InlineData(0.1234567, "0.123457", "0,123457")]
    [InlineData(1_000_000, "1000000", "1000000")]
    public void Numbers_AreWrittenAsThePageWritesThem(double value, string english, string indonesian)
    {
        Assert.Equal(english, TypedNumber.Format(value, English));
        Assert.Equal(indonesian, TypedNumber.Format(value, Indonesian));
    }

    [Fact]
    public void TextTheBoxWrote_ReadsBackAsThatNumber_EvenWhenWindowsWouldReadItAsThousands()
    {
        // An English display with an Indonesian number format: "1.234" alone would be 1234.
        var field = new TypedNumberField(",", ".", English);
        Assert.Equal("1.234", field.Write(1.234));
        Assert.Equal(1.234, field.Read("1.234"));
        Assert.Equal(1.234, field.Read(" 1.234 "));
        Assert.Equal(1235, field.Read("1.235"));
        Assert.Equal(string.Empty, field.Write(double.NaN));
    }
}

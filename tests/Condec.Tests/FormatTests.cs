// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Formats;

namespace Condec.Tests;

public class FormatTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id");

    [Theory]
    [InlineData(".pdf", "PDF", "Dokumen PDF")]
    [InlineData(".docx", "DOCX", "Dokumen Word")]
    [InlineData(".md", "Markdown", "Dokumen Markdown")]
    [InlineData(".dxf", "DXF (gambar CAD)", "Gambar CAD DXF")]
    [InlineData(".JPG", "JPG", "Gambar JPEG")]
    [InlineData(".xyz", "XYZ", "File XYZ")]
    public void Catalog_GivesIndonesianNames(string extension, string targetLabel, string kindName)
    {
        Assert.Equal(targetLabel, FormatCatalog.GetTargetLabel(extension, Indonesian));
        Assert.Equal(kindName, FormatCatalog.GetKindName(extension, Indonesian));
    }

    [Theory]
    [InlineData(".pdf", "PDF", "PDF document")]
    [InlineData(".docx", "DOCX", "Word document")]
    [InlineData(".md", "Markdown", "Markdown document")]
    [InlineData(".dxf", "DXF (CAD drawing)", "DXF CAD drawing")]
    [InlineData(".JPG", "JPG", "JPEG image")]
    [InlineData(".xyz", "XYZ", "XYZ file")]
    public void Catalog_GivesEnglishNames(string extension, string targetLabel, string kindName)
    {
        Assert.Equal(targetLabel, FormatCatalog.GetTargetLabel(extension, English));
        Assert.Equal(kindName, FormatCatalog.GetKindName(extension, English));
    }

    [Theory]
    [InlineData("png", ".png")]
    [InlineData(".PNG", ".png")]
    [InlineData(" .Tiff ", ".tiff")]
    public void Normalize_GivesLowerCaseWithDot(string input, string expected)
    {
        Assert.Equal(expected, FileExtension.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    public void Normalize_RejectsEmpty(string input)
    {
        Assert.Throws<ArgumentException>(() => FileExtension.Normalize(input));
    }

    [Theory]
    [InlineData(@"C:\data\Laporan.DOCX", ".docx")]
    [InlineData(@"C:\data\README", "")]
    [InlineData(@"C:\data\arsip.", "")]
    public void FromPath_ReadsTheExtension(string path, string expected)
    {
        Assert.Equal(expected, FileExtension.FromPath(path));
    }

    [Fact]
    public void ToCode_IsUpperCaseWithoutDot()
    {
        Assert.Equal("HEIC", FileExtension.ToCode(".heic"));
    }

    [Theory]
    [InlineData(2026, 9, 24, 9, 59, 30, "Baru saja")]
    [InlineData(2026, 9, 24, 10, 0, 30, "Baru saja")]
    [InlineData(2026, 9, 24, 8, 5, 0, "Hari ini, 08.05")]
    [InlineData(2026, 9, 23, 19, 40, 0, "Kemarin, 19.40")]
    [InlineData(2026, 9, 22, 15, 12, 0, "22 Sep, 15.12")]
    [InlineData(2026, 8, 20, 8, 47, 0, "20 Agu, 08.47")]
    [InlineData(2025, 12, 31, 23, 59, 0, "31 Des 2025, 23.59")]
    public void Timestamp_IsFormattedLikeTheDesign(int year, int month, int day, int hour, int minute, int second, string expected)
    {
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.Equal(expected, DisplayFormat.FormatTimestamp(new DateTime(year, month, day, hour, minute, second), now, Indonesian));
    }

    [Theory]
    [InlineData(2026, 9, 24, 9, 59, 30, "Just now")]
    [InlineData(2026, 9, 24, 8, 5, 0, "Today, 8:05 AM")]
    [InlineData(2026, 9, 23, 19, 40, 0, "Yesterday, 7:40 PM")]
    [InlineData(2026, 9, 22, 15, 12, 0, "Sep 22, 3:12 PM")]
    [InlineData(2026, 8, 20, 8, 47, 0, "Aug 20, 8:47 AM")]
    [InlineData(2025, 12, 31, 23, 59, 0, "Dec 31, 2025, 11:59 PM")]
    public void Timestamp_InEnglish(int year, int month, int day, int hour, int minute, int second, string expected)
    {
        var now = new DateTime(2026, 9, 24, 10, 0, 0);

        Assert.Equal(expected, DisplayFormat.FormatTimestamp(new DateTime(year, month, day, hour, minute, second), now, English));
    }

    [Fact]
    public void Timestamp_YesterdayAcrossMonthBoundary()
    {
        Assert.Equal(
            "Kemarin, 23.00",
            DisplayFormat.FormatTimestamp(new DateTime(2026, 9, 30, 23, 0, 0), new DateTime(2026, 10, 1, 0, 30, 0), Indonesian));
    }

    [Theory]
    [InlineData(0L, "0 byte")]
    [InlineData(1023L, "1023 byte")]
    [InlineData(1024L, "1,0 KB")]
    [InlineData(2_516_582L, "2,4 MB")]
    [InlineData(1_153_434L, "1,1 MB")]
    [InlineData(157_286_400L, "150 MB")]
    [InlineData(1_048_575L, "1,0 MB")]
    [InlineData(104_805_376L, "100 MB")]
    [InlineData(5_368_709_120L, "5,0 GB")]
    public void FileSize_UsesDecimalCommaAndBinaryUnits(long bytes, string expected)
    {
        Assert.Equal(expected, DisplayFormat.FormatFileSize(bytes, Indonesian));
    }

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(2_516_582L, "2.4 MB")]
    [InlineData(157_286_400L, "150 MB")]
    public void FileSize_UsesDecimalPointInEnglish(long bytes, string expected)
    {
        Assert.Equal(expected, DisplayFormat.FormatFileSize(bytes, English));
    }
}

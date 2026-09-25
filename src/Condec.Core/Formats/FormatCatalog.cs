// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Formats;

/// <summary>
/// Indonesian display names per extension. The target label is what the format list shows
/// ("PDF", "DXF (gambar CAD)"); the kind name describes a chosen file ("Dokumen Word · 2,4 MB").
/// </summary>
public static class FormatCatalog
{
    private sealed record Names(string TargetLabel, string KindName);

    private static readonly Dictionary<string, Names> ByExtension = new(StringComparer.Ordinal)
    {
        [".jpg"] = new("JPG", "Gambar JPEG"),
        [".jpeg"] = new("JPEG", "Gambar JPEG"),
        [".png"] = new("PNG", "Gambar PNG"),
        [".bmp"] = new("BMP", "Gambar BMP"),
        [".gif"] = new("GIF", "Gambar GIF"),
        [".tif"] = new("TIFF", "Gambar TIFF"),
        [".tiff"] = new("TIFF", "Gambar TIFF"),
        [".heic"] = new("HEIC", "Gambar HEIC"),
        [".heif"] = new("HEIF", "Gambar HEIF"),
        [".webp"] = new("WebP", "Gambar WebP"),

        [".mp3"] = new("MP3", "Audio MP3"),
        [".m4a"] = new("M4A", "Audio M4A"),
        [".wav"] = new("WAV", "Audio WAV"),
        [".wma"] = new("WMA", "Audio WMA"),
        [".flac"] = new("FLAC", "Audio FLAC"),
        [".mp4"] = new("MP4", "Video MP4"),
        [".wmv"] = new("WMV", "Video WMV"),

        [".pdf"] = new("PDF", "Dokumen PDF"),
        [".docx"] = new("DOCX", "Dokumen Word"),
        [".doc"] = new("DOC", "Dokumen Word"),
        [".odt"] = new("ODT", "Dokumen OpenDocument"),
        [".rtf"] = new("RTF", "Dokumen RTF"),
        [".txt"] = new("TXT", "Teks biasa"),
        [".html"] = new("HTML", "Halaman web"),
        [".htm"] = new("HTML", "Halaman web"),
        [".md"] = new("Markdown", "Dokumen Markdown"),
        [".xlsx"] = new("XLSX", "Lembar kerja Excel"),
        [".xls"] = new("XLS", "Lembar kerja Excel"),
        [".ods"] = new("ODS", "Lembar kerja OpenDocument"),
        [".pptx"] = new("PPTX", "Presentasi PowerPoint"),
        [".ppt"] = new("PPT", "Presentasi PowerPoint"),
        [".odp"] = new("ODP", "Presentasi OpenDocument"),

        [".dxf"] = new("DXF (gambar CAD)", "Gambar CAD DXF"),
        [".dwg"] = new("DWG (gambar CAD)", "Gambar CAD DWG"),
    };

    /// <summary>Every extension with a display name, in catalog order.</summary>
    public static IReadOnlyCollection<string> KnownExtensions => ByExtension.Keys;

    public static string GetTargetLabel(string extension) =>
        ByExtension.TryGetValue(FileExtension.Normalize(extension), out var names)
            ? names.TargetLabel
            : FileExtension.ToCode(extension);

    public static string GetKindName(string extension) =>
        ByExtension.TryGetValue(FileExtension.Normalize(extension), out var names)
            ? names.KindName
            : "File " + FileExtension.ToCode(extension);
}

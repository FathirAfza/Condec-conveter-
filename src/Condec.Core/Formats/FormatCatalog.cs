// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Localization;

namespace Condec.Core.Formats;

/// <summary>
/// Display names per extension, in the app's language. The target label is what the format list shows
/// ("PDF", "DXF (CAD drawing)"); the kind name describes a chosen file ("Word document · 2.4 MB").
/// </summary>
public static class FormatCatalog
{
    /// <param name="Label">The format's name, the same in every language; null when <paramref name="LabelKey"/> is used.</param>
    /// <param name="LabelKey">The resource that holds the label, for the two CAD formats whose label has a translated part.</param>
    /// <param name="KindKey">The resource that describes a file of this format.</param>
    /// <param name="KindArgument">Goes into {0} of the kind, for kinds shared by several formats ("{0} image").</param>
    private sealed record Names(string? Label, string? LabelKey, string KindKey, string? KindArgument);

    private static Names Plain(string label, string kindKey, string? kindArgument = null) => new(label, null, kindKey, kindArgument);

    private static readonly Dictionary<string, Names> ByExtension = new(StringComparer.Ordinal)
    {
        [".jpg"] = Plain("JPG", "Kind.Image", "JPEG"),
        [".jpeg"] = Plain("JPEG", "Kind.Image", "JPEG"),
        [".png"] = Plain("PNG", "Kind.Image", "PNG"),
        [".bmp"] = Plain("BMP", "Kind.Image", "BMP"),
        [".gif"] = Plain("GIF", "Kind.Image", "GIF"),
        [".tif"] = Plain("TIFF", "Kind.Image", "TIFF"),
        [".tiff"] = Plain("TIFF", "Kind.Image", "TIFF"),
        [".heic"] = Plain("HEIC", "Kind.Image", "HEIC"),
        [".heif"] = Plain("HEIF", "Kind.Image", "HEIF"),
        [".webp"] = Plain("WebP", "Kind.Image", "WebP"),

        [".mp3"] = Plain("MP3", "Kind.Audio", "MP3"),
        [".m4a"] = Plain("M4A", "Kind.Audio", "M4A"),
        [".wav"] = Plain("WAV", "Kind.Audio", "WAV"),
        [".wma"] = Plain("WMA", "Kind.Audio", "WMA"),
        [".flac"] = Plain("FLAC", "Kind.Audio", "FLAC"),
        [".mp4"] = Plain("MP4", "Kind.Video", "MP4"),
        [".wmv"] = Plain("WMV", "Kind.Video", "WMV"),

        [".pdf"] = Plain("PDF", "Kind.Pdf"),
        [".docx"] = Plain("DOCX", "Kind.Word"),
        [".doc"] = Plain("DOC", "Kind.Word"),
        [".odt"] = Plain("ODT", "Kind.OpenDocumentText"),
        [".rtf"] = Plain("RTF", "Kind.Rtf"),
        [".txt"] = Plain("TXT", "Kind.PlainText"),
        [".html"] = Plain("HTML", "Kind.WebPage"),
        [".htm"] = Plain("HTML", "Kind.WebPage"),
        [".md"] = Plain("Markdown", "Kind.Markdown"),
        [".xlsx"] = Plain("XLSX", "Kind.Excel"),
        [".xls"] = Plain("XLS", "Kind.Excel"),
        [".ods"] = Plain("ODS", "Kind.OpenDocumentSheet"),
        [".pptx"] = Plain("PPTX", "Kind.PowerPoint"),
        [".ppt"] = Plain("PPT", "Kind.PowerPoint"),
        [".odp"] = Plain("ODP", "Kind.OpenDocumentPresentation"),

        [".dxf"] = new(null, "Label.Dxf", "Kind.Cad", "DXF"),
        [".dwg"] = new(null, "Label.Dwg", "Kind.Cad", "DWG"),
    };

    /// <summary>Every extension with a display name, in catalog order.</summary>
    public static IReadOnlyCollection<string> KnownExtensions => ByExtension.Keys;

    /// <param name="culture">The language to write in; the app's language when null.</param>
    public static string GetTargetLabel(string extension, CultureInfo? culture = null) =>
        ByExtension.TryGetValue(FileExtension.Normalize(extension), out var names)
            ? names.Label ?? Loc.Get(names.LabelKey!, culture)
            : FileExtension.ToCode(extension);

    /// <param name="culture">The language to write in; the app's language when null.</param>
    public static string GetKindName(string extension, CultureInfo? culture = null) =>
        ByExtension.TryGetValue(FileExtension.Normalize(extension), out var names)
            ? Loc.Format(culture, names.KindKey, names.KindArgument ?? string.Empty)
            : Loc.Format(culture, "Kind.Unknown", FileExtension.ToCode(extension));
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Formats;

namespace Condec.Core.Batch;

/// <summary>The kinds of file that may be chosen together (DESIGN §6.1, owner decision 2026-10-03).</summary>
public enum SourceKind
{
    Other,
    Image,
    Pdf,
    Document,
    Spreadsheet,
    Presentation,
    Audio,
    Video,
    Cad,
}

/// <summary>
/// Several files are converted together only when they are the same kind, so one format list and one set of options
/// fit all of them: photos in PNG, JPG and HEIC together, but not a photo and a PDF.
/// </summary>
public static class SourceKinds
{
    private static readonly Dictionary<string, SourceKind> ByExtension = new(StringComparer.Ordinal)
    {
        [".jpg"] = SourceKind.Image,
        [".jpeg"] = SourceKind.Image,
        [".png"] = SourceKind.Image,
        [".bmp"] = SourceKind.Image,
        [".gif"] = SourceKind.Image,
        [".tif"] = SourceKind.Image,
        [".tiff"] = SourceKind.Image,
        [".heic"] = SourceKind.Image,
        [".heif"] = SourceKind.Image,
        [".webp"] = SourceKind.Image,

        [".pdf"] = SourceKind.Pdf,

        [".docx"] = SourceKind.Document,
        [".doc"] = SourceKind.Document,
        [".odt"] = SourceKind.Document,
        [".rtf"] = SourceKind.Document,
        [".txt"] = SourceKind.Document,
        [".html"] = SourceKind.Document,
        [".htm"] = SourceKind.Document,
        [".md"] = SourceKind.Document,

        [".xlsx"] = SourceKind.Spreadsheet,
        [".xls"] = SourceKind.Spreadsheet,
        [".ods"] = SourceKind.Spreadsheet,

        [".pptx"] = SourceKind.Presentation,
        [".ppt"] = SourceKind.Presentation,
        [".odp"] = SourceKind.Presentation,

        [".mp3"] = SourceKind.Audio,
        [".m4a"] = SourceKind.Audio,
        [".wav"] = SourceKind.Audio,
        [".wma"] = SourceKind.Audio,
        [".flac"] = SourceKind.Audio,

        [".mp4"] = SourceKind.Video,
        [".m4v"] = SourceKind.Video,
        [".mov"] = SourceKind.Video,
        [".avi"] = SourceKind.Video,
        [".wmv"] = SourceKind.Video,

        [".dxf"] = SourceKind.Cad,
        [".dwg"] = SourceKind.Cad,
    };

    /// <summary>The kind of a file by its extension; <see cref="SourceKind.Other"/> for one with no extension or an unknown one.</summary>
    public static SourceKind Of(string path)
    {
        var extension = FileExtension.FromPath(path);
        return extension.Length > 0 && ByExtension.TryGetValue(extension, out var kind) ? kind : SourceKind.Other;
    }

    /// <summary>
    /// The targets the batch can go to, in the order they first appear in the sources' lists. <paramref name="targetsOf"/>
    /// gives one source extension's targets (from the registry). A target is offered when every source either can go to
    /// it or already is that format (PNG and JPG pictures together can all become JPG: the JPG is left as it is), and at
    /// least one source really goes to it.
    /// </summary>
    public static IReadOnlyList<T> CommonTargets<T>(IEnumerable<string> sourceExtensions, Func<string, IReadOnlyList<T>> targetsOf, Func<T, string> extensionOf)
    {
        var sources = sourceExtensions.Distinct(StringComparer.Ordinal).ToList();
        var lists = sources.Select(s => (Source: s, Targets: targetsOf(s))).ToList();
        var candidates = new List<T>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, targets) in lists)
        {
            candidates.AddRange(targets.Where(t => seen.Add(extensionOf(t))));
        }

        return [.. candidates.Where(t => lists.All(l =>
            l.Targets.Any(o => extensionOf(o) == extensionOf(t)) || SameFormat(l.Source, extensionOf(t))))];
    }

    /// <summary>Whether two extensions name one format: ".jpeg" is ".jpg", ".tiff" is ".tif", ".htm" is ".html".</summary>
    public static bool SameFormat(string first, string second) => Canonical(first) == Canonical(second);

    private static string Canonical(string extension) => FileExtension.Normalize(extension) switch
    {
        ".jpeg" => ".jpg",
        ".tiff" => ".tif",
        ".htm" => ".html",
        var other => other,
    };
}

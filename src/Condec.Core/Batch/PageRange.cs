// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;

namespace Condec.Core.Batch;

/// <summary>
/// Which pages of a PDF (or multi-page TIFF) to convert: every page, or a list typed the way print dialogs take it,
/// "1-3, 5, 8-". The same range is applied to every file of a batch, so a page past the end of a shorter file is
/// left out for that file instead of being an error.
/// </summary>
public sealed class PageRange
{
    private readonly IReadOnlyList<(int First, int? Last)> _parts;

    private PageRange(IReadOnlyList<(int First, int? Last)> parts) => _parts = parts;

    /// <summary>Every page.</summary>
    public static PageRange All { get; } = new([(1, null)]);

    /// <summary>The first page only.</summary>
    public static PageRange First { get; } = new([(1, 1)]);

    public bool IsAll => _parts is [(1, null)];

    /// <summary>
    /// Reads "1-3, 5, 8-". Parts are separated by commas or semicolons; "a-b" is a span, "a-" runs to the last page.
    /// Spaces are ignored. Pages start at 1. False for an empty text, a 0, a reversed span ("5-3"), or anything else.
    /// </summary>
    public static bool TryParse(string? text, out PageRange range)
    {
        range = All;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = new List<(int First, int? Last)>();
        foreach (var raw in text.Split([',', ';']))
        {
            var part = raw.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (part.Length == 0)
            {
                // "1,,3" and a trailing comma are forgiven; an empty text was refused above.
                continue;
            }

            var dash = part.IndexOf('-', StringComparison.Ordinal);
            if (dash < 0)
            {
                if (!TryPage(part, out var page))
                {
                    return false;
                }

                parts.Add((page, page));
                continue;
            }

            if (!TryPage(part[..dash], out var first))
            {
                return false;
            }

            var rest = part[(dash + 1)..];
            if (rest.Length == 0)
            {
                parts.Add((first, null));
                continue;
            }

            if (!TryPage(rest, out var last) || last < first)
            {
                return false;
            }

            parts.Add((first, last));
        }

        if (parts.Count == 0)
        {
            return false;
        }

        range = new PageRange(parts);
        return true;
    }

    /// <summary>The pages of a file with <paramref name="pageCount"/> pages, in order, each once. Empty when none exist in that file.</summary>
    public IReadOnlyList<int> PagesIn(int pageCount)
    {
        var pages = new SortedSet<int>();
        foreach (var (first, last) in _parts)
        {
            var end = Math.Min(last ?? pageCount, pageCount);
            for (var page = first; page <= end; page++)
            {
                pages.Add(page);
            }
        }

        return [.. pages];
    }

    private static bool TryPage(string text, out int page) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Formats;

namespace Condec.Core.Batch;

/// <summary>One file a batch will write.</summary>
/// <param name="SourcePath">The file it comes from.</param>
/// <param name="Page">The page it holds, or null when the whole file (or a combined drawing of several pages) becomes one result.</param>
/// <param name="PageCount">Pages in the source, to pad the page number so results sort in order ("-01" … "-12").</param>
/// <param name="TargetExtension">The result's format.</param>
public sealed record OutputRequest(string SourcePath, int? Page, int PageCount, string TargetExtension);

/// <summary>
/// The names of a batch's results in one folder (DESIGN §6.1, owner decision 2026-10-03): "&lt;source&gt;.&lt;ext&gt;", one page
/// "&lt;source&gt;-&lt;n&gt;.&lt;ext&gt;". A name that is taken, by a file already in the folder, a source file, or an earlier result of
/// the same batch, gets " (2)", " (3)" and so on. Nothing is ever overwritten, and result n always belongs to request n.
/// </summary>
public static class OutputNames
{
    /// <summary>The highest " (n)" tried before giving up; a folder with that many copies of one name is not a real case.</summary>
    public const int MaximumCopies = 9999;

    /// <param name="sameFormatSuffix">Added to the name when a file is converted to its own format (" (hasil)"), as the save dialog does.</param>
    /// <param name="exists">Whether a path is taken on disk; File.Exists or Directory.Exists in the app, a set in tests.</param>
    public static IReadOnlyList<string> Plan(IReadOnlyList<OutputRequest> requests, string folder, string sameFormatSuffix, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(requests);
        return PlanNamed([.. requests.Select(r => (r.SourcePath, BaseName(r, sameFormatSuffix)))], folder, exists);
    }

    /// <summary>
    /// The same rules as <see cref="Plan"/> for results whose names are made elsewhere ("foto-2560x1440.png" in Upscale Image):
    /// a name taken by a file in the folder, a source file, or an earlier result gets " (2)", " (3)" and so on.
    /// </summary>
    /// <param name="results">Result n: the file it comes from and the name it should have.</param>
    public static IReadOnlyList<string> PlanNamed(IReadOnlyList<(string SourcePath, string FileName)> results, string folder, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(results);
        var taken = new HashSet<string>(results.Select(r => Path.GetFullPath(r.SourcePath)), StringComparer.OrdinalIgnoreCase);
        var names = new List<string>(results.Count);
        foreach (var (_, fileName) in results)
        {
            var path = Free(Path.Combine(folder, fileName), p => taken.Contains(p) || exists(p));
            taken.Add(path);
            names.Add(path);
        }

        return names;
    }

    /// <summary>
    /// <paramref name="path"/> itself when it is free, otherwise the first free "name (n).ext". Used again right before each
    /// file is written, in case something appeared in the folder while the batch was running.
    /// </summary>
    public static string Free(string path, Func<string, bool> taken)
    {
        var full = Path.GetFullPath(path);
        if (!taken(full))
        {
            return full;
        }

        var folder = Path.GetDirectoryName(full) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(full);
        var extension = Path.GetExtension(full);
        for (var copy = 2; copy <= MaximumCopies; copy++)
        {
            var candidate = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{stem} ({copy}){extension}"));
            if (!taken(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"No free name for '{stem}{extension}' in '{folder}'.");
    }

    /// <summary>The name before any " (n)": source name, page number, same-format suffix, target extension.</summary>
    public static string BaseName(OutputRequest request, string sameFormatSuffix)
    {
        var target = FileExtension.Normalize(request.TargetExtension);
        var stem = Path.GetFileNameWithoutExtension(request.SourcePath);
        if (request.Page is { } page)
        {
            var width = Math.Max(1, request.PageCount).ToString(CultureInfo.InvariantCulture).Length;
            stem += "-" + page.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
        }

        if (FileExtension.FromPath(request.SourcePath) == target)
        {
            stem += sameFormatSuffix;
        }

        return stem + target;
    }
}

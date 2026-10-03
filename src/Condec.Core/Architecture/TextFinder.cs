// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Architecture;

/// <summary>Reads one line of writing from a picture of it. Implemented with Windows.Media.Ocr on Windows.</summary>
public interface ITextRecognizer
{
    /// <summary>What the picture says, or null when nothing could be read.</summary>
    Task<string?> RecognizeAsync(RasterPicture line, CancellationToken ct);
}

/// <summary>
/// Finds lines of writing without reading them: rows of small separate marks of about one height, which is what letters
/// and digits are to a machine. What the line says is a separate step (<see cref="ITextRecognizer"/>).
/// </summary>
public static class TextFinder
{
    /// <summary>A mark taller than this share of the shorter side of the picture is a drawing, not a letter.</summary>
    public const double MaximumHeightShare = 0.06;

    /// <summary>A mark shorter than this, in pixels, is a speck.</summary>
    public const int MinimumHeight = 5;

    /// <summary>A line of writing has at least this many marks.</summary>
    public const int MinimumMarks = 3;

    /// <summary>Marks of one line are no further apart than this many times the taller one's height.</summary>
    public const double MaximumGapRatio = 1.0;

    /// <summary>The tallest mark of a line is no more than this many times the shortest.</summary>
    public const double MaximumHeightSpread = 3.5;

    /// <summary>A found line: where it is and which blobs make it.</summary>
    public sealed record Line(PixelRect Bounds, IReadOnlyList<Blob> Marks);

    public static List<Line> Find(IReadOnlyList<Blob> blobs, int pictureWidth, int pictureHeight, Func<Blob, bool> include, CancellationToken ct)
    {
        var maximumHeight = Math.Max(MinimumHeight + 1, (int)(Math.Min(pictureWidth, pictureHeight) * MaximumHeightShare));

        var marks = blobs
            .Where(include)
            .Where(b => b.Height >= MinimumHeight && b.Height <= maximumHeight && b.Width <= 3 * maximumHeight && IsMarkShaped(b))
            .OrderBy(b => b.MinX)
            .ToList();

        // Union-find over marks that sit side by side.
        var parent = Enumerable.Range(0, marks.Count).ToArray();
        int Root(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        for (var i = 0; i < marks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = marks[i];
            for (var j = i + 1; j < marks.Count; j++)
            {
                var b = marks[j];
                // Sorted by left edge, so the gap only grows as j does: past the widest gap any pair may have, stop.
                var gap = b.MinX - a.MaxX;
                if (gap > MaximumGapRatio * maximumHeight)
                {
                    break;
                }

                if (gap > MaximumGapRatio * Math.Max(a.Height, b.Height))
                {
                    continue;
                }

                var overlap = Math.Min(a.MaxY, b.MaxY) - Math.Max(a.MinY, b.MinY) + 1;
                if (overlap >= 0.5 * Math.Min(a.Height, b.Height))
                {
                    parent[Root(j)] = Root(i);
                }
            }
        }

        var lines = new List<Line>();
        foreach (var group in Enumerable.Range(0, marks.Count).GroupBy(Root))
        {
            var members = group.Select(i => marks[i]).ToList();
            if (members.Count < MinimumMarks)
            {
                continue;
            }

            var tallest = members.Max(m => m.Height);
            var shortest = members.Min(m => m.Height);
            if (tallest > MaximumHeightSpread * shortest)
            {
                continue;
            }

            var bounds = members.Select(m => m.Bounds).Aggregate((x, y) => x.Union(y));

            // Writing runs along its line: a stack of marks is not a line of text.
            if (bounds.Width < bounds.Height)
            {
                continue;
            }

            lines.Add(new Line(bounds, members));
        }

        return lines;
    }

    /// <summary>A letter stands up or is about square; a mark far wider than tall is a dash of the drawing, not writing.</summary>
    private static bool IsMarkShaped(Blob blob) => blob.Width <= 4 * blob.Height;
}

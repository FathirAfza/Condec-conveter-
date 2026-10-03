// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;

namespace Condec.Core.Architecture;

/// <summary>
/// Finds the places of a picture that are too soft to trace (DESIGN §6.3.1). A sharp line goes from paper to ink within a
/// pixel or two; a blurred one takes several, and a faint one never reaches ink. For each square of the picture, the steepest
/// edge in it is measured as the tone change across two neighboring pixels, against the whole tone range (white to black, 255):
/// a step from white to black is steepness 1, a ramp over w pixels is 2/w, and a line that only reaches grey is as soft as its
/// height. Squares below <see cref="MinimumSteepness"/> are unclear. The measure is against the whole range, not the square's
/// own: a thin line blurred wide loses height as it spreads, so against its own height it would always look sharp.
/// </summary>
public static class ClarityAnalyzer
{
    /// <summary>Side of one square, in pixels.</summary>
    public const int CellSize = 32;

    /// <summary>
    /// Steepness below which a square is unclear: an edge spread over more than about 4 pixels, or a line that reaches less than
    /// half of the tone range. A box blur of radius 1 measures 0.67 where lines cross, radius 2 0.45, radius 3 0.35, radius 4
    /// 0.28 (tests). `[ASUMSI]`: 0.5 is where text and thin lines stop being readable to the tracer; calibrate on real pictures.
    /// </summary>
    public const double MinimumSteepness = 0.5;

    /// <summary>A square whose brightest and darkest pixels differ by less than this has no line in it (paper, a flat fill).</summary>
    public const int MinimumContrast = 40;

    /// <summary>
    /// The most squares an area spans on a side. A picture that is soft all over is many areas, not one: the count is what
    /// decides whether upscaling is offered, and one box over everything would hide the picture. `[ASUMSI]`: 6 squares (192 px).
    /// </summary>
    public const int MaxAreaCells = 6;

    /// <summary>Squares that are unclear and touch, joined into the areas the page marks (each at most <see cref="MaxAreaCells"/> squares across).</summary>
    public static List<PixelRect> FindUnclearAreas(GrayImage gray) => Analyze(gray).Areas;

    /// <summary>Which squares of the picture are unclear, and the areas they make.</summary>
    /// <param name="ignore">Places that are soft by nature (a logo's shading, a photo) and not to be judged.</param>
    public static ClarityMap Analyze(GrayImage gray, IReadOnlyList<PixelRect>? ignore = null)
    {
        var columns = (gray.Width + CellSize - 1) / CellSize;
        var rows = (gray.Height + CellSize - 1) / CellSize;
        var unclear = new bool[columns * rows];

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var centerX = (column * CellSize) + (CellSize / 2);
                var centerY = (row * CellSize) + (CellSize / 2);
                unclear[(row * columns) + column] = !(ignore?.Any(r => r.Contains(centerX, centerY)) ?? false)
                    && IsUnclear(gray, column * CellSize, row * CellSize);
            }
        }

        // Touching squares (8-connected) make one area.
        var areas = new List<PixelRect>();
        var seen = new bool[unclear.Length];
        var stack = new Stack<int>();
        for (var start = 0; start < unclear.Length; start++)
        {
            if (!unclear[start] || seen[start])
            {
                continue;
            }

            // The squares of this stretch, taken block by block so no area is larger than MaxAreaCells squares across.
            var blocks = new Dictionary<(int Column, int Row), (int MinColumn, int MinRow, int MaxColumn, int MaxRow)>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                var column = at % columns;
                var row = at / columns;
                var block = (column / MaxAreaCells, row / MaxAreaCells);
                blocks[block] = blocks.TryGetValue(block, out var box)
                    ? (Math.Min(box.MinColumn, column), Math.Min(box.MinRow, row), Math.Max(box.MaxColumn, column), Math.Max(box.MaxRow, row))
                    : (column, row, column, row);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nc = column + dx;
                        var nr = row + dy;
                        if (nc < 0 || nr < 0 || nc >= columns || nr >= rows)
                        {
                            continue;
                        }

                        var neighbor = (nr * columns) + nc;
                        if (unclear[neighbor] && !seen[neighbor])
                        {
                            seen[neighbor] = true;
                            stack.Push(neighbor);
                        }
                    }
                }
            }

            foreach (var (_, box) in blocks.OrderBy(b => b.Key.Row).ThenBy(b => b.Key.Column))
            {
                areas.Add(new PixelRect(box.MinColumn * CellSize, box.MinRow * CellSize, Math.Min(gray.Width, (box.MaxColumn + 1) * CellSize), Math.Min(gray.Height, (box.MaxRow + 1) * CellSize)));
            }
        }

        return new ClarityMap(gray.Width, gray.Height, columns, rows, unclear, areas);
    }

    /// <summary>The steepness of the steepest edge in a square, 0 to 1; null when there is no line in it.</summary>
    public static double? Steepness(GrayImage gray, int left, int top)
    {
        var right = Math.Min(gray.Width, left + CellSize);
        var bottom = Math.Min(gray.Height, top + CellSize);
        if (right - left < 3 || bottom - top < 3)
        {
            return null;
        }

        var histogram = new int[256];
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                histogram[gray[x, y]]++;
            }
        }

        var total = (right - left) * (bottom - top);
        var low = Percentile(histogram, total, 0.02);
        var high = Percentile(histogram, total, 0.98);
        var contrast = high - low;
        if (contrast < MinimumContrast)
        {
            return null;
        }

        // Steepest step between neighboring pixels, found with the Sobel gradient (divided by 4, so a step is its own height).
        var steepest = 0.0;
        for (var y = Math.Max(top, 1); y < Math.Min(bottom, gray.Height - 1); y++)
        {
            for (var x = Math.Max(left, 1); x < Math.Min(right, gray.Width - 1); x++)
            {
                var gx = (gray[x + 1, y - 1] + (2 * gray[x + 1, y]) + gray[x + 1, y + 1]) - (gray[x - 1, y - 1] + (2 * gray[x - 1, y]) + gray[x - 1, y + 1]);
                var gy = (gray[x - 1, y + 1] + (2 * gray[x, y + 1]) + gray[x + 1, y + 1]) - (gray[x - 1, y - 1] + (2 * gray[x, y - 1]) + gray[x + 1, y - 1]);
                var magnitude = Math.Sqrt((gx * gx) + (gy * gy)) / 4;
                if (magnitude > steepest)
                {
                    steepest = magnitude;
                }
            }
        }

        return Math.Min(1, steepest / 255);
    }

    private static bool IsUnclear(GrayImage gray, int left, int top) => Steepness(gray, left, top) is { } steepness && steepness < MinimumSteepness;

    private static int Percentile(int[] histogram, int total, double fraction)
    {
        var target = total * fraction;
        var sum = 0;
        for (var i = 0; i < 256; i++)
        {
            sum += histogram[i];
            if (sum >= target)
            {
                return i;
            }
        }

        return 255;
    }
}

/// <summary>The squares of a picture and which of them are unclear.</summary>
/// <param name="Areas">Touching unclear squares joined; each is a rectangle.</param>
public sealed record ClarityMap(int Width, int Height, int Columns, int Rows, bool[] Unclear, List<PixelRect> Areas)
{
    /// <summary>The share of the squares <paramref name="rect"/> touches that are unclear, 0 to 1.</summary>
    public double UnclearShare(PixelRect rect)
    {
        var area = rect.Clamp(Width, Height);
        if (area.Width <= 0 || area.Height <= 0)
        {
            return 0;
        }

        var firstColumn = area.Left / ClarityAnalyzer.CellSize;
        var lastColumn = (area.Right - 1) / ClarityAnalyzer.CellSize;
        var firstRow = area.Top / ClarityAnalyzer.CellSize;
        var lastRow = (area.Bottom - 1) / ClarityAnalyzer.CellSize;
        int total = 0, unclear = 0;
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                total++;
                if (Unclear[(row * Columns) + column])
                {
                    unclear++;
                }
            }
        }

        return total == 0 ? 0 : (double)unclear / total;
    }
}

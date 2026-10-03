// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Architecture;

/// <summary>
/// The centerlines of thin strokes. A drawing's lines are strokes a few pixels wide; CAD wants one line per stroke, not the
/// two edges of it. Thinning (Zhang and Suen 1984) wears a stroke down to one pixel along its middle, and the pixels are
/// then followed from end to end.
/// </summary>
public static class Skeleton
{
    /// <summary>
    /// Thins <paramref name="pixels"/> (a width × height picture, true is ink) to a one pixel wide skeleton, in place.
    /// The picture must have an empty border row and column all around.
    /// </summary>
    public static void Thin(bool[] pixels, int width, int height, CancellationToken ct = default)
    {
        var marked = new List<int>();
        bool changed;
        do
        {
            changed = false;
            for (var pass = 0; pass < 2; pass++)
            {
                ct.ThrowIfCancellationRequested();
                marked.Clear();
                for (var y = 1; y < height - 1; y++)
                {
                    var row = y * width;
                    for (var x = 1; x < width - 1; x++)
                    {
                        var at = row + x;
                        if (!pixels[at])
                        {
                            continue;
                        }

                        // Neighbors clockwise from north.
                        var p2 = pixels[at - width];
                        var p3 = pixels[at - width + 1];
                        var p4 = pixels[at + 1];
                        var p5 = pixels[at + width + 1];
                        var p6 = pixels[at + width];
                        var p7 = pixels[at + width - 1];
                        var p8 = pixels[at - 1];
                        var p9 = pixels[at - width - 1];

                        var count = (p2 ? 1 : 0) + (p3 ? 1 : 0) + (p4 ? 1 : 0) + (p5 ? 1 : 0) + (p6 ? 1 : 0) + (p7 ? 1 : 0) + (p8 ? 1 : 0) + (p9 ? 1 : 0);
                        if (count is < 2 or > 6)
                        {
                            continue;
                        }

                        var transitions = (!p2 && p3 ? 1 : 0) + (!p3 && p4 ? 1 : 0) + (!p4 && p5 ? 1 : 0) + (!p5 && p6 ? 1 : 0)
                            + (!p6 && p7 ? 1 : 0) + (!p7 && p8 ? 1 : 0) + (!p8 && p9 ? 1 : 0) + (!p9 && p2 ? 1 : 0);
                        if (transitions != 1)
                        {
                            continue;
                        }

                        var remove = pass == 0
                            ? !(p2 && p4 && p6) && !(p4 && p6 && p8)
                            : !(p2 && p4 && p8) && !(p2 && p6 && p8);
                        if (remove)
                        {
                            marked.Add(at);
                        }
                    }
                }

                foreach (var at in marked)
                {
                    pixels[at] = false;
                }

                changed |= marked.Count > 0;
            }
        }
        while (changed);
    }

    /// <summary>
    /// The skeleton followed from end to end: every path runs from an end or a junction to the next, as pixel positions in
    /// the same picture. Short stubs the thinning leaves at corners and junctions (shorter than <paramref name="spurLength"/>)
    /// are cut first. A skeleton without ends or junctions (a ring) is one closed path.
    /// </summary>
    public static List<SkeletonPath> Trace(bool[] skeleton, int width, int height, int spurLength, CancellationToken ct = default)
    {
        var pixels = (bool[])skeleton.Clone();
        for (var round = 0; round < 2 && spurLength > 0; round++)
        {
            if (!PruneSpurs(pixels, width, height, spurLength))
            {
                break;
            }
        }

        ct.ThrowIfCancellationRequested();
        return Follow(pixels, width, height);
    }

    private static readonly (int Dx, int Dy)[] Directions = [(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)];

    /// <summary>The neighbors that carry the line on. A diagonal neighbor beside two straight ones is skipped: it is only the corner of a staircase.</summary>
    private static void Neighbors(bool[] pixels, int width, int height, int x, int y, List<int> into)
    {
        into.Clear();
        bool At(int px, int py) => (uint)px < (uint)width && (uint)py < (uint)height && pixels[(py * width) + px];

        for (var d = 0; d < Directions.Length; d++)
        {
            var (dx, dy) = Directions[d];
            if (!At(x + dx, y + dy))
            {
                continue;
            }

            if (dx != 0 && dy != 0 && (At(x + dx, y) || At(x, y + dy)))
            {
                continue;
            }

            into.Add(((y + dy) * width) + x + dx);
        }
    }

    private static bool PruneSpurs(bool[] pixels, int width, int height, int spurLength)
    {
        var neighbors = new List<int>();
        var degree = new int[pixels.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[(y * width) + x])
                {
                    Neighbors(pixels, width, height, x, y, neighbors);
                    degree[(y * width) + x] = neighbors.Count;
                }
            }
        }

        var removed = false;
        var path = new List<int>();
        var next = new List<int>();
        for (var start = 0; start < pixels.Length; start++)
        {
            if (!pixels[start] || degree[start] != 1)
            {
                continue;
            }

            // Walk from the end to the first junction; if it is close, the walk was a stub.
            path.Clear();
            path.Add(start);
            var previous = -1;
            var current = start;
            var reachedJunction = false;
            while (true)
            {
                Neighbors(pixels, width, height, current % width, current / width, next);
                var forward = -1;
                var count = 0;
                foreach (var n in next)
                {
                    if (n != previous)
                    {
                        forward = n;
                        count++;
                    }
                }

                if (count != 1)
                {
                    break;
                }

                if (degree[forward] > 2)
                {
                    reachedJunction = true;
                    break;
                }

                if (path.Count >= spurLength)
                {
                    break;
                }

                previous = current;
                current = forward;
                path.Add(current);
            }

            if (reachedJunction && path.Count < spurLength)
            {
                foreach (var at in path)
                {
                    pixels[at] = false;
                }

                removed = true;
            }
        }

        return removed;
    }

    private static List<SkeletonPath> Follow(bool[] pixels, int width, int height)
    {
        var neighbors = new List<int>();
        var adjacency = new Dictionary<int, int[]>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = (y * width) + x;
                if (pixels[at])
                {
                    Neighbors(pixels, width, height, x, y, neighbors);
                    adjacency[at] = [.. neighbors];
                }
            }
        }

        var paths = new List<SkeletonPath>();
        var visited = new HashSet<int>();
        var usedEdges = new HashSet<long>();

        static long Edge(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        // Paths that start at an end or a junction.
        foreach (var (node, around) in adjacency)
        {
            if (around.Length == 2)
            {
                continue;
            }

            foreach (var first in around)
            {
                if (usedEdges.Contains(Edge(node, first)))
                {
                    continue;
                }

                var points = new List<int> { node };
                var previous = node;
                var current = first;
                usedEdges.Add(Edge(node, first));
                while (true)
                {
                    points.Add(current);
                    var here = adjacency[current];
                    if (here.Length != 2)
                    {
                        break;
                    }

                    visited.Add(current);
                    var forward = here[0] == previous ? here[1] : here[0];
                    usedEdges.Add(Edge(current, forward));
                    previous = current;
                    current = forward;
                }

                paths.Add(new SkeletonPath(points.ConvertAll(p => (p % width, p / width)), false));
            }
        }

        // What is left are rings.
        foreach (var (node, around) in adjacency)
        {
            if (around.Length != 2 || visited.Contains(node))
            {
                continue;
            }

            var points = new List<int>();
            var previous = -1;
            var current = node;
            do
            {
                points.Add(current);
                visited.Add(current);
                var here = adjacency[current];
                var forward = here[0] == previous ? here[1] : here[0];
                if (previous == -1)
                {
                    forward = here[0];
                }

                previous = current;
                current = forward;
            }
            while (current != node && !visited.Contains(current));

            paths.Add(new SkeletonPath(points.ConvertAll(p => (p % width, p / width)), true));
        }

        return paths;
    }
}

/// <summary>One stroke followed from end to end, as pixel positions; a ring comes back to its first pixel.</summary>
public sealed record SkeletonPath(List<(int X, int Y)> Pixels, bool IsRing);

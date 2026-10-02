// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Cad;

/// <summary>An 8-bit grayscale raster, rows top to bottom. 0 is black.</summary>
public sealed record GrayImage(int Width, int Height, byte[] Pixels)
{
    public byte this[int x, int y] => Pixels[(y * Width) + x];
}

/// <summary>Renders one PDF page to grayscale. Implemented with Windows.Data.Pdf on Windows.</summary>
public interface IPdfPageRasterizer
{
    /// <param name="pageNumber">1-based.</param>
    /// <param name="pixelsPerPoint">Resolution: 2.0 is 144 dpi.</param>
    Task<GrayImage> RenderAsync(string path, int pageNumber, double pixelsPerPoint, CancellationToken ct);
}

/// <summary>
/// Traces the outlines of the dark shapes in a scanned page: Otsu binarization, marching squares, then
/// Ramer–Douglas–Peucker simplification. The result approximates the ink; it is not a measured drawing.
/// </summary>
public static class ScanVectorizer
{
    /// <summary>Outlines whose bounding box is smaller than this many pixels on both sides are specks of noise.</summary>
    internal const double MinFeaturePixels = 3;

    /// <summary>
    /// Closed outlines in pixel coordinates (origin top left, y down). Each outline lists its corners once;
    /// the last point connects back to the first.
    /// </summary>
    public static List<List<(double X, double Y)>> Trace(GrayImage image, double tolerancePixels, CancellationToken ct)
    {
        var threshold = OtsuThreshold(image.Pixels);
        var outlines = new List<List<(double X, double Y)>>();
        foreach (var contour in TraceContours(image, threshold, ct))
        {
            var minX = contour.Min(p => p.X);
            var maxX = contour.Max(p => p.X);
            var minY = contour.Min(p => p.Y);
            var maxY = contour.Max(p => p.Y);
            if (maxX - minX < MinFeaturePixels && maxY - minY < MinFeaturePixels)
            {
                continue;
            }

            var simplified = SimplifyClosed(contour, tolerancePixels);
            if (simplified.Count >= 3)
            {
                outlines.Add(simplified);
            }
        }

        return outlines;
    }

    /// <summary>The gray level that best separates ink from paper (Otsu 1979): pixels below it are ink.</summary>
    public static byte OtsuThreshold(byte[] pixels)
    {
        var histogram = new long[256];
        foreach (var value in pixels)
        {
            histogram[value]++;
        }

        double total = pixels.Length;
        double sumAll = 0;
        for (var i = 0; i < 256; i++)
        {
            sumAll += i * histogram[i];
        }

        double sumBackground = 0;
        long weightBackground = 0;
        double bestVariance = -1;
        var best = 0;
        for (var t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0)
            {
                continue;
            }

            var weightForeground = total - weightBackground;
            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += t * histogram[t];
            var meanBackground = sumBackground / weightBackground;
            var meanForeground = (sumAll - sumBackground) / weightForeground;
            var variance = weightBackground * weightForeground * (meanBackground - meanForeground) * (meanBackground - meanForeground);
            if (variance > bestVariance)
            {
                bestVariance = variance;
                best = t;
            }
        }

        // Pixels at or below the best split are ink.
        return (byte)Math.Min(255, best + 1);
    }

    /// <summary>
    /// Marching squares over the ink mask, with everything outside the image counted as paper so every
    /// contour closes. Edge crossings sit on cell edge midpoints; saddle cells keep diagonal ink apart.
    /// </summary>
    internal static List<List<(double X, double Y)>> TraceContours(GrayImage image, byte threshold, CancellationToken ct)
    {
        bool Ink(int x, int y) => x >= 0 && y >= 0 && x < image.Width && y < image.Height && image[x, y] < threshold;

        // Points are stored doubled (so edge midpoints are integers) and packed into one long.
        static long Key(int x2, int y2) => ((long)x2 << 32) | (uint)y2;

        var neighbours = new Dictionary<long, (long A, long B)>(PointKeyComparer.Instance);
        void Link(long from, long to)
        {
            neighbours[from] = neighbours.TryGetValue(from, out var n) ? (n.A, to) : (to, long.MinValue);
            neighbours[to] = neighbours.TryGetValue(to, out var m) ? (m.A, from) : (from, long.MinValue);
        }

        for (var y = -1; y < image.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = -1; x < image.Width; x++)
            {
                var cell = (Ink(x, y) ? 8 : 0) | (Ink(x + 1, y) ? 4 : 0) | (Ink(x + 1, y + 1) ? 2 : 0) | (Ink(x, y + 1) ? 1 : 0);
                if (cell is 0 or 15)
                {
                    continue;
                }

                var top = Key((2 * x) + 1, 2 * y);
                var right = Key((2 * x) + 2, (2 * y) + 1);
                var bottom = Key((2 * x) + 1, (2 * y) + 2);
                var left = Key(2 * x, (2 * y) + 1);

                switch (cell)
                {
                    case 1 or 14: Link(left, bottom); break;
                    case 2 or 13: Link(bottom, right); break;
                    case 3 or 12: Link(left, right); break;
                    case 4 or 11: Link(top, right); break;
                    case 6 or 9: Link(top, bottom); break;
                    case 7 or 8: Link(left, top); break;
                    case 5: Link(left, top); Link(bottom, right); break;
                    case 10: Link(top, right); Link(left, bottom); break;
                }
            }
        }

        var contours = new List<List<(double X, double Y)>>();
        var visited = new HashSet<long>(PointKeyComparer.Instance);
        foreach (var start in neighbours.Keys)
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var contour = new List<(double X, double Y)> { Unpack(start) };
            var previous = start;
            var current = neighbours[start].A;
            while (current != start && current != long.MinValue && visited.Add(current))
            {
                contour.Add(Unpack(current));
                var (a, b) = neighbours[current];
                (previous, current) = (current, a == previous ? b : a);
            }

            contours.Add(contour);
        }

        return contours;

        static (double X, double Y) Unpack(long key) => ((key >> 32) / 2.0, (int)(uint)key / 2.0);
    }

    /// <summary>
    /// Hashes a packed point so nearby points spread out. The default hash of a long is its two halves XORed,
    /// which puts every point with the same x2 ^ y2 in one bucket; on a large noisy picture that made tracing
    /// quadratic (a 1200 × 800 speckled picture took 37 seconds).
    /// </summary>
    private sealed class PointKeyComparer : IEqualityComparer<long>
    {
        public static readonly PointKeyComparer Instance = new();

        public bool Equals(long x, long y) => x == y;

        public int GetHashCode(long key) => (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32);
    }

    /// <summary>Ramer–Douglas–Peucker for a closed outline: split at the point farthest from the first, simplify both halves.</summary>
    internal static List<(double X, double Y)> SimplifyClosed(List<(double X, double Y)> ring, double tolerance)
    {
        if (ring.Count < 4)
        {
            return ring;
        }

        var far = 0;
        var farDistance = -1.0;
        for (var i = 1; i < ring.Count; i++)
        {
            var d = Math.Pow(ring[i].X - ring[0].X, 2) + Math.Pow(ring[i].Y - ring[0].Y, 2);
            if (d > farDistance)
            {
                farDistance = d;
                far = i;
            }
        }

        var keep = new bool[ring.Count + 1];
        var closed = new List<(double X, double Y)>(ring) { ring[0] };
        keep[0] = keep[far] = keep[ring.Count] = true;
        MarkKept(closed, 0, far, tolerance, keep);
        MarkKept(closed, far, ring.Count, tolerance, keep);

        var result = new List<(double X, double Y)>();
        for (var i = 0; i < ring.Count; i++)
        {
            if (keep[i])
            {
                result.Add(ring[i]);
            }
        }

        return result;
    }

    private static void MarkKept(List<(double X, double Y)> points, int first, int last, double tolerance, bool[] keep)
    {
        // Iterative, so a long outline can't overflow the stack.
        var stack = new Stack<(int First, int Last)>();
        stack.Push((first, last));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            var index = -1;
            var max = tolerance;
            for (var i = a + 1; i < b; i++)
            {
                var d = DistanceToSegment(points[i], points[a], points[b]);
                if (d > max)
                {
                    max = d;
                    index = i;
                }
            }

            if (index >= 0)
            {
                keep[index] = true;
                stack.Push((a, index));
                stack.Push((index, b));
            }
        }
    }

    private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / lengthSquared, 0, 1);
        var x = a.X + (t * dx) - p.X;
        var y = a.Y + (t * dy) - p.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}

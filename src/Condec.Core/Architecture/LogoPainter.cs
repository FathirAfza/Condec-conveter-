// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;

namespace Condec.Core.Architecture;

/// <summary>
/// Turns a logo into areas of its own colors, filled (owner decision 2026-10-04: outlines traced around a logo break it into
/// loose pieces and lose its colors). The logo is read from the file at full resolution, its colors are reduced to a few, and
/// the area of each color is traced. Areas of neighboring colors overlap by one pixel, so no gap opens between them; paper
/// inside the logo stays open.
/// </summary>
public static class LogoPainter
{
    /// <summary>Colors one logo is reduced to, at most.</summary>
    public const int MaximumColors = 8;

    /// <summary>Colors closer than this (distance in RGB, 0 to 255 per channel) are one color.</summary>
    public const double SameColorDistance = 48;

    /// <summary>A color covering less than this share of the logo's ink is folded into the nearest other color.</summary>
    public const double MinimumColorShare = 0.005;

    /// <summary>The logo is read at no more than this many pixels; a larger one is averaged down first.</summary>
    public const int MaximumPixels = 4_000_000;

    /// <summary>How far, in pixels of the resolution read, the outline of an area may stray from it.</summary>
    public const double Tolerance = 1.0;

    private const int Iterations = 8;

    /// <param name="file">The picture as the file has it.</param>
    /// <param name="reduction">How many file pixels one analysis pixel stands for.</param>
    /// <param name="threshold">Gray level below which a pixel is ink, as the analysis found it.</param>
    /// <param name="rect">The logo, in analysis pixels.</param>
    /// <param name="pictureHeight">The analysis picture's height, to count y up.</param>
    /// <returns>One fill per color, the largest area first, in analysis pixels with y up.</returns>
    public static List<DrawingPrimitive> Paint(RasterPicture file, int reduction, byte threshold, PixelRect rect, int pictureHeight, CancellationToken ct)
    {
        var area = new PixelRect(rect.Left * reduction, rect.Top * reduction, rect.Right * reduction, rect.Bottom * reduction).Clamp(file.Width, file.Height);
        if (area.Width <= 0 || area.Height <= 0)
        {
            return [];
        }

        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)area.Area / MaximumPixels)));
        var width = (area.Width + step - 1) / step;
        var height = (area.Height + step - 1) / step;
        var pixels = Read(file, area, step, width, height, threshold);
        var ink = pixels.Where(p => p >= 0).ToArray();
        if (ink.Length == 0)
        {
            return [];
        }

        var centers = Seeds(ink);
        var labels = new int[pixels.Length];
        for (var round = 0; round < Iterations; round++)
        {
            ct.ThrowIfCancellationRequested();
            if (!Assign(pixels, centers, labels) && round > 0)
            {
                break;
            }

            centers = Means(pixels, labels, centers);
        }

        centers = Merge(pixels, labels, centers, ink.Length);
        Assign(pixels, centers, labels);
        labels = Smooth(labels, width, height, centers.Count);

        var counts = new int[centers.Count];
        foreach (var label in labels)
        {
            if (label >= 0)
            {
                counts[label]++;
            }
        }

        // Largest first, so a smaller area drawn later covers the pixel its larger neighbor reaches into.
        var order = Enumerable.Range(0, centers.Count).Where(c => counts[c] > 0).OrderByDescending(c => counts[c]).ThenBy(c => c).ToList();
        var rank = new int[centers.Count];
        for (var i = 0; i < order.Count; i++)
        {
            rank[order[i]] = i;
        }

        var fills = new List<DrawingPrimitive>();
        foreach (var color in order)
        {
            ct.ThrowIfCancellationRequested();
            var gray = new byte[labels.Length];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var at = (y * width) + x;
                    var label = labels[at];
                    var set = label == color || (label >= 0 && rank[label] > rank[color] && Touches(labels, width, height, x, y, color));
                    gray[at] = set ? (byte)0 : (byte)255;
                }
            }

            var loops = ScanVectorizer.Trace(new GrayImage(width, height, gray), Tolerance, ct)
                .Select(outline => (IReadOnlyList<(double X, double Y)>)outline
                    .Select(p => ((area.Left + ((p.X + 0.5) * step)) / reduction, pictureHeight - ((area.Top + ((p.Y + 0.5) * step)) / reduction)))
                    .ToList())
                .ToList();
            if (loops.Count > 0)
            {
                var (r, g, b) = centers[color];
                fills.Add(new FillPrimitive(loops, new Rgb(ToByte(r), ToByte(g), ToByte(b))));
            }
        }

        return fills;
    }

    /// <summary>
    /// The logo averaged down by <paramref name="step"/>, one packed 0xRRGGBB per pixel, or -1 where it is paper: neither ink
    /// (darker than the threshold) nor colored, the same test the analysis finds logos with.
    /// </summary>
    private static int[] Read(RasterPicture file, PixelRect area, int step, int width, int height, byte threshold)
    {
        var pixels = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int b = 0, g = 0, r = 0, count = 0;
                for (var sy = area.Top + (y * step); sy < Math.Min(area.Bottom, area.Top + ((y + 1) * step)); sy++)
                {
                    for (var sx = area.Left + (x * step); sx < Math.Min(area.Right, area.Left + ((x + 1) * step)); sx++)
                    {
                        var at = ((sy * file.Width) + sx) * 4;
                        b += file.Bgra[at];
                        g += file.Bgra[at + 1];
                        r += file.Bgra[at + 2];
                        count++;
                    }
                }

                r = (r + (count / 2)) / count;
                g = (g + (count / 2)) / count;
                b = (b + (count / 2)) / count;
                var gray = ((299 * r) + (587 * g) + (114 * b) + 500) / 1000;
                var saturated = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) >= LogoFinder.MinimumSaturation;
                pixels[(y * width) + x] = gray < threshold || saturated ? (r << 16) | (g << 8) | b : -1;
            }
        }

        return pixels;
    }

    /// <summary>Starting colors: the most common colors, coarsely binned, that are not the same color as one taken already.</summary>
    private static List<(double R, double G, double B)> Seeds(int[] ink)
    {
        var bins = new Dictionary<int, (int Count, long R, long G, long B)>();
        foreach (var color in ink)
        {
            var key = ((color >> 20) << 8) | (((color >> 12) & 0xF) << 4) | ((color >> 4) & 0xF);
            var (count, r, g, b) = bins.GetValueOrDefault(key);
            bins[key] = (count + 1, r + ((color >> 16) & 0xFF), g + ((color >> 8) & 0xFF), b + (color & 0xFF));
        }

        var seeds = new List<(double R, double G, double B)>();
        foreach (var (_, bin) in bins.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key))
        {
            var mean = ((double)bin.R / bin.Count, (double)bin.G / bin.Count, (double)bin.B / bin.Count);
            if (seeds.All(seed => Distance(seed, mean) >= SameColorDistance))
            {
                seeds.Add(mean);
                if (seeds.Count == MaximumColors)
                {
                    break;
                }
            }
        }

        return seeds;
    }

    /// <summary>Gives every ink pixel the nearest color; true when any pixel changed.</summary>
    private static bool Assign(int[] pixels, List<(double R, double G, double B)> centers, int[] labels)
    {
        var changed = false;
        for (var i = 0; i < pixels.Length; i++)
        {
            var label = pixels[i] < 0 ? -1 : Nearest(pixels[i], centers);
            changed |= labels[i] != label;
            labels[i] = label;
        }

        return changed;
    }

    /// <summary>Each color moved to the mean of its pixels; a color without pixels stays where it is.</summary>
    private static List<(double R, double G, double B)> Means(int[] pixels, int[] labels, List<(double R, double G, double B)> centers)
    {
        var sums = new (long Count, long R, long G, long B)[centers.Count];
        for (var i = 0; i < pixels.Length; i++)
        {
            if (labels[i] >= 0)
            {
                var color = pixels[i];
                var s = sums[labels[i]];
                sums[labels[i]] = (s.Count + 1, s.R + ((color >> 16) & 0xFF), s.G + ((color >> 8) & 0xFF), s.B + (color & 0xFF));
            }
        }

        return centers.Select((center, c) => sums[c].Count == 0
            ? center
            : ((double)sums[c].R / sums[c].Count, (double)sums[c].G / sums[c].Count, (double)sums[c].B / sums[c].Count)).ToList();
    }

    /// <summary>Joins colors that ended up the same color, then folds the rare ones into their nearest neighbor.</summary>
    private static List<(double R, double G, double B)> Merge(int[] pixels, int[] labels, List<(double R, double G, double B)> centers, int inkCount)
    {
        var counts = Counts(labels, centers.Count);
        while (true)
        {
            (int A, int B)? closest = null;
            var best = SameColorDistance;
            for (var a = 0; a < centers.Count; a++)
            {
                for (var b = a + 1; b < centers.Count; b++)
                {
                    var distance = Distance(centers[a], centers[b]);
                    if (distance < best)
                    {
                        best = distance;
                        closest = (a, b);
                    }
                }
            }

            if (closest is not { } pair)
            {
                break;
            }

            var total = Math.Max(1, counts[pair.A] + counts[pair.B]);
            var (ca, cb) = (centers[pair.A], centers[pair.B]);
            centers[pair.A] = (
                ((ca.R * counts[pair.A]) + (cb.R * counts[pair.B])) / total,
                ((ca.G * counts[pair.A]) + (cb.G * counts[pair.B])) / total,
                ((ca.B * counts[pair.A]) + (cb.B * counts[pair.B])) / total);
            centers.RemoveAt(pair.B);
            Assign(pixels, centers, labels);
            counts = Counts(labels, centers.Count);
        }

        // The rarest color goes first, until every color left is common enough; one always stays.
        while (centers.Count > 1)
        {
            var rarest = Enumerable.Range(0, centers.Count).MinBy(c => counts[c]);
            if (counts[rarest] >= MinimumColorShare * inkCount)
            {
                break;
            }

            centers.RemoveAt(rarest);
            Assign(pixels, centers, labels);
            counts = Counts(labels, centers.Count);
        }

        return centers;
    }

    /// <summary>
    /// Each ink pixel takes the color most of its neighbors (and itself) have, keeping its own on a tie: the thin rims of
    /// in-between colors that anti-aliasing leaves along an edge go to one side or the other.
    /// </summary>
    private static int[] Smooth(int[] labels, int width, int height, int colors)
    {
        var smoothed = (int[])labels.Clone();
        var votes = new int[colors];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var own = labels[(y * width) + x];
                if (own < 0)
                {
                    continue;
                }

                Array.Clear(votes);
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var (nx, ny) = (x + dx, y + dy);
                        if ((uint)nx < (uint)width && (uint)ny < (uint)height && labels[(ny * width) + nx] is >= 0 and var label)
                        {
                            votes[label]++;
                        }
                    }
                }

                var winner = own;
                for (var c = 0; c < colors; c++)
                {
                    if (votes[c] > votes[winner])
                    {
                        winner = c;
                    }
                }

                smoothed[(y * width) + x] = winner;
            }
        }

        return smoothed;
    }

    private static bool Touches(int[] labels, int width, int height, int x, int y, int color)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var (nx, ny) = (x + dx, y + dy);
                if ((uint)nx < (uint)width && (uint)ny < (uint)height && labels[(ny * width) + nx] == color)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int[] Counts(int[] labels, int colors)
    {
        var counts = new int[colors];
        foreach (var label in labels)
        {
            if (label >= 0)
            {
                counts[label]++;
            }
        }

        return counts;
    }

    private static int Nearest(int color, List<(double R, double G, double B)> centers)
    {
        var rgb = ((double)((color >> 16) & 0xFF), (double)((color >> 8) & 0xFF), (double)(color & 0xFF));
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var c = 0; c < centers.Count; c++)
        {
            var distance = SquaredDistance(centers[c], rgb);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = c;
            }
        }

        return best;
    }

    private static double Distance((double R, double G, double B) a, (double R, double G, double B) b) => Math.Sqrt(SquaredDistance(a, b));

    private static double SquaredDistance((double R, double G, double B) a, (double R, double G, double B) b) =>
        ((a.R - b.R) * (a.R - b.R)) + ((a.G - b.G) * (a.G - b.G)) + ((a.B - b.B) * (a.B - b.B));

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}

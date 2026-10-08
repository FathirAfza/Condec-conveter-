// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;

namespace Condec.Core.Architecture;

/// <summary>What a blob of ink became.</summary>
/// <param name="Blob">The ink it came from.</param>
/// <param name="Primitives">Lines, polylines, arcs and circles, in picture pixels with y up (<paramref name="PictureHeight"/> minus the row).</param>
public sealed record BlobLines(Blob Blob, IReadOnlyList<DrawingPrimitive> Primitives);

/// <summary>
/// Turns the ink of a drawing into CAD shapes. A stroke a few pixels wide becomes one line along its middle; ink that is
/// solid (a filled wall, a black block) keeps its outline, because it has no middle to speak of.
/// </summary>
public static class LineWorkExtractor
{
    /// <summary>A blob whose ink is wider than this on average (pixels) is solid and is outlined instead of thinned.</summary>
    public const double SolidStrokePixels = 9;

    /// <summary>Blobs smaller than this on both sides (pixels) are specks of noise.</summary>
    public const int MinimumBlobPixels = 3;

    /// <param name="ink">The ink of the whole picture.</param>
    /// <param name="labels">The blob of every pixel, from <see cref="ConnectedComponents.Label"/>.</param>
    /// <param name="include">Which blobs to turn into shapes.</param>
    /// <param name="tolerance">How far, in pixels, a shape may stray from the ink.</param>
    public static List<BlobLines> Extract(BitMask ink, int[] labels, IReadOnlyList<Blob> blobs, Func<Blob, bool> include, double tolerance, CancellationToken ct)
    {
        var result = new List<BlobLines>();
        foreach (var blob in blobs)
        {
            ct.ThrowIfCancellationRequested();
            if (!include(blob) || (blob.Width < MinimumBlobPixels && blob.Height < MinimumBlobPixels))
            {
                continue;
            }

            var primitives = ExtractBlob(ink, labels, blob, tolerance, ct);
            if (primitives.Count > 0)
            {
                result.Add(new BlobLines(blob, primitives));
            }
        }

        return result;
    }

    private static List<DrawingPrimitive> ExtractBlob(BitMask ink, int[] labels, Blob blob, double tolerance, CancellationToken ct)
    {
        // The blob alone, with an empty border all around (thinning needs one).
        var width = blob.Width + 2;
        var height = blob.Height + 2;
        var local = new bool[width * height];
        for (var y = 0; y < blob.Height; y++)
        {
            var source = ((blob.MinY + y) * ink.Width) + blob.MinX;
            for (var x = 0; x < blob.Width; x++)
            {
                local[((y + 1) * width) + x + 1] = labels[source + x] == blob.Id;
            }
        }

        var thinned = (bool[])local.Clone();
        Skeleton.Thin(thinned, width, height, ct);
        var skeletonPixels = thinned.Count(b => b);
        var meanWidth = (double)blob.Area / Math.Max(1, skeletonPixels);

        var primitives = new List<DrawingPrimitive>();
        if (meanWidth > SolidStrokePixels)
        {
            primitives.AddRange(Outline(local, width, blob, ink.Height, tolerance, ct));
            return primitives;
        }

        var spur = Math.Max(3, (int)Math.Round(1.5 * meanWidth));
        foreach (var path in Skeleton.Trace(thinned, width, height, spur, ct))
        {
            // Pixel centers, y up.
            var points = path.Pixels
                .Select(p => ((double X, double Y))(blob.MinX + p.X - 1 + 0.5, ink.Height - (blob.MinY + p.Y - 1 + 0.5)))
                .ToList();
            primitives.AddRange(CurveFitter.Fit(points, path.IsRing, tolerance));
        }

        return primitives;
    }

    private static IEnumerable<DrawingPrimitive> Outline(bool[] local, int width, Blob blob, int pictureHeight, double tolerance, CancellationToken ct)
    {
        // The blob as a picture of its own (ink is dark), traced the way a scanned page is.
        var inner = new bool[blob.Width * blob.Height];
        for (var y = 0; y < blob.Height; y++)
        {
            Array.Copy(local, ((y + 1) * width) + 1, inner, y * blob.Width, blob.Width);
        }

        return Outlines(inner, blob.Width, blob.Height, blob.MinX, blob.MinY, pictureHeight, tolerance, ct);
    }

    /// <summary>
    /// The outlines of the set pixels of <paramref name="mask"/> as closed polylines, in picture pixels with y up. The mask is a
    /// piece of the picture whose top left corner is at (<paramref name="offsetX"/>, <paramref name="offsetY"/>).
    /// </summary>
    public static IEnumerable<DrawingPrimitive> Outlines(bool[] mask, int width, int height, int offsetX, int offsetY, int pictureHeight, double tolerance, CancellationToken ct)
    {
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
        {
            gray[i] = mask[i] ? (byte)0 : (byte)255;
        }

        foreach (var outline in ScanVectorizer.Trace(new GrayImage(width, height, gray), tolerance, ct))
        {
            var points = outline
                .Select(p => ((double X, double Y))(offsetX + p.X, pictureHeight - (offsetY + p.Y)))
                .ToList();
            yield return new PolylinePrimitive(points, true);
        }
    }
}

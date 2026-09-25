// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CSMath;

namespace Condec.Core.Cad;

/// <summary>
/// Puts filled shapes back together. PDF producers triangulate solid fills (a title block cell, a frame
/// band, a bearing plate) into triangles and quads that share edges; drawn as outlines those edges turn
/// into diagonals across the drawing. Rings that share an edge are merged along it until none do.
/// </summary>
internal static class PolygonMerger
{
    public static List<List<XY>> Merge(IEnumerable<IReadOnlyList<XY>> rings, double tolerance)
    {
        var result = rings.Select(ring => Clean(ring, tolerance)).Where(ring => ring.Count >= 3).ToList();
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < result.Count && !merged; i++)
            {
                for (var j = i + 1; j < result.Count && !merged; j++)
                {
                    if (MergeAlongSharedEdge(result[i], result[j], tolerance) is { } union)
                    {
                        result[i] = Clean(union, tolerance);
                        result.RemoveAt(j);
                        merged = true;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>The union of two rings that share one edge, or null when they don't.</summary>
    private static List<XY>? MergeAlongSharedEdge(List<XY> a, List<XY> b, double tolerance)
    {
        for (var i = 0; i < a.Count; i++)
        {
            var u = a[i];
            var v = a[(i + 1) % a.Count];
            for (var j = 0; j < b.Count; j++)
            {
                var p = b[j];
                var q = b[(j + 1) % b.Count];
                var same = Near(u, p, tolerance) && Near(v, q, tolerance);
                var reversed = Near(u, q, tolerance) && Near(v, p, tolerance);
                if (!same && !reversed)
                {
                    continue;
                }

                // Walk a from v all the way round to u, then b from u the long way round back to v.
                var union = new List<XY>();
                for (var k = 0; k < a.Count; k++)
                {
                    union.Add(a[(i + 1 + k) % a.Count]);
                }

                // In b the shared edge runs j → j+1. Leaving u, take the direction that does not cross it.
                var uIndex = same ? j : (j + 1) % b.Count;
                var step = same ? -1 : 1;
                for (var k = 1; k < b.Count - 1; k++)
                {
                    union.Add(b[((uIndex + (step * k)) % b.Count + b.Count) % b.Count]);
                }

                return union;
            }
        }

        return null;
    }

    /// <summary>Drops repeated points and corners that lie on the straight line between their neighbours.</summary>
    private static List<XY> Clean(IReadOnlyList<XY> ring, double tolerance)
    {
        var points = new List<XY>();
        foreach (var p in ring)
        {
            if (points.Count == 0 || !Near(points[^1], p, tolerance))
            {
                points.Add(p);
            }
        }

        while (points.Count > 1 && Near(points[0], points[^1], tolerance))
        {
            points.RemoveAt(points.Count - 1);
        }

        var changed = true;
        while (changed && points.Count > 3)
        {
            changed = false;
            for (var i = 0; i < points.Count; i++)
            {
                var previous = points[(i - 1 + points.Count) % points.Count];
                var next = points[(i + 1) % points.Count];
                if (DistanceToSegment(points[i], previous, next) <= tolerance)
                {
                    points.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }

        return points;
    }

    private static double DistanceToSegment(XY p, XY a, XY b)
    {
        var ab = b - a;
        var length2 = (ab.X * ab.X) + (ab.Y * ab.Y);
        if (length2 <= 0)
        {
            return CadGeometry.Distance(p, a);
        }

        var t = Math.Clamp((((p.X - a.X) * ab.X) + ((p.Y - a.Y) * ab.Y)) / length2, 0, 1);
        return CadGeometry.Distance(p, new XY(a.X + (t * ab.X), a.Y + (t * ab.Y)));
    }

    private static bool Near(XY a, XY b, double tolerance) => CadGeometry.Distance(a, b) <= tolerance;
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CSMath;

namespace Condec.Core.Cad;

/// <summary>
/// Maps PdfPig page coordinates to drawing units. PdfPig already delivers every point the way a viewer
/// shows the page: the crop box origin is subtracted and the page's /Rotate is applied, so only the
/// scale is left to do.
/// </summary>
internal readonly record struct PageTransform(double UnitsPerPoint)
{
    public XY Map(double x, double y) => new(x * UnitsPerPoint, y * UnitsPerPoint);

    public Rect Map(Rect rect) => new(rect.MinX * UnitsPerPoint, rect.MinY * UnitsPerPoint, rect.MaxX * UnitsPerPoint, rect.MaxY * UnitsPerPoint);
}

/// <summary>An axis-aligned rectangle; the clip region a path is visible in, or the extent of a shape.</summary>
internal readonly record struct Rect(double MinX, double MinY, double MaxX, double MaxY)
{
    public bool IsEmpty => MaxX < MinX || MaxY < MinY;

    public Rect Intersect(Rect other) =>
        new(Math.Max(MinX, other.MinX), Math.Max(MinY, other.MinY), Math.Min(MaxX, other.MaxX), Math.Min(MaxY, other.MaxY));

    public bool Contains(XY p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public bool Intersects(Rect other) => !IsEmpty && !other.IsEmpty
        && other.MaxX >= MinX && other.MinX <= MaxX && other.MaxY >= MinY && other.MinY <= MaxY;

    /// <summary>The rectangle grown by <paramref name="margin"/> on every side.</summary>
    public Rect Grow(double margin) => new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

    public static Rect Around(IEnumerable<XY> points)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return new Rect(minX, minY, maxX, maxY);
    }
}

/// <summary>A circular arc in drawing units, counter-clockwise from <see cref="StartAngle"/> to <see cref="EndAngle"/> (radians).</summary>
internal readonly record struct ArcFit(XY Center, double Radius, double StartAngle, double EndAngle)
{
    /// <summary>The extent of the arc itself, from points sampled along it.</summary>
    public Rect Bounds()
    {
        var (center, radius, start, sweep) = (Center, Radius, StartAngle, CadGeometry.Sweep(this));
        const int samples = 16;
        return Rect.Around(Enumerable.Range(0, samples + 1).Select(i =>
        {
            var angle = start + (sweep * i / samples);
            return new XY(center.X + (radius * Math.Cos(angle)), center.Y + (radius * Math.Sin(angle)));
        }));
    }
}

internal static class CadGeometry
{
    /// <summary>How far, relative to the radius, the Bézier may stray from the circle and still count as an arc.</summary>
    internal const double ArcTolerance = 0.002;

    /// <summary>
    /// How far, relative to the chord, the control points of a Bézier may leave the chord and still count
    /// as a straight line. Producers write gentle curves and even straight edges as Béziers.
    /// </summary>
    internal const double FlatTolerance = 0.001;

    /// <summary>
    /// The circular arc a cubic Bézier draws, when it draws one. PDF producers write circles and arcs as
    /// Béziers of up to a quarter circle each; turning those back into ARC entities keeps the drawing exact
    /// and editable. Anything that isn't a circle within <see cref="ArcTolerance"/> returns null.
    /// </summary>
    public static ArcFit? FitArc(XY p0, XY p1, XY p2, XY p3)
    {
        var mid = Bezier(p0, p1, p2, p3, 0.5);
        if (Circumcenter(p0, mid, p3) is not { } center)
        {
            return null;
        }

        var radius = Distance(center, p0);
        if (radius <= 0 || double.IsNaN(radius))
        {
            return null;
        }

        foreach (var t in (double[])[0.125, 0.25, 0.375, 0.625, 0.75, 0.875])
        {
            if (Math.Abs(Distance(center, Bezier(p0, p1, p2, p3, t)) - radius) > ArcTolerance * radius)
            {
                return null;
            }
        }

        // The turn from the start through the midpoint to the end gives the direction of travel.
        var counterClockwise = Cross(p0 - center, mid - center) + Cross(mid - center, p3 - center) > 0;
        var a0 = Math.Atan2(p0.Y - center.Y, p0.X - center.X);
        var a3 = Math.Atan2(p3.Y - center.Y, p3.X - center.X);
        return counterClockwise ? new ArcFit(center, radius, a0, a3) : new ArcFit(center, radius, a3, a0);
    }

    /// <summary>True when the Bézier is a straight line from its start to its end, within <see cref="FlatTolerance"/>.</summary>
    public static bool IsFlat(XY p0, XY p1, XY p2, XY p3)
    {
        var chord = Distance(p0, p3);
        if (chord <= 0)
        {
            return false;
        }

        var d = p3 - p0;
        double Offset(XY p) => Math.Abs(Cross(d, p - p0)) / chord;
        double Along(XY p) => Dot(d, p - p0) / (chord * chord);

        // Control points on the chord, and between its ends: the curve never leaves or overshoots it.
        return Offset(p1) <= FlatTolerance * chord && Offset(p2) <= FlatTolerance * chord
            && Along(p1) is >= -FlatTolerance and <= 1 + FlatTolerance && Along(p2) is >= -FlatTolerance and <= 1 + FlatTolerance;
    }

    /// <summary>The part of the segment inside <paramref name="clip"/> (Liang–Barsky), or null when none is.</summary>
    public static (XY From, XY To)? ClipSegment(XY a, XY b, Rect clip)
    {
        double t0 = 0, t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        bool Edge(double p, double q)
        {
            if (p == 0)
            {
                return q >= 0;
            }

            var t = q / p;
            if (p < 0)
            {
                if (t > t1)
                {
                    return false;
                }

                t0 = Math.Max(t0, t);
            }
            else
            {
                if (t < t0)
                {
                    return false;
                }

                t1 = Math.Min(t1, t);
            }

            return true;
        }

        if (!Edge(-dx, a.X - clip.MinX) || !Edge(dx, clip.MaxX - a.X) || !Edge(-dy, a.Y - clip.MinY) || !Edge(dy, clip.MaxY - a.Y))
        {
            return null;
        }

        return (new XY(a.X + (t0 * dx), a.Y + (t0 * dy)), new XY(a.X + (t1 * dx), a.Y + (t1 * dy)));
    }

    /// <summary>How far, relative to the radius, arcs of one circle may disagree about its center and radius.</summary>
    internal const double SameCircleTolerance = 0.01;

    /// <summary>How far apart, in radians, the end of one arc and the start of the next may be to join them.</summary>
    internal const double JoinAngleTolerance = 0.02;

    /// <summary>The whole circle a closed chain of arcs draws, or null when they aren't one full circle.</summary>
    public static (XY Center, double Radius)? MergeCircle(IReadOnlyList<ArcFit> arcs)
    {
        var (center, radius) = Average(arcs);
        if (!arcs.All(arc => OnCircle(arc, center, radius)))
        {
            return null;
        }

        var sweep = arcs.Sum(Sweep);
        return Math.Abs(sweep - (2 * Math.PI)) <= 0.03 * 2 * Math.PI ? (center, radius) : null;
    }

    /// <summary>One arc for two neighbouring arcs of the same circle, in either direction of travel.</summary>
    public static ArcFit? JoinArcs(ArcFit a, ArcFit b)
    {
        var (center, radius) = Average([a, b]);
        if (!OnCircle(a, center, radius) || !OnCircle(b, center, radius) || Sweep(a) + Sweep(b) >= (2 * Math.PI) - JoinAngleTolerance)
        {
            return null;
        }

        // Arcs are stored counter-clockwise, so a clockwise path meets the previous arc at its start.
        if (Math.Abs(AngleBetween(a.EndAngle, b.StartAngle)) <= JoinAngleTolerance)
        {
            return new ArcFit(center, radius, a.StartAngle, b.EndAngle);
        }

        if (Math.Abs(AngleBetween(b.EndAngle, a.StartAngle)) <= JoinAngleTolerance)
        {
            return new ArcFit(center, radius, b.StartAngle, a.EndAngle);
        }

        return null;
    }

    /// <summary>The counter-clockwise sweep of an arc, in (0, 2π].</summary>
    public static double Sweep(ArcFit arc)
    {
        var sweep = (arc.EndAngle - arc.StartAngle) % (2 * Math.PI);
        return sweep <= 0 ? sweep + (2 * Math.PI) : sweep;
    }

    private static (XY Center, double Radius) Average(IReadOnlyList<ArcFit> arcs)
    {
        var weights = arcs.Select(Sweep).ToList();
        var total = weights.Sum();
        var x = arcs.Select((arc, i) => arc.Center.X * weights[i]).Sum() / total;
        var y = arcs.Select((arc, i) => arc.Center.Y * weights[i]).Sum() / total;
        var r = arcs.Select((arc, i) => arc.Radius * weights[i]).Sum() / total;
        return (new XY(x, y), r);
    }

    private static bool OnCircle(ArcFit arc, XY center, double radius) =>
        Distance(arc.Center, center) <= SameCircleTolerance * radius && Math.Abs(arc.Radius - radius) <= SameCircleTolerance * radius;

    /// <summary>The signed difference b − a, wrapped into (−π, π].</summary>
    private static double AngleBetween(double a, double b)
    {
        var d = (b - a) % (2 * Math.PI);
        return d > Math.PI ? d - (2 * Math.PI) : d <= -Math.PI ? d + (2 * Math.PI) : d;
    }

    public static XY Bezier(XY p0, XY p1, XY p2, XY p3, double t)
    {
        var u = 1 - t;
        return (u * u * u * p0) + (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t * p3);
    }

    public static double Distance(XY a, XY b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static double Cross(XY a, XY b) => (a.X * b.Y) - (a.Y * b.X);

    private static double Dot(XY a, XY b) => (a.X * b.X) + (a.Y * b.Y);

    private static XY? Circumcenter(XY a, XY b, XY c)
    {
        var d = 2 * ((a.X * (b.Y - c.Y)) + (b.X * (c.Y - a.Y)) + (c.X * (a.Y - b.Y)));
        var scale = Math.Max(Distance(a, c), Distance(a, b));
        if (Math.Abs(d) < 1e-9 * scale * scale)
        {
            return null;
        }

        var a2 = (a.X * a.X) + (a.Y * a.Y);
        var b2 = (b.X * b.X) + (b.Y * b.Y);
        var c2 = (c.X * c.X) + (c.Y * c.Y);
        return new XY(
            ((a2 * (b.Y - c.Y)) + (b2 * (c.Y - a.Y)) + (c2 * (a.Y - b.Y))) / d,
            ((a2 * (c.X - b.X)) + (b2 * (a.X - c.X)) + (c2 * (b.X - a.X))) / d);
    }
}

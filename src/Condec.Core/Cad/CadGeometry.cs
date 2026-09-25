// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CSMath;

namespace Condec.Core.Cad;

/// <summary>
/// Maps PDF user space (points, origin at the bottom left of the crop box) to drawing units, turning the
/// page the way a PDF viewer shows it when the page has a /Rotate entry.
/// </summary>
internal readonly record struct PageTransform(double Left, double Bottom, double Width, double Height, int Rotation, double UnitsPerPoint)
{
    public XY Map(double x, double y)
    {
        var dx = x - Left;
        var dy = y - Bottom;

        // /Rotate turns the page clockwise for display. The results are still measured from the
        // bottom left of the page as shown.
        var (px, py) = ((((Rotation % 360) + 360) % 360) switch
        {
            90 => (dy, Width - dx),
            180 => (Width - dx, Height - dy),
            270 => (Height - dy, dx),
            _ => (dx, dy),
        });

        return new XY(px * UnitsPerPoint, py * UnitsPerPoint);
    }

    /// <summary>Clockwise display rotation turns text counter-clockwise in drawing angles.</summary>
    public double MapAngle(double radians) => radians - (Rotation * Math.PI / 180);
}

/// <summary>A circular arc in drawing units, counter-clockwise from <see cref="StartAngle"/> to <see cref="EndAngle"/> (radians).</summary>
internal readonly record struct ArcFit(XY Center, double Radius, double StartAngle, double EndAngle);

internal static class CadGeometry
{
    /// <summary>How far, relative to the radius, the Bézier may stray from the circle and still count as an arc.</summary>
    internal const double ArcTolerance = 0.002;

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

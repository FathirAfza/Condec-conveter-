// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Architecture;

/// <summary>A shape found in a picture, in picture pixels with y going up (the way a drawing counts).</summary>
public abstract record DrawingPrimitive;

/// <summary>A line (two points) or a polyline.</summary>
public sealed record PolylinePrimitive(IReadOnlyList<(double X, double Y)> Points, bool IsClosed) : DrawingPrimitive
{
    public bool IsLine => !IsClosed && Points.Count == 2;
}

/// <summary>A circular arc, counterclockwise from <paramref name="StartAngle"/> to <paramref name="EndAngle"/> (radians).</summary>
public sealed record ArcPrimitive(double CenterX, double CenterY, double Radius, double StartAngle, double EndAngle) : DrawingPrimitive
{
    /// <summary>How far the arc turns, 0 to 2π.</summary>
    public double Sweep
    {
        get
        {
            var sweep = EndAngle - StartAngle;
            while (sweep < 0)
            {
                sweep += 2 * Math.PI;
            }

            return sweep;
        }
    }
}

public sealed record CirclePrimitive(double CenterX, double CenterY, double Radius) : DrawingPrimitive;

/// <summary>
/// An area filled with one color, such as a part of a logo: its outlines in picture pixels with y up, outer edges and holes
/// alike. A point is inside when an odd number of outlines go around it.
/// </summary>
public sealed record FillPrimitive(IReadOnlyList<IReadOnlyList<(double X, double Y)>> Loops, Rgb Color) : DrawingPrimitive;

/// <summary>Turns a traced path into lines, polylines, arcs and circles.</summary>
internal static class CurveFitter
{
    /// <summary>An arc must turn at least this far (20°), or it is a gentle bend that a line stands in for.</summary>
    internal const double MinimumSweep = 20 * Math.PI / 180;

    /// <summary>Arcs smaller than this radius (pixels) are corners rounded by the pixel grid, not arcs.</summary>
    internal const double MinimumRadius = 5;

    /// <param name="points">The path in order, y up.</param>
    /// <param name="closed">The last point connects back to the first.</param>
    /// <param name="tolerance">How far, in pixels, the result may stray from the path.</param>
    public static List<DrawingPrimitive> Fit(IReadOnlyList<(double X, double Y)> points, bool closed, double tolerance)
    {
        var result = new List<DrawingPrimitive>();
        if (points.Count < 2)
        {
            return result;
        }

        if (closed && points.Count >= 12 && TryFitCircle(points, 0, points.Count - 1, tolerance, out var circle, closedPath: true)
            && circle.Radius >= MinimumRadius)
        {
            result.Add(new CirclePrimitive(circle.CenterX, circle.CenterY, circle.Radius));
            return result;
        }

        IReadOnlyList<(double X, double Y)> chain = closed ? [.. points, points[0]] : points;
        var kept = Simplify(chain, tolerance);

        if (kept.Count == 2)
        {
            result.Add(new PolylinePrimitive([chain[kept[0]], chain[kept[1]]], false));
            return result;
        }

        // Runs of short corners that turn the same way and sit on one circle are an arc.
        var polyline = new List<(double X, double Y)>();
        var vertex = 0;
        while (vertex < kept.Count - 1)
        {
            var best = -1;
            ArcPrimitive? bestArc = null;
            for (var end = vertex + 2; end < kept.Count; end++)
            {
                if (!TurnsTheSameWay(chain, kept, vertex, end))
                {
                    break;
                }

                if (TryFitCircle(chain, kept[vertex], kept[end], tolerance, out var arc, closedPath: false))
                {
                    best = end;
                    bestArc = arc;
                }
            }

            if (bestArc is not null)
            {
                Flush(result, polyline);
                result.Add(bestArc);
                vertex = best;
                polyline.Add(chain[kept[vertex]]);
                continue;
            }

            if (polyline.Count == 0)
            {
                polyline.Add(chain[kept[vertex]]);
            }

            polyline.Add(chain[kept[vertex + 1]]);
            vertex++;
        }

        Flush(result, polyline);
        if (closed && result.Count == 1 && result[0] is PolylinePrimitive { IsClosed: false } only && only.Points.Count >= 4)
        {
            // The same point at both ends: drop the repeat and close it.
            result[0] = new PolylinePrimitive(only.Points.Take(only.Points.Count - 1).ToList(), true);
        }

        return result;
    }

    private static void Flush(List<DrawingPrimitive> result, List<(double X, double Y)> polyline)
    {
        if (polyline.Count >= 2)
        {
            result.Add(new PolylinePrimitive([.. polyline], false));
        }

        polyline.Clear();
    }

    /// <summary>Ramer–Douglas–Peucker: the indices of the points that keep the path within <paramref name="tolerance"/>.</summary>
    internal static List<int> Simplify(IReadOnlyList<(double X, double Y)> points, double tolerance)
    {
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            var index = -1;
            var max = tolerance;
            for (var i = first + 1; i < last; i++)
            {
                var d = DistanceToSegment(points[i], points[first], points[last]);
                if (d > max)
                {
                    max = d;
                    index = i;
                }
            }

            if (index >= 0)
            {
                keep[index] = true;
                stack.Push((first, index));
                stack.Push((index, last));
            }
        }

        var kept = new List<int>();
        for (var i = 0; i < keep.Length; i++)
        {
            if (keep[i])
            {
                kept.Add(i);
            }
        }

        return kept;
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

    /// <summary>Every corner from <paramref name="from"/> to <paramref name="to"/> turns the same way, by less than 70°.</summary>
    private static bool TurnsTheSameWay(IReadOnlyList<(double X, double Y)> chain, List<int> kept, int from, int to)
    {
        var sign = 0;
        for (var i = from + 1; i < to; i++)
        {
            var a = chain[kept[i - 1]];
            var b = chain[kept[i]];
            var c = chain[kept[i + 1]];
            var cross = ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));
            var dot = ((b.X - a.X) * (c.X - b.X)) + ((b.Y - a.Y) * (c.Y - b.Y));
            var turn = Math.Atan2(cross, dot);
            if (Math.Abs(turn) > 70 * Math.PI / 180)
            {
                return false;
            }

            var thisSign = Math.Sign(cross);
            if (thisSign == 0)
            {
                continue;
            }

            if (sign != 0 && thisSign != sign)
            {
                return false;
            }

            sign = thisSign;
        }

        return true;
    }

    /// <summary>
    /// Fits a circle (Kåsa's least squares) to the points from <paramref name="first"/> to <paramref name="last"/> and says
    /// whether they lie on it closely enough, turn steadily, and turn far enough to be an arc.
    /// </summary>
    private static bool TryFitCircle(IReadOnlyList<(double X, double Y)> points, int first, int last, double tolerance, out ArcPrimitive arc, bool closedPath)
    {
        arc = null!;
        var count = last - first + 1;
        if (count < 6)
        {
            return false;
        }

        double meanX = 0, meanY = 0;
        for (var i = first; i <= last; i++)
        {
            meanX += points[i].X;
            meanY += points[i].Y;
        }

        meanX /= count;
        meanY /= count;

        // Normal equations of  x² + y² + A·x + B·y + C = 0  on centered coordinates.
        double suu = 0, suv = 0, svv = 0, suuu = 0, svvv = 0, suvv = 0, svuu = 0;
        for (var i = first; i <= last; i++)
        {
            var u = points[i].X - meanX;
            var v = points[i].Y - meanY;
            suu += u * u;
            suv += u * v;
            svv += v * v;
            suuu += u * u * u;
            svvv += v * v * v;
            suvv += u * v * v;
            svuu += v * u * u;
        }

        var determinant = (suu * svv) - (suv * suv);
        if (Math.Abs(determinant) < 1e-9)
        {
            return false;
        }

        var right1 = 0.5 * (suuu + suvv);
        var right2 = 0.5 * (svvv + svuu);
        var centerU = ((right1 * svv) - (right2 * suv)) / determinant;
        var centerV = ((suu * right2) - (suv * right1)) / determinant;
        var radius = Math.Sqrt((centerU * centerU) + (centerV * centerV) + ((suu + svv) / count));
        if (double.IsNaN(radius) || radius < MinimumRadius || radius > 1e6)
        {
            return false;
        }

        var centerX = centerU + meanX;
        var centerY = centerV + meanY;

        // Every point close to the circle, and the angle moving steadily one way.
        var allowed = tolerance + (0.01 * radius);
        var previousAngle = Math.Atan2(points[first].Y - centerY, points[first].X - centerX);
        var total = 0.0;
        var direction = 0;
        for (var i = first; i <= last; i++)
        {
            var dx = points[i].X - centerX;
            var dy = points[i].Y - centerY;
            if (Math.Abs(Math.Sqrt((dx * dx) + (dy * dy)) - radius) > allowed)
            {
                return false;
            }

            var angle = Math.Atan2(dy, dx);
            var step = angle - previousAngle;
            while (step > Math.PI)
            {
                step -= 2 * Math.PI;
            }

            while (step < -Math.PI)
            {
                step += 2 * Math.PI;
            }

            if (Math.Abs(step) > 1e-6)
            {
                var stepDirection = Math.Sign(step);
                if (direction != 0 && stepDirection != direction && Math.Abs(step) * radius > 1.5)
                {
                    return false;
                }

                direction = direction == 0 ? stepDirection : direction;
            }

            total += step;
            previousAngle = angle;
        }

        if (Math.Abs(total) < MinimumSweep)
        {
            return false;
        }

        var startAngle = Math.Atan2(points[first].Y - centerY, points[first].X - centerX);
        var endAngle = Math.Atan2(points[last].Y - centerY, points[last].X - centerX);
        arc = total > 0
            ? new ArcPrimitive(centerX, centerY, radius, startAngle, endAngle)
            : new ArcPrimitive(centerX, centerY, radius, endAngle, startAngle);

        // A closed path that goes all the way round is a circle: the caller wants that, not an arc.
        return !closedPath || Math.Abs(total) >= 2 * Math.PI * 0.9;
    }
}

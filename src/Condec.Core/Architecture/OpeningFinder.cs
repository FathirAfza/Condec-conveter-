// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Architecture;

/// <summary>
/// Finds the doors and windows among the line-work of a plan. A door is the way a plan draws one: an arc of about a quarter
/// circle and the leaf, a line from the arc's center to one end of it. A window is a stack of three or more short parallel
/// lines of one length lying close together. `[ASUMSI]`: both rules are the common drawing conventions and were tuned on
/// drawings made in code; no floor-plan sample was available to calibrate them.
/// </summary>
public static class OpeningFinder
{
    /// <summary>A door's arc turns between these angles (60° to 120°).</summary>
    public const double MinimumDoorSweep = 60 * Math.PI / 180;

    public const double MaximumDoorSweep = 120 * Math.PI / 180;

    /// <summary>A door's arc is no larger than this share of the shorter side of the picture (a door is not a roundabout).</summary>
    public const double MaximumDoorRadiusShare = 0.15;

    /// <summary>The leaf is within this share of the arc's radius in length.</summary>
    public const double LeafLengthTolerance = 0.2;

    /// <summary>Window lines are within this share of each other in length.</summary>
    public const double WindowLengthTolerance = 0.12;

    /// <summary>A window's lines lie no further apart than this share of their length.</summary>
    public const double WindowSpacingShare = 0.35;

    /// <summary>Window lines are closer than this to be the same stroke seen twice (pixels).</summary>
    public const double WindowMinimumSpacing = 2;

    public const int MinimumWindowLines = 3;

    public const int MaximumWindowLines = 5;

    /// <summary>A shape of the line-work that is left, and the shape it was found as (the same one unless a door's leaf was cut out of it).</summary>
    public sealed record Piece(DrawingPrimitive Shape, DrawingPrimitive Source);

    /// <summary>The shapes that make each opening, and the line-work that is left.</summary>
    public sealed record Result(IReadOnlyList<IReadOnlyList<DrawingPrimitive>> Openings, IReadOnlyList<Piece> Remaining);

    public static Result Find(IReadOnlyList<DrawingPrimitive> primitives, int pictureWidth, int pictureHeight, double tolerance)
    {
        var current = primitives.Select(p => new Piece(p, p)).ToList();
        var openings = new List<IReadOnlyList<DrawingPrimitive>>();
        var maximumRadius = MaximumDoorRadiusShare * Math.Min(pictureWidth, pictureHeight);

        foreach (var arc in primitives.OfType<ArcPrimitive>())
        {
            if (arc.Sweep < MinimumDoorSweep || arc.Sweep > MaximumDoorSweep || arc.Radius > maximumRadius)
            {
                continue;
            }

            current.RemoveAll(p => ReferenceEquals(p.Shape, arc));
            var leaf = TakeLeaf(arc, current, tolerance);
            openings.Add(leaf is null ? [arc] : [arc, leaf]);
        }

        var lines = current.Select(p => p.Shape).OfType<PolylinePrimitive>().Where(p => p.IsLine).ToList();
        var inWindows = new HashSet<DrawingPrimitive>(ReferenceEqualityComparer.Instance);
        foreach (var window in FindWindows(lines, Math.Min(pictureWidth, pictureHeight)))
        {
            foreach (var line in window)
            {
                inWindows.Add(line);
            }

            openings.Add(window);
        }

        current.RemoveAll(p => inWindows.Contains(p.Shape));
        return new Result(openings, current);
    }

    /// <summary>
    /// Takes the door's leaf out of <paramref name="shapes"/>: a straight run from the arc's center to one end of the arc, about
    /// as long as the radius. It is often part of a longer polyline (the wall runs into the hinge and the leaf runs on), so the
    /// polyline is cut there and its two pieces stay.
    /// </summary>
    private static PolylinePrimitive? TakeLeaf(ArcPrimitive arc, List<Piece> shapes, double tolerance)
    {
        var slack = Math.Max(tolerance * 2, 2);
        var ends = new[]
        {
            (X: arc.CenterX + (arc.Radius * Math.Cos(arc.StartAngle)), Y: arc.CenterY + (arc.Radius * Math.Sin(arc.StartAngle))),
            (X: arc.CenterX + (arc.Radius * Math.Cos(arc.EndAngle)), Y: arc.CenterY + (arc.Radius * Math.Sin(arc.EndAngle))),
        };

        Piece? from = null;
        var at = -1;
        var bestError = double.MaxValue;
        foreach (var piece in shapes)
        {
            if (piece.Shape is not PolylinePrimitive { IsClosed: false } polyline)
            {
                continue;
            }

            for (var i = 0; i + 1 < polyline.Points.Count; i++)
            {
                var (a, b) = (polyline.Points[i], polyline.Points[i + 1]);
                foreach (var (hinge, tip) in new[] { (a, b), (b, a) })
                {
                    var atCenter = Distance(hinge, (arc.CenterX, arc.CenterY));
                    var length = Distance(hinge, tip);
                    if (atCenter > slack + (0.1 * arc.Radius) || Math.Abs(length - arc.Radius) > LeafLengthTolerance * arc.Radius)
                    {
                        continue;
                    }

                    var toEnd = Math.Min(Distance(tip, ends[0]), Distance(tip, ends[1]));
                    if (toEnd > slack + (0.15 * arc.Radius))
                    {
                        continue;
                    }

                    if (atCenter + toEnd < bestError)
                    {
                        bestError = atCenter + toEnd;
                        from = piece;
                        at = i;
                    }
                }
            }
        }

        if (from is null)
        {
            return null;
        }

        var points = ((PolylinePrimitive)from.Shape).Points;
        shapes.Remove(from);
        if (at >= 1)
        {
            shapes.Add(new Piece(new PolylinePrimitive(points.Take(at + 1).ToList(), false), from.Source));
        }

        if (at + 2 < points.Count)
        {
            shapes.Add(new Piece(new PolylinePrimitive(points.Skip(at + 1).ToList(), false), from.Source));
        }

        return new PolylinePrimitive([points[at], points[at + 1]], false);
    }

    /// <summary>Stacks of parallel lines of one length, lying close together and level with each other.</summary>
    private static List<IReadOnlyList<DrawingPrimitive>> FindWindows(List<PolylinePrimitive> lines, int shorterSide)
    {
        var maximumLength = shorterSide * 0.25;
        var candidates = lines
            .Select(l => (Line: l, Length: Distance(l.Points[0], l.Points[1])))
            .Where(c => c.Length >= 8 && c.Length <= maximumLength)
            .ToList();

        var parent = Enumerable.Range(0, candidates.Count).ToArray();
        int Root(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                if (SitTogether(candidates[i].Line, candidates[i].Length, candidates[j].Line, candidates[j].Length))
                {
                    parent[Root(j)] = Root(i);
                }
            }
        }

        var windows = new List<IReadOnlyList<DrawingPrimitive>>();
        foreach (var group in Enumerable.Range(0, candidates.Count).GroupBy(Root))
        {
            var members = group.ToList();
            if (members.Count >= MinimumWindowLines && members.Count <= MaximumWindowLines)
            {
                windows.Add(members.Select(m => (DrawingPrimitive)candidates[m].Line).ToList());
            }
        }

        return windows;
    }

    private static bool SitTogether(PolylinePrimitive a, double lengthA, PolylinePrimitive b, double lengthB)
    {
        if (Math.Abs(lengthA - lengthB) > WindowLengthTolerance * Math.Max(lengthA, lengthB))
        {
            return false;
        }

        var (ax, ay) = (a.Points[1].X - a.Points[0].X, a.Points[1].Y - a.Points[0].Y);
        var (bx, by) = (b.Points[1].X - b.Points[0].X, b.Points[1].Y - b.Points[0].Y);

        // Parallel: the sine of the angle between them is small (about 3°).
        var cross = ((ax * by) - (ay * bx)) / (lengthA * lengthB);
        if (Math.Abs(cross) > 0.05)
        {
            return false;
        }

        // Side by side: the distance across, and how much of their length faces the other.
        var (ux, uy) = (ax / lengthA, ay / lengthA);
        var across = Math.Abs(((b.Points[0].X - a.Points[0].X) * -uy) + ((b.Points[0].Y - a.Points[0].Y) * ux));
        if (across < WindowMinimumSpacing || across > WindowSpacingShare * Math.Max(lengthA, lengthB))
        {
            return false;
        }

        var aStart = 0.0;
        var aEnd = lengthA;
        var bStart = ((b.Points[0].X - a.Points[0].X) * ux) + ((b.Points[0].Y - a.Points[0].Y) * uy);
        var bEnd = ((b.Points[1].X - a.Points[0].X) * ux) + ((b.Points[1].Y - a.Points[0].Y) * uy);
        if (bStart > bEnd)
        {
            (bStart, bEnd) = (bEnd, bStart);
        }

        var overlap = Math.Min(aEnd, bEnd) - Math.Max(aStart, bStart);
        return overlap >= 0.85 * Math.Min(lengthA, lengthB);
    }

    private static double Distance((double X, double Y) a, (double X, double Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

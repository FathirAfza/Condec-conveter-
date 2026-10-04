// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using CSMath;

namespace Condec.Core.Architecture;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Black => new(0, 0, 0);
}

/// <summary>A stroked or filled outline of a drawing, in drawing units with y up.</summary>
/// <param name="WidthMm">Line width on paper; ignored for fills.</param>
/// <param name="Holes">For a fill, more outlines filled with it odd-even (a solid HATCH with islands): a hole stays open.</param>
public sealed record CadPath(
    IReadOnlyList<(double X, double Y)> Points,
    bool IsClosed,
    bool IsFilled,
    Rgb Color,
    double WidthMm,
    string Layer,
    IReadOnlyList<IReadOnlyList<(double X, double Y)>>? Holes = null);

/// <summary>A line of text, anchored at its bottom left, in drawing units.</summary>
/// <param name="Rotation">Counterclockwise, in radians.</param>
/// <param name="WidthFactor">How wide the letters are drawn compared with Helvetica's; CAD's plain fonts are narrower.</param>
public sealed record CadLabel(string Text, double X, double Y, double Height, double Rotation, Rgb Color, string Layer, double WidthFactor = 1);

/// <summary>A layer of the file: its name, whether the file shows it, and its color.</summary>
public sealed record CadLayerInfo(string Name, bool IsOn, Rgb Color);

/// <summary>
/// A CAD drawing as plain geometry: every entity flattened to polylines, fills and text, blocks and dimensions expanded.
/// Drawing it needs no CAD program (DESIGN §6.3.2).
/// </summary>
/// <param name="Paths">Lines, outlines and fills.</param>
/// <param name="Labels">Text.</param>
/// <param name="Layers">The layers of the file, with the ones that carry something.</param>
/// <param name="Units">The drawing's unit as the file states it.</param>
public sealed record CadScene(IReadOnlyList<CadPath> Paths, IReadOnlyList<CadLabel> Labels, IReadOnlyList<CadLayerInfo> Layers, UnitsType Units)
{
    /// <summary>The rectangle (minX, minY, maxX, maxY) that holds what <paramref name="visible"/> layers show; null when nothing is shown.</summary>
    public (double MinX, double MinY, double MaxX, double MaxY)? BoundsOf(Func<string, bool> visible)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;
        void Take(double x, double y)
        {
            any = true;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        foreach (var path in Paths.Where(p => visible(p.Layer)))
        {
            foreach (var (x, y) in path.Points.Concat(path.Holes?.SelectMany(hole => hole) ?? []))
            {
                Take(x, y);
            }
        }

        foreach (var label in Labels.Where(l => visible(l.Layer)))
        {
            // The text's own extent is not known without a font: a box as wide as it is long at half height per letter.
            Take(label.X, label.Y);
            var width = label.Text.Length * label.Height * 0.8 * label.WidthFactor;
            Take(label.X + (width * Math.Cos(label.Rotation)), label.Y + (width * Math.Sin(label.Rotation)));
            Take(label.X - (label.Height * Math.Sin(label.Rotation)), label.Y + (label.Height * Math.Cos(label.Rotation)));
        }

        return any ? (minX, minY, maxX, maxY) : null;
    }
}

/// <summary>Reads a drawing's entities into a <see cref="CadScene"/>.</summary>
public static class CadFlattener
{
    /// <summary>How deep blocks may nest; a block that contains itself stops here.</summary>
    private const int MaximumBlockDepth = 8;

    /// <summary>Points along a full circle; arcs, ellipses and splines use as many as their share of it.</summary>
    private const int CirclePoints = 96;

    /// <summary>Line width on paper when the drawing doesn't say, in millimeters.</summary>
    public const double DefaultWidthMm = 0.18;

    /// <summary>
    /// Text in a CAD font (SHX) is set narrower than Helvetica: drawn at Helvetica's width, words run into the ones next to
    /// them. On one real drawing the words sat 0.70 and 0.84 of Helvetica's width apart, and the default SHX font is nearer 0.6.
    /// `[ASUMSI]`: 0.75 for every style that is not a TrueType font; a little short is better than letters over each other.
    /// </summary>
    public const double PlainFontWidthFactor = 0.75;

    private readonly record struct Affine(double A, double B, double C, double D, double Tx, double Ty)
    {
        public static Affine Identity => new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) => ((A * x) + (C * y) + Tx, (B * x) + (D * y) + Ty);

        /// <summary>This transform applied after <paramref name="inner"/>.</summary>
        public Affine After(Affine inner) => new(
            (A * inner.A) + (C * inner.B),
            (B * inner.A) + (D * inner.B),
            (A * inner.C) + (C * inner.D),
            (B * inner.C) + (D * inner.D),
            (A * inner.Tx) + (C * inner.Ty) + Tx,
            (B * inner.Tx) + (D * inner.Ty) + Ty);

        public double Scale => Math.Sqrt(Math.Abs((A * D) - (B * C)));

        public double Rotation => Math.Atan2(B, A);
    }

    private sealed class Collector
    {
        public List<CadPath> Paths { get; } = [];

        public List<CadLabel> Labels { get; } = [];
    }

    public static CadScene Flatten(CadDocument cad, CancellationToken ct)
    {
        var collector = new Collector();
        foreach (var entity in cad.Entities)
        {
            ct.ThrowIfCancellationRequested();
            Add(entity, Affine.Identity, null, 0, collector);
        }

        var layers = cad.Layers
            .Select(l => new CadLayerInfo(l.Name, l.IsOn && !l.Flags.HasFlag(LayerFlags.Frozen), ColorOf(l.Color, Rgb.Black)))
            .ToList();
        return new CadScene(collector.Paths, collector.Labels, layers, cad.Header.InsUnits);
    }

    private static void Add(Entity entity, Affine at, Color? byBlock, int depth, Collector into)
    {
        if (entity.IsInvisible)
        {
            return;
        }

        var color = Resolve(entity, byBlock);
        var rgb = ToRgb(color);
        var layer = entity.Layer?.Name ?? Layer.DefaultName;
        var width = WidthOf(entity);

        void Stroke(IEnumerable<(double X, double Y)> points, bool closed, bool filled = false)
        {
            var mapped = points.Select(p => at.Apply(p.X, p.Y)).ToList();
            if (mapped.Count >= 2 || (filled && mapped.Count >= 3))
            {
                into.Paths.Add(new CadPath(mapped, closed, filled, rgb, width, layer));
            }
        }

        switch (entity)
        {
            case Line line:
                Stroke([(line.StartPoint.X, line.StartPoint.Y), (line.EndPoint.X, line.EndPoint.Y)], false);
                break;
            case LwPolyline lw:
                Stroke(WithBulges(lw.Vertices.Select(v => (v.Location.X, v.Location.Y, v.Bulge)).ToList(), lw.IsClosed, lw.Normal.Z < 0), lw.IsClosed);
                break;
            case Polyline2D p2:
                Stroke(WithBulges(p2.Vertices.Select(v => (v.Location.X, v.Location.Y, v.Bulge)).ToList(), p2.IsClosed, p2.Normal.Z < 0), p2.IsClosed);
                break;
            case Polyline3D p3:
                Stroke(p3.Vertices.Select(v => (v.Location.X, v.Location.Y)), p3.IsClosed);
                break;
            case Arc arc:
                Stroke(ArcPoints(arc), false);
                break;
            case Circle circle:
                Stroke(circle.PolygonalVertexes(CirclePoints).Select(p => (p.X, p.Y)), true);
                break;
            case Ellipse ellipse:
                Stroke(
                    ellipse.PolygonalVertexes(Math.Max(8, (int)(CirclePoints * (ellipse.EndParameter - ellipse.StartParameter) / (2 * Math.PI)))).Select(p => (p.X, p.Y)),
                    Math.Abs(ellipse.EndParameter - ellipse.StartParameter - (2 * Math.PI)) < 1e-6);
                break;
            case Spline spline:
                Stroke(SplinePoints(spline), spline.IsClosed);
                break;
            case Solid solid:
                Stroke([(solid.FirstCorner.X, solid.FirstCorner.Y), (solid.SecondCorner.X, solid.SecondCorner.Y), (solid.FourthCorner.X, solid.FourthCorner.Y), (solid.ThirdCorner.X, solid.ThirdCorner.Y)], true, filled: true);
                break;
            case Hatch hatch:
                var loops = hatch.Paths
                    .Select(boundary => boundary.GetPoints(CirclePoints).Select(p => at.Apply(p.X, p.Y)).ToList())
                    .Where(loop => loop.Count >= 3)
                    .ToList();

                // A solid hatch with islands (style Normal or Outer) is filled odd-even as AutoCAD fills it, so its holes stay open.
                if (hatch.IsSolid && hatch.Style != HatchStyleType.Ignore && loops.Count > 1)
                {
                    into.Paths.Add(new CadPath(loops[0], true, true, rgb, width, layer, loops.Skip(1).ToList()));
                    break;
                }

                foreach (var boundary in hatch.Paths)
                {
                    Stroke(boundary.GetPoints(CirclePoints).Select(p => (p.X, p.Y)), true, filled: hatch.IsSolid);
                }

                break;
            case TextEntity text:
                AddText(text.Value, text.InsertPoint.X, text.InsertPoint.Y, text.Height, text.Rotation, WidthOf(text.Style, text.WidthFactor), at, color, layer, into);
                break;
            case MText mtext:
                AddMText(mtext, at, color, layer, into);
                break;
            case Dimension dimension:
                if (dimension.Block is { } dimensionBlock && depth < MaximumBlockDepth)
                {
                    foreach (var inner in dimensionBlock.Entities)
                    {
                        Add(inner, at, entity.Color.IsByBlock ? byBlock : color, depth + 1, into);
                    }
                }

                break;
            case Insert insert:
                AddInsert(insert, at, color, depth, into);
                break;
        }
    }

    private static void AddInsert(Insert insert, Affine at, Color color, int depth, Collector into)
    {
        if (insert.Block is not { } block || depth >= MaximumBlockDepth)
        {
            return;
        }

        var cos = Math.Cos(insert.Rotation);
        var sin = Math.Sin(insert.Rotation);
        var sx = insert.XScale;
        var sy = insert.YScale;
        var basePoint = block.BlockEntity?.BasePoint ?? XYZ.Zero;
        var rows = Math.Max(1, (int)insert.RowCount);
        var columns = Math.Max(1, (int)insert.ColumnCount);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                // World = move to the insertion point, rotate, scale, after moving the block's base point to the origin.
                var local = new Affine(cos * sx, sin * sx, -sin * sy, cos * sy, 0, 0);
                var shift = local.Apply(-basePoint.X, -basePoint.Y);
                var offset = new Affine(1, 0, 0, 1, insert.InsertPoint.X + (column * insert.ColumnSpacing) + shift.X, insert.InsertPoint.Y + (row * insert.RowSpacing) + shift.Y);
                var transform = at.After(new Affine(local.A, local.B, local.C, local.D, offset.Tx, offset.Ty));
                foreach (var inner in block.Entities)
                {
                    Add(inner, transform, inner.Color.IsByBlock ? color : null, depth + 1, into);
                }
            }
        }
    }

    private static double WidthOf(TextStyle? style, double widthFactor)
    {
        var factor = double.IsFinite(widthFactor) && widthFactor > 0 ? widthFactor : 1;
        return style is { TrueType: not 0 } || (style?.Filename?.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ?? false)
            ? factor
            : factor * PlainFontWidthFactor;
    }

    private static void AddText(string value, double x, double y, double height, double rotation, double widthFactor, Affine at, Color color, string layer, Collector into)
    {
        if (string.IsNullOrWhiteSpace(value) || height <= 0 || !double.IsFinite(height))
        {
            return;
        }

        var (px, py) = at.Apply(x, y);
        into.Labels.Add(new CadLabel(value, px, py, height * at.Scale, rotation + at.Rotation, ToRgb(color), layer, widthFactor));
    }

    private static void AddMText(MText mtext, Affine at, Color color, string layer, Collector into)
    {
        var lines = mtext.GetPlainTextLines().ToList();
        var lineHeight = mtext.Height * 1.4;
        for (var i = 0; i < lines.Count; i++)
        {
            // The insertion point is the top left; each line hangs below the one before, along the text's own direction.
            var drop = (i + 1) * lineHeight;
            var x = mtext.InsertPoint.X + (drop * Math.Sin(mtext.Rotation));
            var y = mtext.InsertPoint.Y - (drop * Math.Cos(mtext.Rotation)) + (mtext.Height * 0.4);
            AddText(lines[i], x, y, mtext.Height, mtext.Rotation, WidthOf(mtext.Style, 1), at, color, layer, into);
        }
    }

    /// <summary>The vertices with every bulged segment replaced by the points of its arc.</summary>
    private static List<(double X, double Y)> WithBulges(List<(double X, double Y, double Bulge)> vertices, bool closed, bool mirrored)
    {
        var points = new List<(double X, double Y)>();
        var count = vertices.Count;
        for (var i = 0; i < count; i++)
        {
            var from = vertices[i];
            points.Add((mirrored ? -from.X : from.X, from.Y));
            var last = i == count - 1;
            if (last && !closed)
            {
                break;
            }

            if (Math.Abs(from.Bulge) < 1e-9)
            {
                continue;
            }

            var to = vertices[(i + 1) % count];
            foreach (var p in BulgePoints((from.X, from.Y), (to.X, to.Y), from.Bulge))
            {
                points.Add((mirrored ? -p.X : p.X, p.Y));
            }
        }

        if (closed && points.Count > 1 && points[^1] == points[0])
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    /// <summary>The points strictly between the ends of an arc segment of a polyline, from its bulge (the tangent of a quarter of the angle it turns).</summary>
    private static IEnumerable<(double X, double Y)> BulgePoints((double X, double Y) from, (double X, double Y) to, double bulge)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var chord = Math.Sqrt((dx * dx) + (dy * dy));
        if (chord < 1e-12)
        {
            yield break;
        }

        var theta = 4 * Math.Atan(bulge);
        var half = theta / 2;
        var back = (chord / 2) / Math.Tan(half);
        var cx = ((from.X + to.X) / 2) + (-dy / chord * back);
        var cy = ((from.Y + to.Y) / 2) + (dx / chord * back);
        var radius = Math.Sqrt(Math.Pow(from.X - cx, 2) + Math.Pow(from.Y - cy, 2));
        var start = Math.Atan2(from.Y - cy, from.X - cx);
        var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) / (2 * Math.PI) * CirclePoints));
        for (var step = 1; step < steps; step++)
        {
            var angle = start + (theta * step / steps);
            yield return (cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)));
        }
    }

    private static IEnumerable<(double X, double Y)> ArcPoints(Arc arc)
    {
        var sweep = arc.EndAngle - arc.StartAngle;
        while (sweep <= 0)
        {
            sweep += 2 * Math.PI;
        }

        var steps = Math.Max(4, (int)Math.Ceiling(sweep / (2 * Math.PI) * CirclePoints));
        var mirrored = arc.Normal.Z < 0;
        for (var step = 0; step <= steps; step++)
        {
            var angle = arc.StartAngle + (sweep * step / steps);
            var x = arc.Center.X + (arc.Radius * Math.Cos(angle));
            yield return (mirrored ? -x : x, arc.Center.Y + (arc.Radius * Math.Sin(angle)));
        }
    }

    private static IEnumerable<(double X, double Y)> SplinePoints(Spline spline)
    {
        try
        {
            return spline.PolygonalVertexes(Math.Max(16, spline.ControlPoints.Count * 8)).Select(p => (p.X, p.Y)).ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException or NullReferenceException)
        {
            // A spline whose knots don't add up is still drawn through the points it has.
            return (spline.FitPoints.Count > 1 ? spline.FitPoints : spline.ControlPoints).Select(p => (p.X, p.Y)).ToList();
        }
    }

    /// <summary>The color the entity is drawn in: its own, its layer's, or the block's it sits in.</summary>
    private static Color Resolve(Entity entity, Color? byBlock)
    {
        var color = entity.Color;
        if (color.IsByBlock)
        {
            return byBlock ?? Color.Default;
        }

        if (color.IsByLayer)
        {
            return entity.Layer?.Color is { IsByLayer: false, IsByBlock: false } layer ? layer : Color.Default;
        }

        return color;
    }

    private static Rgb ToRgb(Color color) => ColorOf(color, Rgb.Black);

    /// <summary>Index 7 is white on a dark screen and black on paper; any near-white color would be invisible on paper, so it is black too.</summary>
    private static Rgb ColorOf(Color color, Rgb fallback)
    {
        if (!color.IsTrueColor && color.Index is 7 or 0 or 256 or 257)
        {
            return fallback;
        }

        var rgb = new Rgb(color.R, color.G, color.B);
        return rgb.R >= 235 && rgb.G >= 235 && rgb.B >= 235 ? Rgb.Black : rgb;
    }

    private static double WidthOf(Entity entity)
    {
        var weight = entity.LineWeight;
        var hundredths = (short)weight;
        return hundredths > 0 ? Math.Clamp(hundredths / 100.0, 0.09, 2.0) : DefaultWidthMm;
    }
}

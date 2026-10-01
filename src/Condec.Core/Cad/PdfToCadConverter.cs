// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Types.Units;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Pdf;
using CSMath;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;

namespace Condec.Core.Cad;

/// <summary>The chosen PDF page has nothing that can become a drawing: no lines, curves, text or scanned ink.</summary>
public sealed class NothingToConvertException(string message) : Exception(message);

/// <summary>
/// One PDF page to a DXF or DWG drawing. Vector pages keep their geometry: lines become LINE or LWPOLYLINE,
/// Béziers that trace a circle become ARC or CIRCLE and the rest SPLINE, words become TEXT. Scanned pages are
/// rendered and their ink outlines traced into closed LWPOLYLINEs.
/// </summary>
public sealed class PdfToCadConverter(IPdfPageRasterizer? rasterizer) : IConverter
{
    /// <summary>
    /// AutoCAD 2000 (AC1015): the oldest version ACadSharp writes as both DXF and DWG, so the widest range
    /// of CAD programs opens the result.
    /// </summary>
    public const ACadVersion OutputVersion = ACadVersion.AC1015;

    /// <summary>Render resolution for tracing scans: 3 pixels per point is 216 dpi.</summary>
    internal const double ScanPixelsPerPoint = 3;

    /// <summary>How far a traced outline may deviate from the pixel contour, in pixels.</summary>
    internal const double ScanTolerancePixels = 1.0;

    /// <summary>A fill without an outline that covers this much of the page is its background.</summary>
    internal const double BackgroundCoverage = 0.95;

    /// <summary>Cap height of common fonts relative to the font size; DXF text height is the cap height.</summary>
    internal const double CapHeightRatio = 0.7;

    /// <summary>
    /// A filled four-sided shape no wider than this (in points) is a line drawn as a sliver, the way some
    /// producers paint strokes. It becomes one LINE along its middle instead of a thin closed outline.
    /// </summary>
    internal const double SliverMaxWidthPoints = 1.5;

    /// <summary>A sliver must be at least this many times longer than it is wide.</summary>
    internal const double SliverMinAspect = 4;

    /// <summary>Points closer than this, in PDF points, are the same point when joining segments.</summary>
    internal const double JoinTolerancePoints = 0.01;

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        FileExtension.Normalize(sourceExtension) == ".pdf" ? [".dxf", ".dwg"] : [];

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var target = FileExtension.Normalize(request.TargetExtension);
        var options = request.Options as CadOptions ?? new CadOptions(1);

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0, "Membaca halaman PDF"));
        var cad = new CadDocument(OutputVersion);
        cad.Header.InsUnits = options.Unit switch
        {
            CadUnit.Millimeters => UnitsType.Millimeters,
            CadUnit.Centimeters => UnitsType.Centimeters,
            CadUnit.Meters => UnitsType.Meters,
            _ => UnitsType.Inches,
        };

        int entityCount;
        using (var pdf = PdfInspector.Open(request.SourcePath))
        {
            if (options.PageNumber < 1 || options.PageNumber > pdf.NumberOfPages)
            {
                throw new ArgumentOutOfRangeException(nameof(request), $"Page {options.PageNumber} is outside 1..{pdf.NumberOfPages}.");
            }

            var page = pdf.GetPage(options.PageNumber);
            var entities = PdfInspector.GetPageKind(page) == PdfPageKind.Scan
                ? await TraceScanAsync(request.SourcePath, page, options, ct).ConfigureAwait(false)
                : ReadVectors(page, options, ct);

            foreach (var entity in entities)
            {
                cad.Entities.Add(entity);
            }

            entityCount = entities.Count;
        }

        if (entityCount == 0)
        {
            throw new NothingToConvertException($"Page {options.PageNumber} has nothing to convert.");
        }

        SetExtents(cad);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));
        ct.ThrowIfCancellationRequested();
        progress.Report(new ConversionProgress(ConversionStage.Encode, 0, $"Menulis {entityCount} objek CAD"));

        var bytes = CadFiles.Write(cad, target);
        await request.Output.WriteAsync(bytes, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }

    /// <summary>Drawing extents in the header, so "zoom extents" lands on the drawing right away.</summary>
    internal static void SetExtents(CadDocument cad)
    {
        var box = CSMath.BoundingBox.Null;
        foreach (var entity in cad.Entities)
        {
            box = box.Merge(entity.GetBoundingBox());
        }

        if (box.Extent == BoundingBoxExtent.Finite)
        {
            cad.Header.ModelSpaceExtMin = box.Min;
            cad.Header.ModelSpaceExtMax = box.Max;
        }
    }

    internal static List<Entity> ReadVectors(Page page, CadOptions options, CancellationToken ct)
    {
        var transform = new PageTransform(options.UnitsPerPoint);
        var pageBox = new Rect(0, 0, page.Width, page.Height);
        var clips = PdfClipTracker.Resolve(page, pageBox);
        var builder = new EntityBuilder(transform, options.JoinLines);

        // Invisible text (render mode 3, "neither") is either an OCR layer over a picture, which has no place
        // in a drawing, or the searchable twin of letters painted as filled outlines. In the second case the
        // outlines are dropped and the text is kept, since TEXT is what a CAD user can edit.
        var visibleLines = new List<TextLine>();
        var hiddenLines = new List<(TextLine Line, Rect Bounds)>();
        var backedLines = new HashSet<TextLine>();
        if (options.KeepText)
        {
            var visible = page.Letters.Where(letter => !IsInvisible(letter)).ToList();
            var hidden = page.Letters.Where(IsInvisible).ToList();
            visibleLines = TextLines.Group(NearestNeighbourWordExtractor.Instance.GetWords(visible));
            hiddenLines = TextLines.Group(NearestNeighbourWordExtractor.Instance.GetWords(hidden))
                .Select(line => (line, line.Bounds().Grow(line.First.PointSize * 0.5)))
                .ToList();
        }

        // Producers often paint a shape twice, once to fill it and once to stroke its outline, and paint the
        // page background as a filled rectangle. Neither belongs in a drawing.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pageArea = page.Width * page.Height;
        for (var i = 0; i < page.Paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = page.Paths[i];
            if (path.IsClipping || !(path.IsStroked || path.IsFilled) || IsPaperColoured(path))
            {
                continue;
            }

            // A fill whose outline is stroked in the same colour is just a solid shape.
            var solid = path.IsFilled && (!path.IsStroked || SameColour(path.FillColor, path.StrokeColor));
            var clip = clips?[i] ?? pageBox;
            Rect? pathBounds = null;
            if (path.GetBoundingRectangle() is { } bounds)
            {
                pathBounds = ToRect(bounds);
                if (solid && bounds.Width * bounds.Height >= BackgroundCoverage * pageArea)
                {
                    continue;
                }

                if (!clip.Intersects(pathBounds.Value))
                {
                    continue;
                }
            }

            if (!seen.Add(Signature(path)))
            {
                continue;
            }

            if (solid && pathBounds is { } shape && hiddenLines.FirstOrDefault(h => Contains(h.Bounds, shape)) is { Line: not null } backing)
            {
                backedLines.Add(backing.Line);
                continue;
            }

            var color = CadColors.FromPdf(path.IsStroked ? path.StrokeColor : path.FillColor);
            if (solid && Sliver(path) is { } sliver)
            {
                builder.AddSegment(transform.Map(sliver.From.X, sliver.From.Y), transform.Map(sliver.To.X, sliver.To.Y), transform.Map(clip), color);
                continue;
            }

            if (solid && path.Count >= 2 && Rings(path) is { } rings)
            {
                foreach (var ring in PolygonMerger.Merge(rings, JoinTolerancePoints))
                {
                    builder.AddPolygon(ring.Select(p => transform.Map(p.X, p.Y)).ToList(), transform.Map(clip), color);
                }

                continue;
            }

            foreach (var subpath in path)
            {
                builder.AddSubpath(subpath, transform.Map(clip), color);
            }
        }

        var entities = builder.Finish();

        foreach (var line in visibleLines.Concat(hiddenLines.Select(h => h.Line).Where(backedLines.Contains)))
        {
            if (pageBox.Intersects(line.Bounds()) && ToText(line, transform) is { } text)
            {
                entities.Add(text);
            }
        }

        return entities;
    }

    private static bool IsInvisible(Letter letter) => letter.RenderingMode is TextRenderingMode.Neither or TextRenderingMode.NeitherClip;

    /// <summary>White paint on white paper: background masks, cell fills and border rectangles a viewer never shows.</summary>
    internal static bool IsPaperColoured(PdfPath path) =>
        (!path.IsFilled || IsWhite(path.FillColor)) && (!path.IsStroked || IsWhite(path.StrokeColor));

    private static bool IsWhite(IColor? color)
    {
        if (color is null)
        {
            return false;
        }

        var (r, g, b) = color.ToRGBValues();
        return r >= 0.97 && g >= 0.97 && b >= 0.97;
    }

    private static bool SameColour(IColor? a, IColor? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        var (r1, g1, b1) = a.ToRGBValues();
        var (r2, g2, b2) = b.ToRGBValues();
        return Math.Abs(r1 - r2) < 0.02 && Math.Abs(g1 - g2) < 0.02 && Math.Abs(b1 - b2) < 0.02;
    }

    private static bool Contains(Rect outer, Rect inner) =>
        inner.MinX >= outer.MinX && inner.MaxX <= outer.MaxX && inner.MinY >= outer.MinY && inner.MaxY <= outer.MaxY;

    /// <summary>The subpaths as closed polygons, or null when any of them has a curve.</summary>
    private static List<IReadOnlyList<XY>>? Rings(PdfPath path)
    {
        var rings = new List<IReadOnlyList<XY>>();
        foreach (var subpath in path)
        {
            var ring = new List<XY>();
            foreach (var command in subpath.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.Move m:
                        ring.Add(P(m.Location));
                        break;
                    case PdfSubpath.Line l:
                        if (ring.Count == 0)
                        {
                            ring.Add(P(l.From));
                        }

                        ring.Add(P(l.To));
                        break;
                    case PdfSubpath.Close:
                        break;
                    default:
                        return null;
                }
            }

            if (ring.Count >= 3)
            {
                rings.Add(ring);
            }
        }

        return rings.Count > 0 ? rings : null;
    }

    /// <summary>The axis-aligned extent of a PdfPig rectangle, which may be rotated (rotated text) or inverted.</summary>
    internal static Rect ToRect(PdfRectangle rectangle) =>
        Rect.Around([P(rectangle.BottomLeft), P(rectangle.BottomRight), P(rectangle.TopLeft), P(rectangle.TopRight)]);

    private static XY P(PdfPoint point) => new(point.X, point.Y);

    /// <summary>The geometry of a path, rounded to 1/100 pt, to recognize the same shape painted twice.</summary>
    private static string Signature(PdfPath path)
    {
        var text = new System.Text.StringBuilder();
        void Add(PdfPoint p) => text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Round(p.X, 2)},{Math.Round(p.Y, 2)};");
        foreach (var subpath in path)
        {
            foreach (var command in subpath.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.Move m: text.Append('M'); Add(m.Location); break;
                    case PdfSubpath.Line l: text.Append('L'); Add(l.From); Add(l.To); break;
                    case PdfSubpath.CubicBezierCurve c: text.Append('C'); Add(c.StartPoint); Add(c.FirstControlPoint); Add(c.SecondControlPoint); Add(c.EndPoint); break;
                    case PdfSubpath.Close: text.Append('Z'); break;
                }
            }

            text.Append('|');
        }

        return text.ToString();
    }

    /// <summary>
    /// The centre line of a filled sliver: one closed subpath with four straight sides forming a long, thin
    /// parallelogram. Null for any other shape.
    /// </summary>
    internal static (PdfPoint From, PdfPoint To)? Sliver(PdfPath path)
    {
        if (path.Count != 1)
        {
            return null;
        }

        var corners = new List<PdfPoint>();
        foreach (var command in path[0].Commands)
        {
            switch (command)
            {
                case PdfSubpath.Move m:
                    corners.Add(m.Location);
                    break;
                case PdfSubpath.Line l when corners.Count == 0:
                    corners.Add(l.From);
                    corners.Add(l.To);
                    break;
                case PdfSubpath.Line l:
                    corners.Add(l.To);
                    break;
                case PdfSubpath.Close:
                    break;
                default:
                    return null;
            }
        }

        // The last corner may repeat the first when the path is closed explicitly.
        if (corners.Count == 5 && PdfDistance(corners[0], corners[4]) < 1e-6)
        {
            corners.RemoveAt(4);
        }

        if (corners.Count != 4)
        {
            return null;
        }

        var a = PdfDistance(corners[0], corners[1]);
        var b = PdfDistance(corners[1], corners[2]);
        var c = PdfDistance(corners[2], corners[3]);
        var d = PdfDistance(corners[3], corners[0]);
        var sideTolerance = 0.05 * Math.Max(Math.Max(a, b), 1e-9);
        if (Math.Abs(a - c) > sideTolerance || Math.Abs(b - d) > sideTolerance)
        {
            return null;
        }

        var (width, length) = a < b ? (a, b) : (b, a);
        if (width > SliverMaxWidthPoints || length < SliverMinAspect * Math.Max(width, 1e-9))
        {
            return null;
        }

        // Join the midpoints of the two short sides.
        return a < b
            ? (Mid(corners[0], corners[1]), Mid(corners[2], corners[3]))
            : (Mid(corners[1], corners[2]), Mid(corners[3], corners[0]));
    }

    private static PdfPoint Mid(PdfPoint a, PdfPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double PdfDistance(PdfPoint a, PdfPoint b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static TextEntity? ToText(TextLine line, PageTransform transform)
    {
        var text = line.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var first = line.First;
        var insert = transform.Map(first.StartBaseLine.X, first.StartBaseLine.Y);
        var height = first.PointSize * CapHeightRatio * transform.UnitsPerPoint;
        if (height <= 0 || double.IsNaN(line.Angle))
        {
            return null;
        }

        return new TextEntity(text)
        {
            InsertPoint = new XYZ(insert.X, insert.Y, 0),
            Height = height,
            Rotation = line.Angle,
            Color = CadColors.FromPdf(first.RenderingMode is TextRenderingMode.Stroke or TextRenderingMode.StrokeClip ? first.StrokeColor : first.FillColor),
        };
    }

    private async Task<List<Entity>> TraceScanAsync(string path, Page page, CadOptions options, CancellationToken ct)
    {
        if (rasterizer is null)
        {
            throw new NotSupportedException("Tracing a scanned page needs a page rasterizer.");
        }

        var image = await rasterizer.RenderAsync(path, page.Number, ScanPixelsPerPoint, ct).ConfigureAwait(false);
        var unitsPerPixel = options.UnitsPerPoint / ScanPixelsPerPoint;
        var entities = new List<Entity>();
        foreach (var outline in ScanVectorizer.Trace(image, ScanTolerancePixels, ct))
        {
            // Pixel rows run down the page as shown; drawing Y runs up.
            var points = outline.Select(p => (IVector)new XY(p.X * unitsPerPixel, (image.Height - p.Y) * unitsPerPixel));
            entities.Add(new LwPolyline(points) { IsClosed = true });
        }

        return entities;
    }

    /// <summary>
    /// Turns subpaths into entities in drawing units. Straight runs are collected first and joined across
    /// paths at the end, because producers paint connected outlines one segment at a time.
    /// </summary>
    private sealed class EntityBuilder(PageTransform transform, bool joinLines)
    {
        private readonly List<Entity> _entities = [];
        private readonly PolylineJoiner _joiner = new(JoinTolerancePoints * transform.UnitsPerPoint);
        private readonly List<Color> _groups = [];
        private readonly double _near = JoinTolerancePoints * transform.UnitsPerPoint;

        private abstract record Segment;

        private sealed record StraightSegment(XY From, XY To) : Segment;

        private sealed record ArcSegment(ArcFit Arc) : Segment;

        private sealed record BezierSegment(XY P0, XY P1, XY P2, XY P3) : Segment;

        public void AddSegment(XY from, XY to, Rect clip, Color color)
        {
            if (CadGeometry.ClipSegment(from, to, clip) is { } visible && !Near(visible.From, visible.To))
            {
                AddRun([visible.From, visible.To], color);
            }
        }

        /// <summary>A closed outline. Whole when fully visible; otherwise its visible edges as open runs.</summary>
        public void AddPolygon(List<XY> ring, Rect clip, Color color)
        {
            if (ring.Count < 3)
            {
                return;
            }

            if (joinLines && ring.All(clip.Contains))
            {
                _entities.Add(new LwPolyline(ring.Select(p => (IVector)p)) { IsClosed = true, Color = color });
                return;
            }

            for (var i = 0; i < ring.Count; i++)
            {
                AddSegment(ring[i], ring[(i + 1) % ring.Count], clip, color);
            }
        }

        public void AddSubpath(PdfSubpath subpath, Rect clip, Color color)
        {
            // Collect the segments first; a Close adds the segment back to the start of the subpath.
            var segments = new List<Segment>();
            XY? start = null;
            XY? current = null;
            var closed = false;
            foreach (var command in subpath.Commands)
            {
                switch (command)
                {
                    case PdfSubpath.Move move:
                        start = current = transform.Map(move.Location.X, move.Location.Y);
                        break;
                    case PdfSubpath.Line line:
                        var to = transform.Map(line.To.X, line.To.Y);
                        segments.Add(new StraightSegment(transform.Map(line.From.X, line.From.Y), to));
                        start ??= segments[^1] is StraightSegment s ? s.From : null;
                        current = to;
                        break;
                    case PdfSubpath.CubicBezierCurve bezier:
                        var p0 = transform.Map(bezier.StartPoint.X, bezier.StartPoint.Y);
                        var p1 = transform.Map(bezier.FirstControlPoint.X, bezier.FirstControlPoint.Y);
                        var p2 = transform.Map(bezier.SecondControlPoint.X, bezier.SecondControlPoint.Y);
                        var p3 = transform.Map(bezier.EndPoint.X, bezier.EndPoint.Y);
                        start ??= p0;
                        segments.Add(
                            CadGeometry.IsFlat(p0, p1, p2, p3) ? new StraightSegment(p0, p3)
                            : CadGeometry.FitArc(p0, p1, p2, p3) is { } arc ? new ArcSegment(arc)
                            : new BezierSegment(p0, p1, p2, p3));
                        current = p3;
                        break;
                    case PdfSubpath.Close:
                        if (start is { } first && current is { } last && !Near(first, last))
                        {
                            segments.Add(new StraightSegment(last, first));
                        }

                        closed = true;
                        current = start;
                        break;
                }
            }

            segments.RemoveAll(segment => segment is StraightSegment s && Near(s.From, s.To));
            if (segments.Count == 0)
            {
                return;
            }

            // A path that ends where it began is closed even without an explicit Close.
            closed |= start is { } s0 && current is { } c0 && Near(s0, c0);

            // A circle is drawn as four or more Bézier arcs around one center. Rounded PDF coordinates move each
            // arc's center a little, so they are compared with a tolerance and written as one CIRCLE.
            if (closed && segments.Count >= 2 && segments.All(segment => segment is ArcSegment)
                && CadGeometry.MergeCircle([.. segments.Cast<ArcSegment>().Select(a => a.Arc)]) is { } circle)
            {
                var bounds = new Rect(circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius, circle.Center.X + circle.Radius, circle.Center.Y + circle.Radius);
                if (clip.Intersects(bounds))
                {
                    _entities.Add(new Circle { Center = new XYZ(circle.Center.X, circle.Center.Y, 0), Radius = circle.Radius, Color = color });
                }

                return;
            }

            segments = MergeArcs(segments);

            // A subpath of straight segments only that returns to its start is one closed polyline, as long
            // as all of it is visible; a partly clipped outline is cut into open pieces below instead.
            if (joinLines && closed && segments.Count >= 3 && segments.All(segment => segment is StraightSegment))
            {
                var corners = segments.Cast<StraightSegment>().Select(s => s.From).ToList();
                if (corners.All(clip.Contains))
                {
                    _entities.Add(new LwPolyline(corners.Select(p => (IVector)p)) { IsClosed = true, Color = color });
                    return;
                }
            }

            var run = new List<XY>();
            var curve = new List<BezierSegment>();
            void FlushRun()
            {
                AddRun(run, color);
                run = [];
            }

            void FlushCurve()
            {
                if (curve.Count > 0)
                {
                    var spline = ToSpline(curve);
                    if (clip.Intersects(Rect.Around(spline.ControlPoints.Select(p => new XY(p.X, p.Y)))))
                    {
                        spline.Color = color;
                        _entities.Add(spline);
                    }
                }

                curve.Clear();
            }

            foreach (var segment in segments)
            {
                switch (segment)
                {
                    case StraightSegment s:
                        FlushCurve();
                        if (CadGeometry.ClipSegment(s.From, s.To, clip) is not { } visible || Near(visible.From, visible.To))
                        {
                            FlushRun();
                            break;
                        }

                        if (run.Count > 0 && !Near(run[^1], visible.From))
                        {
                            FlushRun();
                        }

                        if (run.Count == 0)
                        {
                            run.Add(visible.From);
                        }

                        run.Add(visible.To);
                        break;
                    case ArcSegment a:
                        FlushRun();
                        FlushCurve();
                        if (clip.Intersects(a.Arc.Bounds()))
                        {
                            _entities.Add(new Arc
                            {
                                Center = new XYZ(a.Arc.Center.X, a.Arc.Center.Y, 0),
                                Radius = a.Arc.Radius,
                                StartAngle = a.Arc.StartAngle,
                                EndAngle = a.Arc.EndAngle,
                                Color = color,
                            });
                        }

                        break;
                    case BezierSegment c:
                        FlushRun();
                        if (curve.Count > 0 && !Near(curve[^1].P3, c.P0))
                        {
                            FlushCurve();
                        }

                        curve.Add(c);
                        break;
                }
            }

            FlushRun();
            FlushCurve();
        }

        /// <summary>The entities so far, with straight runs joined into polylines. Two-point runs become LINEs.</summary>
        public List<Entity> Finish()
        {
            foreach (var (points, group, closed) in _joiner.Join())
            {
                var color = _groups[group];
                if (points.Count == 2 && !closed)
                {
                    _entities.Add(new Line(new XYZ(points[0].X, points[0].Y, 0), new XYZ(points[1].X, points[1].Y, 0)) { Color = color });
                }
                else
                {
                    _entities.Add(new LwPolyline(points.Select(p => (IVector)p)) { IsClosed = closed, Color = color });
                }
            }

            return _entities;
        }

        private void AddRun(List<XY> points, Color color)
        {
            if (points.Count < 2)
            {
                return;
            }

            if (joinLines)
            {
                _joiner.Add(points, GroupOf(color));
                return;
            }

            for (var i = 1; i < points.Count; i++)
            {
                _entities.Add(new Line(new XYZ(points[i - 1].X, points[i - 1].Y, 0), new XYZ(points[i].X, points[i].Y, 0)) { Color = color });
            }
        }

        private int GroupOf(Color color)
        {
            var index = _groups.IndexOf(color);
            if (index < 0)
            {
                _groups.Add(color);
                index = _groups.Count - 1;
            }

            return index;
        }

        /// <summary>Joins neighbouring arcs of the same circle into one arc.</summary>
        private static List<Segment> MergeArcs(List<Segment> segments)
        {
            var merged = new List<Segment>();
            foreach (var segment in segments)
            {
                if (segment is ArcSegment next && merged.Count > 0 && merged[^1] is ArcSegment previous
                    && CadGeometry.JoinArcs(previous.Arc, next.Arc) is { } joined)
                {
                    merged[^1] = new ArcSegment(joined);
                }
                else
                {
                    merged.Add(segment);
                }
            }

            return merged;
        }

        /// <summary>
        /// One cubic B-spline for a chain of Béziers that follow on from each other. Each Bézier keeps its
        /// control points; the knots are clamped at the ends and tripled between pieces, which reproduces the
        /// curve exactly.
        /// </summary>
        private static Spline ToSpline(List<BezierSegment> chain)
        {
            var spline = new Spline { Degree = 3, Flags = SplineFlags.Planar };
            spline.ControlPoints.Add(new XYZ(chain[0].P0.X, chain[0].P0.Y, 0));
            foreach (var piece in chain)
            {
                spline.ControlPoints.Add(new XYZ(piece.P1.X, piece.P1.Y, 0));
                spline.ControlPoints.Add(new XYZ(piece.P2.X, piece.P2.Y, 0));
                spline.ControlPoints.Add(new XYZ(piece.P3.X, piece.P3.Y, 0));
            }

            for (var k = 0; k < 4; k++)
            {
                spline.Knots.Add(0);
            }

            for (var i = 1; i < chain.Count; i++)
            {
                for (var k = 0; k < 3; k++)
                {
                    spline.Knots.Add(i);
                }
            }

            for (var k = 0; k < 4; k++)
            {
                spline.Knots.Add(chain.Count);
            }

            return spline;
        }

        private bool Near(XY a, XY b) => CadGeometry.Distance(a, b) <= _near;
    }
}

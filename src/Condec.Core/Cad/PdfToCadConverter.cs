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

namespace Condec.Core.Cad;

/// <summary>The chosen PDF page has nothing that can become a drawing: no lines, curves, text or scanned ink.</summary>
public sealed class NothingToConvertException(string message) : Exception(message);

/// <summary>
/// One PDF page to a DXF or DWG drawing. Vector pages keep their geometry: lines become LINE or LWPOLYLINE,
/// Béziers that trace a circle become ARC and the rest SPLINE, words become TEXT. Scanned pages are
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

        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));
        ct.ThrowIfCancellationRequested();
        progress.Report(new ConversionProgress(ConversionStage.Encode, 0, $"Menulis {entityCount} objek CAD"));

        var bytes = Write(cad, target);
        await request.Output.WriteAsync(bytes, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }

    /// <summary>ACadSharp writers dispose the stream they write to, so they write to memory first.</summary>
    internal static byte[] Write(CadDocument cad, string target)
    {
        using var buffer = new MemoryStream();
        if (target == ".dwg")
        {
            using var writer = new DwgWriter(buffer, cad);
            writer.Write();
        }
        else
        {
            using var writer = new DxfWriter(buffer, cad, false);
            writer.Write();
        }

        return buffer.ToArray();
    }

    internal static List<Entity> ReadVectors(Page page, CadOptions options, CancellationToken ct)
    {
        var box = page.CropBox.Bounds;
        var transform = new PageTransform(box.Left, box.Bottom, box.Width, box.Height, page.Rotation.Value, options.UnitsPerPoint);
        var entities = new List<Entity>();

        // Producers often paint a shape twice, once to fill it and once to stroke its outline, and paint the
        // page background as a filled rectangle. Neither belongs in a drawing.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pageArea = box.Width * box.Height;
        foreach (var path in page.Paths)
        {
            ct.ThrowIfCancellationRequested();
            if (path.IsClipping || !(path.IsStroked || path.IsFilled))
            {
                continue;
            }

            if (!path.IsStroked && path.GetBoundingRectangle() is { } bounds && bounds.Width * bounds.Height >= BackgroundCoverage * pageArea)
            {
                continue;
            }

            if (!seen.Add(Signature(path)))
            {
                continue;
            }

            foreach (var subpath in path)
            {
                AddSubpath(subpath, transform, options.JoinLines, entities);
            }
        }

        if (options.KeepText)
        {
            foreach (var word in page.GetWords())
            {
                if (ToText(word, transform) is { } text)
                {
                    entities.Add(text);
                }
            }
        }

        return entities;
    }

    /// <summary>The geometry of a path, rounded to 1/100 pt, to recognize the same shape painted twice.</summary>
    private static string Signature(UglyToad.PdfPig.Graphics.PdfPath path)
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

    private abstract record Segment;

    private sealed record StraightSegment(XY From, XY To) : Segment;

    private sealed record ArcSegment(ArcFit Arc) : Segment;

    private sealed record SplineSegment(XY P0, XY P1, XY P2, XY P3) : Segment;

    private static void AddSubpath(PdfSubpath subpath, PageTransform transform, bool joinLines, List<Entity> entities)
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
                    current = to;
                    break;
                case PdfSubpath.CubicBezierCurve curve:
                    var p0 = transform.Map(curve.StartPoint.X, curve.StartPoint.Y);
                    var p1 = transform.Map(curve.FirstControlPoint.X, curve.FirstControlPoint.Y);
                    var p2 = transform.Map(curve.SecondControlPoint.X, curve.SecondControlPoint.Y);
                    var p3 = transform.Map(curve.EndPoint.X, curve.EndPoint.Y);
                    segments.Add(CadGeometry.FitArc(p0, p1, p2, p3) is { } arc ? new ArcSegment(arc) : new SplineSegment(p0, p1, p2, p3));
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

        // A circle is drawn as four or more Bézier arcs around one center. Rounded PDF coordinates move each
        // arc's center a little, so they are compared with a tolerance and written as one CIRCLE.
        if (closed && segments.Count >= 2 && segments.All(segment => segment is ArcSegment)
            && CadGeometry.MergeCircle([.. segments.Cast<ArcSegment>().Select(a => a.Arc)]) is { } circle)
        {
            entities.Add(new Circle { Center = new XYZ(circle.Center.X, circle.Center.Y, 0), Radius = circle.Radius });
            return;
        }

        segments = MergeArcs(segments);

        // A subpath of straight segments only that returns to its start is one closed polyline.
        if (joinLines && closed && segments.Count >= 3 && segments.All(segment => segment is StraightSegment))
        {
            var corners = segments.Cast<StraightSegment>().Select(s => (IVector)s.From);
            entities.Add(new LwPolyline(corners) { IsClosed = true });
            return;
        }

        var run = new List<XY>();
        void FlushRun()
        {
            if (run.Count >= 2)
            {
                entities.Add(new LwPolyline(run.Select(p => (IVector)p)));
            }

            run.Clear();
        }

        foreach (var segment in segments)
        {
            switch (segment)
            {
                case StraightSegment s when joinLines:
                    if (run.Count > 0 && !Near(run[^1], s.From))
                    {
                        FlushRun();
                    }

                    if (run.Count == 0)
                    {
                        run.Add(s.From);
                    }

                    run.Add(s.To);
                    break;
                case StraightSegment s:
                    entities.Add(new Line(new XYZ(s.From.X, s.From.Y, 0), new XYZ(s.To.X, s.To.Y, 0)));
                    break;
                case ArcSegment a:
                    FlushRun();
                    entities.Add(new Arc
                    {
                        Center = new XYZ(a.Arc.Center.X, a.Arc.Center.Y, 0),
                        Radius = a.Arc.Radius,
                        StartAngle = a.Arc.StartAngle,
                        EndAngle = a.Arc.EndAngle,
                    });
                    break;
                case SplineSegment c:
                    FlushRun();
                    entities.Add(ToSpline(c.P0, c.P1, c.P2, c.P3));
                    break;
            }
        }

        FlushRun();
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

    private static Spline ToSpline(XY p0, XY p1, XY p2, XY p3)
    {
        var spline = new Spline { Degree = 3 };
        foreach (var p in (XY[])[p0, p1, p2, p3])
        {
            spline.ControlPoints.Add(new XYZ(p.X, p.Y, 0));
        }

        // A single cubic Bézier is a clamped B-spline with this knot vector.
        foreach (var knot in (double[])[0, 0, 0, 0, 1, 1, 1, 1])
        {
            spline.Knots.Add(knot);
        }

        return spline;
    }
    private static TextEntity? ToText(Word word, PageTransform transform)
    {
        if (string.IsNullOrWhiteSpace(word.Text) || word.Letters.Count == 0)
        {
            return null;
        }

        var first = word.Letters[0];
        var last = word.Letters[^1];
        var angle = word.Letters.Count > 1
            ? Math.Atan2(last.StartBaseLine.Y - first.StartBaseLine.Y, last.StartBaseLine.X - first.StartBaseLine.X)
            : 0;
        var insert = transform.Map(first.StartBaseLine.X, first.StartBaseLine.Y);
        var height = first.PointSize * CapHeightRatio * transform.UnitsPerPoint;
        if (height <= 0)
        {
            return null;
        }

        return new TextEntity(word.Text)
        {
            InsertPoint = new XYZ(insert.X, insert.Y, 0),
            Height = height,
            Rotation = transform.MapAngle(angle),
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

    private static bool Near(XY a, XY b) => CadGeometry.Distance(a, b) < 1e-9;
}

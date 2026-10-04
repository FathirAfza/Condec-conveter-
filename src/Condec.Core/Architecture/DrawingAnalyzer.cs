// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;

namespace Condec.Core.Architecture;

/// <summary>
/// Looks at a picture of a drawing and sorts what is in it (DESIGN §6.3.1): logos, tables, writing, doors and windows, and
/// the rest of the line-work. Everything runs on this device, on a copy averaged down to <see cref="MaximumSide"/> pixels.
/// </summary>
public static class DrawingAnalyzer
{
    /// <summary>The longest side the analysis works at; a larger picture is averaged down first.</summary>
    public const int MaximumSide = 2400;

    /// <summary>Lines of writing handed to the recognizer; the rest are drawn as line-work.</summary>
    public const int MaximumRecognizedLines = 500;

    /// <summary>How far, in pixels, a traced shape may stray from the ink.</summary>
    public const double TraceTolerance = 1.5;

    /// <summary>An object is unclear when at least this share of the squares under it is.</summary>
    public const double UnclearObjectShare = 0.5;

    public static async Task<DrawingAnalysis> AnalyzeAsync(RasterPicture file, ITextRecognizer? recognizer, IProgress<double>? progress, CancellationToken ct)
    {
        var (picture, reduction) = file.ReduceTo(MaximumSide);
        progress?.Report(0.03);

        var gray = picture.ToGray();
        var threshold = ScanVectorizer.OtsuThreshold(gray.Pixels);
        var ink = BitMask.Ink(gray, threshold);
        var height = picture.Height;
        progress?.Report(0.06);

        // Logos first: their ink is theirs, whatever it looks like. Each becomes areas of its own colors, read from the file.
        var logoRects = LogoFinder.Find(picture, ct);
        var logoShapes = new List<IReadOnlyList<DrawingPrimitive>>();
        foreach (var rect in logoRects)
        {
            ct.ThrowIfCancellationRequested();
            logoShapes.Add(LogoPainter.Paint(file, reduction, threshold, rect, height, ct));
            ink.Clear(rect);
        }

        progress?.Report(0.12);

        // Ruled grids: a table when it has rows of cells with writing, a title block box when a logo sits in a cell.
        var grids = TableFinder.Find(ink, ct);
        var blobs = ConnectedComponents.Label(ink, out var labels);
        var claimed = new HashSet<int>();
        var tableGrids = new List<(RuledGrid Grid, bool IsKop, List<int> Logos, List<Blob> Own)>();
        foreach (var grid in grids)
        {
            ct.ThrowIfCancellationRequested();
            var logosHere = Enumerable.Range(0, logoRects.Count)
                .Where(i => grid.Cells.Any(c => c.Contains(logoRects[i].Left + (logoRects[i].Width / 2.0), logoRects[i].Top + (logoRects[i].Height / 2.0))))
                .ToList();
            var isTable = grid.IsTable;
            if (!isTable && logosHere.Count == 0)
            {
                continue;
            }

            // The ink blobs the ruling is part of are the grid's own.
            var own = blobs.Where(blob => !claimed.Contains(blob.Id) && Touches(grid, blob, labels, ink.Width)).ToList();
            foreach (var blob in own)
            {
                claimed.Add(blob.Id);
            }

            tableGrids.Add((grid, !isTable, isTable ? [] : logosHere, own));
        }

        progress?.Report(0.2);

        // Writing: rows of small marks outside the grids. Read when a recognizer is there, otherwise kept as line-work.
        var textLines = TextFinder.Find(blobs, ink.Width, ink.Height, b => !claimed.Contains(b.Id), ct);
        var freeTexts = new List<DrawingItem>();
        var gridTexts = tableGrids.Select(_ => new List<(TextLineItem Text, IReadOnlyList<DrawingPrimitive> Glyphs)>()).ToList();
        var textBlobIds = new HashSet<int>();
        var textRead = false;
        var recognized = 0;
        foreach (var line in textLines.OrderByDescending(l => l.Bounds.Area))
        {
            ct.ThrowIfCancellationRequested();
            string? said = null;
            if (recognizer is not null && recognized < MaximumRecognizedLines)
            {
                recognized++;
                said = await ReadAsync(picture, line.Bounds, recognizer, ct).ConfigureAwait(false);
            }

            IReadOnlyList<DrawingPrimitive> glyphs = [];
            if (said is null)
            {
                var ids = line.Marks.Select(m => m.Id).ToHashSet();
                glyphs = LineWorkExtractor.Extract(ink, labels, line.Marks, b => ids.Contains(b.Id), TraceTolerance, ct)
                    .SelectMany(b => b.Primitives)
                    .ToList();
            }
            else
            {
                textRead = true;
            }

            foreach (var mark in line.Marks)
            {
                textBlobIds.Add(mark.Id);
            }

            var item = new TextLineItem(said, line.Bounds);
            var centerX = line.Bounds.Left + (line.Bounds.Width / 2.0);
            var centerY = line.Bounds.Top + (line.Bounds.Height / 2.0);
            var home = tableGrids.FindIndex(t => t.Grid.Cells.Any(c => c.Contains(centerX, centerY) && (t.IsKop || c.Height <= TableFinder.SmallCellShare * ink.Height)));
            if (home >= 0)
            {
                gridTexts[home].Add((item, glyphs));
            }
            else
            {
                freeTexts.Add(new DrawingItem(line.Bounds, glyphs, [item]));
            }
        }

        progress?.Report(0.4);

        // The grids' line-work, then the rest of the line-work.
        var tables = new List<DrawingItem>();
        var logos = new List<DrawingItem>();
        var folded = new HashSet<int>();
        for (var i = 0; i < tableGrids.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (grid, isKop, logosHere, own) = tableGrids[i];
            var shapes = LineWorkExtractor.Extract(ink, labels, own, _ => true, TraceTolerance, ct).SelectMany(b => b.Primitives).ToList();
            shapes.AddRange(gridTexts[i].SelectMany(t => t.Glyphs));
            var bounds = grid.Bounds;
            foreach (var index in logosHere)
            {
                shapes.AddRange(logoShapes[index]);
                folded.Add(index);
                bounds = bounds.Union(logoRects[index]);
            }

            var item = new DrawingItem(bounds, shapes, gridTexts[i].Select(t => t.Text).ToList());
            (isKop ? logos : tables).Add(item);
        }

        for (var index = 0; index < logoRects.Count; index++)
        {
            if (!folded.Contains(index))
            {
                logos.Add(DrawingItem.OfShapes(logoRects[index], logoShapes[index]));
            }
        }

        var lineWork = LineWorkExtractor.Extract(ink, labels, blobs, b => !textBlobIds.Contains(b.Id) && !claimed.Contains(b.Id), TraceTolerance, ct);
        progress?.Report(0.8);

        var all = lineWork.SelectMany(b => b.Primitives).ToList();
        var split = OpeningFinder.Find(all, picture.Width, height, TraceTolerance);

        // What is left goes back to the ink it came from, so every wall item keeps the outline of its own blob.
        var origin = new Dictionary<DrawingPrimitive, Blob>(ReferenceEqualityComparer.Instance);
        foreach (var blob in lineWork)
        {
            foreach (var primitive in blob.Primitives)
            {
                origin[primitive] = blob.Blob;
            }
        }

        var walls = split.Remaining
            .GroupBy(piece => origin[piece.Source].Id)
            .Select(group => DrawingItem.OfShapes(origin[group.First().Source].Bounds, group.Select(piece => piece.Shape).ToList()))
            .ToList();

        var openings = split.Openings.Select(shapes => DrawingItem.OfShapes(DrawingGeometry.Bounds(shapes, height), shapes)).ToList();
        progress?.Report(0.9);

        var clarity = ClarityAnalyzer.Analyze(gray, logoRects.Select(r => r.Grow(ClarityAnalyzer.CellSize)).ToList());
        var groups = new List<DrawingGroup>
        {
            new(DrawingObjectKind.Walls, walls),
            new(DrawingObjectKind.Openings, openings),
            new(DrawingObjectKind.Text, freeTexts),
            new(DrawingObjectKind.Logo, logos),
            new(DrawingObjectKind.Table, tables),
        };

        var analysis = new DrawingAnalysis(picture.Width, height, reduction, picture.DpiX, picture.DpiY, groups, clarity.Areas, ClearShare(groups, clarity, height), textRead);
        progress?.Report(1);
        return analysis;
    }

    /// <summary>Whether the blob has a pixel under the grid's ruling.</summary>
    private static bool Touches(RuledGrid grid, Blob blob, int[] labels, int pictureWidth)
    {
        var area = blob.Bounds;
        var overlap = new PixelRect(Math.Max(area.Left, grid.RulingBounds.Left), Math.Max(area.Top, grid.RulingBounds.Top), Math.Min(area.Right, grid.RulingBounds.Right), Math.Min(area.Bottom, grid.RulingBounds.Bottom));
        for (var y = overlap.Top; y < overlap.Bottom; y++)
        {
            for (var x = overlap.Left; x < overlap.Right; x++)
            {
                if (labels[(y * pictureWidth) + x] == blob.Id && grid.Rules(x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Hands the recognizer a copy of the line, with a margin so letters at its edge are whole.</summary>
    private static async Task<string?> ReadAsync(RasterPicture picture, PixelRect bounds, ITextRecognizer recognizer, CancellationToken ct)
    {
        var crop = Crop(picture, bounds.Grow(Math.Max(3, bounds.Height / 4)).Clamp(picture.Width, picture.Height));
        if (crop is null)
        {
            return null;
        }

        var text = await recognizer.RecognizeAsync(crop, ct).ConfigureAwait(false);
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    internal static RasterPicture? Crop(RasterPicture picture, PixelRect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        var pixels = new byte[area.Width * area.Height * 4];
        for (var y = 0; y < area.Height; y++)
        {
            Buffer.BlockCopy(picture.Bgra, ((((area.Top + y) * picture.Width) + area.Left) * 4), pixels, y * area.Width * 4, area.Width * 4);
        }

        return new RasterPicture(area.Width, area.Height, pixels, picture.DpiX, picture.DpiY);
    }

    /// <summary>The share of objects (each wall line, and each other item) that do not lie in an unclear place.</summary>
    private static double ClearShare(IReadOnlyList<DrawingGroup> groups, ClarityMap clarity, int height)
    {
        var total = 0;
        var unclear = 0;
        foreach (var group in groups)
        {
            foreach (var item in group.Items)
            {
                if (group.Kind == DrawingObjectKind.Walls)
                {
                    foreach (var primitive in item.Primitives)
                    {
                        total++;
                        if (clarity.UnclearShare(DrawingGeometry.Bounds([primitive], height)) >= UnclearObjectShare)
                        {
                            unclear++;
                        }
                    }

                    continue;
                }

                total++;
                if (clarity.UnclearShare(item.Bounds) >= UnclearObjectShare)
                {
                    unclear++;
                }
            }
        }

        return total == 0 ? 1 : 1 - ((double)unclear / total);
    }
}

/// <summary>Where shapes lie in picture pixels.</summary>
public static class DrawingGeometry
{
    /// <summary>The smallest rectangle (y down) that holds the shapes, which are in picture pixels with y up.</summary>
    public static PixelRect Bounds(IEnumerable<DrawingPrimitive> primitives, int pictureHeight)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Take(double x, double y)
        {
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        foreach (var primitive in primitives)
        {
            switch (primitive)
            {
                case PolylinePrimitive polyline:
                    foreach (var (x, y) in polyline.Points)
                    {
                        Take(x, y);
                    }

                    break;
                case ArcPrimitive arc:
                    // The ends and any of the four extremes the arc passes through.
                    Take(arc.CenterX + (arc.Radius * Math.Cos(arc.StartAngle)), arc.CenterY + (arc.Radius * Math.Sin(arc.StartAngle)));
                    Take(arc.CenterX + (arc.Radius * Math.Cos(arc.EndAngle)), arc.CenterY + (arc.Radius * Math.Sin(arc.EndAngle)));
                    for (var quarter = 0; quarter < 4; quarter++)
                    {
                        var angle = quarter * Math.PI / 2;
                        var into = angle - arc.StartAngle;
                        while (into < 0)
                        {
                            into += 2 * Math.PI;
                        }

                        if (into <= arc.Sweep)
                        {
                            Take(arc.CenterX + (arc.Radius * Math.Cos(angle)), arc.CenterY + (arc.Radius * Math.Sin(angle)));
                        }
                    }

                    break;
                case CirclePrimitive circle:
                    Take(circle.CenterX - circle.Radius, circle.CenterY - circle.Radius);
                    Take(circle.CenterX + circle.Radius, circle.CenterY + circle.Radius);
                    break;
                case FillPrimitive fill:
                    foreach (var (x, y) in fill.Loops.SelectMany(loop => loop))
                    {
                        Take(x, y);
                    }

                    break;
            }
        }

        if (maxX < minX)
        {
            return default;
        }

        return new PixelRect((int)Math.Floor(minX), (int)Math.Floor(pictureHeight - maxY), (int)Math.Ceiling(maxX), (int)Math.Ceiling(pictureHeight - minY));
    }

    /// <summary>
    /// A shape found in a piece of the picture, moved to where it lies in the picture. Shapes are y up in each: the piece's
    /// height and the picture's differ, so a row of the piece is the picture's row plus the piece's top.
    /// </summary>
    public static DrawingPrimitive Move(DrawingPrimitive primitive, int left, int top, int pieceHeight, int pictureHeight)
    {
        // y up in the piece -> row in the piece -> row in the picture -> y up in the picture.
        double Y(double y) => pictureHeight - (top + (pieceHeight - y));

        return primitive switch
        {
            PolylinePrimitive p => new PolylinePrimitive(p.Points.Select(q => (q.X + left, Y(q.Y))).ToList(), p.IsClosed),
            ArcPrimitive a => new ArcPrimitive(a.CenterX + left, Y(a.CenterY), a.Radius, a.StartAngle, a.EndAngle),
            CirclePrimitive c => new CirclePrimitive(c.CenterX + left, Y(c.CenterY), c.Radius),
            FillPrimitive f => new FillPrimitive(f.Loops.Select(loop => (IReadOnlyList<(double X, double Y)>)loop.Select(q => (q.X + left, Y(q.Y))).ToList()).ToList(), f.Color),
            _ => primitive,
        };
    }
}

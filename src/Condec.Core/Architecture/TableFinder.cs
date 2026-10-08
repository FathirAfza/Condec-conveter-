// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Architecture;

/// <summary>A ruled grid: the lines that cross to make closed cells.</summary>
/// <param name="Bounds">The grid's rectangle, ruling included.</param>
/// <param name="Cells">The closed cells, each a rectangle inside the ruling.</param>
/// <param name="SmallCells">How many of the cells are as low as a table's row is.</param>
/// <param name="FilledSmallCells">How many of those have something in them (writing).</param>
/// <param name="Ruling">The ruling's own pixels, as a picture of <paramref name="RulingBounds"/>: which ink blobs belong to the grid.</param>
public sealed record RuledGrid(PixelRect Bounds, IReadOnlyList<PixelRect> Cells, int SmallCells, int FilledSmallCells, BitMask Ruling, PixelRect RulingBounds)
{
    /// <summary>Whether the pixel is part of this grid's ruling.</summary>
    public bool Rules(int x, int y) => RulingBounds.Contains(x, y) && Ruling[x - RulingBounds.Left, y - RulingBounds.Top];

    /// <summary>A grid of at least four low cells, a third of them written in, is a table; a few big cells are rooms or boxes.</summary>
    public bool IsTable => SmallCells >= TableFinder.MinimumSmallCells && FilledSmallCells * 3 >= SmallCells;
}

/// <summary>
/// Finds ruled grids: long horizontal and vertical lines that cross to close cells, the way a title block or a schedule is
/// ruled. The sheet's own frame (a ring of lines most of the way across the picture) is taken off first, so what is ruled
/// inside it can be told apart. `[ASUMSI]`: no floor-plan sample was available to tune the limits.
/// </summary>
public static class TableFinder
{
    /// <summary>Lines shorter than this share of the shorter side are not ruling.</summary>
    public const double MinimumLineShare = 0.04;

    /// <summary>A grid at least this share of the picture on both sides is the sheet's frame.</summary>
    public const double FrameShare = 0.6;

    /// <summary>A cell is low, as a table's row is, when it is no taller than this share of the picture's height.</summary>
    public const double SmallCellShare = 0.045;

    /// <summary>A cell narrower or lower than this, in pixels, is a gap between two lines, not a cell.</summary>
    public const int MinimumCellSide = 8;

    public const int MinimumSmallCells = 4;

    public static List<RuledGrid> Find(BitMask ink, CancellationToken ct)
    {
        var shorter = Math.Min(ink.Width, ink.Height);
        var minimum = Math.Max(24, (int)(shorter * MinimumLineShare));
        var horizontal = ink.LongHorizontalRuns(minimum);
        var vertical = ink.LongVerticalRuns(minimum);

        // The frame: while a ruled grid spans the picture, take its outermost four lines off.
        for (var pass = 0; pass < 3; pass++)
        {
            var grid = horizontal.Or(vertical).Dilate(1);
            var spanning = ConnectedComponents.Label(grid, out _)
                .Where(b => b.Width >= FrameShare * ink.Width && b.Height >= FrameShare * ink.Height)
                .ToList();
            if (spanning.Count == 0)
            {
                break;
            }

            foreach (var frame in spanning)
            {
                StripOutermost(horizontal, vertical, frame.Bounds);
            }
        }

        var ruling = horizontal.Or(vertical).Dilate(1);
        var grids = new List<RuledGrid>();
        foreach (var blob in ConnectedComponents.Label(ruling, out var rulingLabels))
        {
            ct.ThrowIfCancellationRequested();
            if (blob.Width < minimum || blob.Height < MinimumCellSide * 2)
            {
                continue;
            }

            var cells = FindCells(ruling, blob.Bounds);
            if (cells.Count == 0)
            {
                continue;
            }

            var small = cells.Where(c => c.Height <= SmallCellShare * ink.Height).ToList();
            var filled = small.Count(c => HasInk(ink, ruling, c));

            // This grid's own ruling pixels (another grid may lie inside its rectangle).
            var own = new BitMask(blob.Width, blob.Height);
            for (var y = 0; y < blob.Height; y++)
            {
                for (var x = 0; x < blob.Width; x++)
                {
                    own.Bits[(y * blob.Width) + x] = rulingLabels[((blob.MinY + y) * ink.Width) + blob.MinX + x] == blob.Id;
                }
            }

            grids.Add(new RuledGrid(blob.Bounds.Grow(1).Clamp(ink.Width, ink.Height), cells, small.Count, filled, own, blob.Bounds));
        }

        return grids;
    }

    /// <summary>The closed white areas inside the ruling of <paramref name="bounds"/>: the ones that don't reach its edge.</summary>
    private static List<PixelRect> FindCells(BitMask ruling, PixelRect bounds)
    {
        var area = bounds.Clamp(ruling.Width, ruling.Height);
        var open = new BitMask(area.Width, area.Height);
        for (var y = 0; y < area.Height; y++)
        {
            for (var x = 0; x < area.Width; x++)
            {
                open.Bits[(y * area.Width) + x] = !ruling.Bits[((area.Top + y) * ruling.Width) + area.Left + x];
            }
        }

        var cells = new List<PixelRect>();
        foreach (var blob in ConnectedComponents.Label(open, out _))
        {
            // A white area that touches the edge of the grid is outside it (or leaks out through a gap).
            if (blob.MinX == 0 || blob.MinY == 0 || blob.MaxX == area.Width - 1 || blob.MaxY == area.Height - 1)
            {
                continue;
            }

            if (blob.Width < MinimumCellSide || blob.Height < MinimumCellSide)
            {
                continue;
            }

            cells.Add(new PixelRect(area.Left + blob.MinX, area.Top + blob.MinY, area.Left + blob.MaxX + 1, area.Top + blob.MaxY + 1));
        }

        return cells;
    }

    /// <summary>Whether there is ink in the cell that is not ruling.</summary>
    private static bool HasInk(BitMask ink, BitMask ruling, PixelRect cell)
    {
        for (var y = cell.Top; y < cell.Bottom; y++)
        {
            for (var x = cell.Left; x < cell.Right; x++)
            {
                var at = (y * ink.Width) + x;
                if (ink.Bits[at] && !ruling.Bits[at])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Clears the topmost and bottommost horizontal lines and the leftmost and rightmost vertical lines inside <paramref name="bounds"/>.</summary>
    private static void StripOutermost(BitMask horizontal, BitMask vertical, PixelRect bounds)
    {
        var area = bounds.Clamp(horizontal.Width, horizontal.Height);
        ClearEdgeRows(horizontal, area, fromTop: true);
        ClearEdgeRows(horizontal, area, fromTop: false);
        ClearEdgeColumns(vertical, area, fromLeft: true);
        ClearEdgeColumns(vertical, area, fromLeft: false);
    }

    private static void ClearEdgeRows(BitMask mask, PixelRect area, bool fromTop)
    {
        var started = false;
        for (var i = 0; i < area.Height; i++)
        {
            var y = fromTop ? area.Top + i : area.Bottom - 1 - i;
            var any = false;
            for (var x = area.Left; x < area.Right; x++)
            {
                if (mask.Bits[(y * mask.Width) + x])
                {
                    any = true;
                    mask.Bits[(y * mask.Width) + x] = false;
                }
            }

            // The line may be several rows thick: stop at the first clear row after it.
            if (any)
            {
                started = true;
            }
            else if (started)
            {
                return;
            }
        }
    }

    private static void ClearEdgeColumns(BitMask mask, PixelRect area, bool fromLeft)
    {
        var started = false;
        for (var i = 0; i < area.Width; i++)
        {
            var x = fromLeft ? area.Left + i : area.Right - 1 - i;
            var any = false;
            for (var y = area.Top; y < area.Bottom; y++)
            {
                if (mask.Bits[(y * mask.Width) + x])
                {
                    any = true;
                    mask.Bits[(y * mask.Width) + x] = false;
                }
            }

            if (any)
            {
                started = true;
            }
            else if (started)
            {
                return;
            }
        }
    }
}

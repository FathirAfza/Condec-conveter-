// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Architecture;

/// <summary>The kinds of thing the analysis finds in a picture; each is a row of the "Objek terdeteksi" list (DESIGN §6.3.1).</summary>
public enum DrawingObjectKind
{
    /// <summary>Walls and the other line-work of a plan.</summary>
    Walls,

    /// <summary>Doors (an arc and its leaf) and windows (a few parallel lines).</summary>
    Openings,

    /// <summary>Text and dimensions.</summary>
    Text,

    /// <summary>Logos and the title block's pictures.</summary>
    Logo,

    /// <summary>A table: a grid of lines with something in its cells.</summary>
    Table,
}

/// <summary>A line of writing, as an OCR engine reads it or as it was found. Rectangles are in picture pixels.</summary>
/// <param name="Text">What it says; null when only the place was found and nothing could read it.</param>
/// <param name="Bounds">Where it is: top left, y down.</param>
public sealed record TextLineItem(string? Text, PixelRect Bounds);

/// <summary>One thing found: where it is, the shapes that make it, and for writing, what it says.</summary>
/// <param name="Primitives">Shapes in picture pixels with y up (the picture's height minus the row).</param>
/// <param name="Texts">Writing inside it, to become TEXT; empty for most items.</param>
public sealed record DrawingItem(PixelRect Bounds, IReadOnlyList<DrawingPrimitive> Primitives, IReadOnlyList<TextLineItem> Texts)
{
    public static DrawingItem OfShapes(PixelRect bounds, IReadOnlyList<DrawingPrimitive> primitives) => new(bounds, primitives, []);
}

/// <summary>Everything found of one kind.</summary>
public sealed record DrawingGroup(DrawingObjectKind Kind, IReadOnlyList<DrawingItem> Items)
{
    /// <summary>The number shown in the list: lines for walls, items for the rest.</summary>
    public int Count => Kind == DrawingObjectKind.Walls ? Items.Sum(i => i.Primitives.Count) : Items.Count;

    public bool IsEmpty => Items.Count == 0;
}

/// <summary>What the analysis found in a picture (DESIGN §6.3.1).</summary>
/// <param name="Width">The size the analysis worked at, in pixels (a big picture is averaged down first).</param>
/// <param name="Reduction">How many pixels of the file one analysis pixel stands for.</param>
/// <param name="DpiX">Pixels per inch of the analysis picture (the file's, divided by <paramref name="Reduction"/>); 0 when the file doesn't say.</param>
/// <param name="UnclearAreas">Places that are too soft to trust.</param>
/// <param name="ClearShare">The share of objects that are not in an unclear place, 0 to 1.</param>
/// <param name="TextWasRead">An OCR engine read the writing; when false, writing is drawn as lines.</param>
public sealed record DrawingAnalysis(
    int Width,
    int Height,
    int Reduction,
    double DpiX,
    double DpiY,
    IReadOnlyList<DrawingGroup> Groups,
    IReadOnlyList<PixelRect> UnclearAreas,
    double ClearShare,
    bool TextWasRead)
{
    public DrawingGroup Group(DrawingObjectKind kind) => Groups.First(g => g.Kind == kind);

    public int ObjectCount => Groups.Sum(g => g.Items.Count);
}

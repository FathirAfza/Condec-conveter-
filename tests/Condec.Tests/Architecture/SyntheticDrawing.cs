// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Architecture;
using Condec.Core.Cad;

namespace Condec.Tests.Architecture;

/// <summary>
/// A drawing made in code: black strokes on white, the way a clean scan looks. The tests of the analysis use it because its
/// right answer is known exactly: a line drawn from here to there must come out as that line.
/// </summary>
public sealed class SyntheticDrawing
{
    private readonly byte[] _gray;
    private readonly byte[] _bgra;

    public SyntheticDrawing(int width, int height)
    {
        Width = width;
        Height = height;
        _gray = new byte[width * height];
        Array.Fill(_gray, (byte)255);
        _bgra = new byte[width * height * 4];
        Array.Fill(_bgra, (byte)255);
    }

    public int Width { get; }

    public int Height { get; }

    public GrayImage Gray => new(Width, Height, (byte[])_gray.Clone());

    public RasterPicture Picture(double dpi = 96) => new(Width, Height, (byte[])_bgra.Clone(), dpi, dpi);

    private void Set(int x, int y, byte b, byte g, byte r)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        var at = (y * Width) + x;
        _gray[at] = (byte)(((299 * r) + (587 * g) + (114 * b) + 500) / 1000);
        _bgra[at * 4] = b;
        _bgra[(at * 4) + 1] = g;
        _bgra[(at * 4) + 2] = r;
        _bgra[(at * 4) + 3] = 255;
    }

    /// <summary>A round brush of <paramref name="thickness"/> pixels, in black unless a color is given.</summary>
    private void Stamp(double cx, double cy, double thickness, (byte B, byte G, byte R)? color = null)
    {
        var radius = thickness / 2;
        var (b, g, r) = color ?? ((byte)0, (byte)0, (byte)0);
        for (var y = (int)Math.Floor(cy - radius); y <= (int)Math.Ceiling(cy + radius); y++)
        {
            for (var x = (int)Math.Floor(cx - radius); x <= (int)Math.Ceiling(cx + radius); x++)
            {
                var dx = x + 0.5 - cx;
                var dy = y + 0.5 - cy;
                if ((dx * dx) + (dy * dy) <= radius * radius)
                {
                    Set(x, y, b, g, r);
                }
            }
        }
    }

    public SyntheticDrawing Line(double x1, double y1, double x2, double y2, double thickness = 3, (byte B, byte G, byte R)? color = null)
    {
        var steps = (int)Math.Ceiling(Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)) * 2) + 1;
        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            Stamp(x1 + ((x2 - x1) * t), y1 + ((y2 - y1) * t), thickness, color);
        }

        return this;
    }

    /// <summary>An arc, angles in degrees as drawn on the page (y down): 0 is to the right, 90 is down.</summary>
    public SyntheticDrawing Arc(double cx, double cy, double radius, double fromDegrees, double toDegrees, double thickness = 3)
    {
        var length = Math.Abs(toDegrees - fromDegrees) * Math.PI / 180 * radius;
        var steps = (int)Math.Ceiling(length * 2) + 1;
        for (var i = 0; i <= steps; i++)
        {
            var angle = (fromDegrees + ((toDegrees - fromDegrees) * i / steps)) * Math.PI / 180;
            Stamp(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)), thickness);
        }

        return this;
    }

    public SyntheticDrawing Circle(double cx, double cy, double radius, double thickness = 3) => Arc(cx, cy, radius, 0, 360, thickness);

    public SyntheticDrawing Rectangle(int left, int top, int right, int bottom, double thickness = 3)
    {
        Line(left, top, right, top, thickness);
        Line(right, top, right, bottom, thickness);
        Line(right, bottom, left, bottom, thickness);
        Line(left, bottom, left, top, thickness);
        return this;
    }

    public SyntheticDrawing Fill(int left, int top, int right, int bottom, (byte B, byte G, byte R)? color = null)
    {
        var (b, g, r) = color ?? ((byte)0, (byte)0, (byte)0);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                Set(x, y, b, g, r);
            }
        }

        return this;
    }

    /// <summary>
    /// Something that reads as writing to a machine: rows of small separate marks of one height, as letters of one line are.
    /// </summary>
    public SyntheticDrawing Letters(int left, int top, int count, int height = 12, int pitch = 9, int stroke = 2)
    {
        for (var i = 0; i < count; i++)
        {
            var x = left + (i * pitch);
            Line(x, top, x, top + height, stroke);
            if (i % 2 == 0)
            {
                Line(x, top, x + (pitch / 2), top, stroke);
                Line(x, top + (height / 2), x + (pitch / 2), top + (height / 2), stroke);
            }
            else
            {
                Line(x, top + height, x + (pitch / 2), top + height, stroke);
            }
        }

        return this;
    }

    /// <summary>The picture blurred by a box filter of <paramref name="radius"/> pixels, as a soft scan or a small photo of a drawing is.</summary>
    public SyntheticDrawing Blurred(int radius)
    {
        var copy = new SyntheticDrawing(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                int b = 0, g = 0, r = 0, count = 0;
                for (var dy = -radius; dy <= radius; dy++)
                {
                    for (var dx = -radius; dx <= radius; dx++)
                    {
                        var sx = Math.Clamp(x + dx, 0, Width - 1);
                        var sy = Math.Clamp(y + dy, 0, Height - 1);
                        var at = ((sy * Width) + sx) * 4;
                        b += _bgra[at];
                        g += _bgra[at + 1];
                        r += _bgra[at + 2];
                        count++;
                    }
                }

                copy.Set(x, y, (byte)(b / count), (byte)(g / count), (byte)(r / count));
            }
        }

        return copy;
    }

    /// <summary>The ink the way the analysis sees it.</summary>
    public BitMask Ink() => BitMask.Ink(Gray, ScanVectorizer.OtsuThreshold(_gray));
}

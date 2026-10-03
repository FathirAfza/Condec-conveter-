// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;

namespace Condec.Core.Architecture;

/// <summary>A picture as it is for analysis: color, so a logo can be told from ink, and the resolution the file states.</summary>
/// <param name="Bgra">Four bytes per pixel, rows top to bottom, transparent areas already on white.</param>
/// <param name="DpiX">Pixels per inch across, as the file states it; 0 when it doesn't.</param>
/// <param name="DpiY">Pixels per inch down.</param>
public sealed record RasterPicture(int Width, int Height, byte[] Bgra, double DpiX, double DpiY)
{
    public GrayImage ToGray()
    {
        var gray = new byte[Width * Height];
        for (var i = 0; i < gray.Length; i++)
        {
            var b = Bgra[i * 4];
            var g = Bgra[(i * 4) + 1];
            var r = Bgra[(i * 4) + 2];
            gray[i] = (byte)(((299 * r) + (587 * g) + (114 * b) + 500) / 1000);
        }

        return new GrayImage(Width, Height, gray);
    }

    /// <summary>
    /// The picture averaged down by a whole factor so neither side is longer than <paramref name="maxSide"/>, and that factor
    /// (1 when it already fits). The resolution is scaled with it, so a size in millimeters stays the same.
    /// </summary>
    public (RasterPicture Picture, int Factor) ReduceTo(int maxSide)
    {
        var factor = (int)Math.Ceiling((double)Math.Max(Width, Height) / maxSide);
        if (factor <= 1)
        {
            return (this, 1);
        }

        var width = (Width + factor - 1) / factor;
        var height = (Height + factor - 1) / factor;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int b = 0, g = 0, r = 0, count = 0;
                for (var sy = y * factor; sy < Math.Min(Height, (y + 1) * factor); sy++)
                {
                    for (var sx = x * factor; sx < Math.Min(Width, (x + 1) * factor); sx++)
                    {
                        var at = ((sy * Width) + sx) * 4;
                        b += Bgra[at];
                        g += Bgra[at + 1];
                        r += Bgra[at + 2];
                        count++;
                    }
                }

                var o = ((y * width) + x) * 4;
                pixels[o] = (byte)((b + (count / 2)) / count);
                pixels[o + 1] = (byte)((g + (count / 2)) / count);
                pixels[o + 2] = (byte)((r + (count / 2)) / count);
                pixels[o + 3] = 255;
            }
        }

        return (new RasterPicture(width, height, pixels, DpiX > 0 ? DpiX / factor : 0, DpiY > 0 ? DpiY / factor : 0), factor);
    }
}

/// <summary>A rectangle in picture pixels, origin top left, y down.</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public int Area => Math.Max(0, Width) * Math.Max(0, Height);

    public bool Contains(double x, double y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public bool Intersects(PixelRect other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    /// <summary>The share of this rectangle that <paramref name="other"/> covers, 0 to 1.</summary>
    public double CoveredBy(PixelRect other)
    {
        var overlap = new PixelRect(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
        return Area == 0 ? 0 : (double)overlap.Area / Area;
    }

    public PixelRect Grow(int margin) => new(Left - margin, Top - margin, Right + margin, Bottom + margin);

    public PixelRect Union(PixelRect other) => new(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    public PixelRect Clamp(int width, int height) => new(Math.Max(0, Left), Math.Max(0, Top), Math.Min(width, Right), Math.Min(height, Bottom));
}

/// <summary>A black and white picture: true is set (ink, or whatever is being looked for).</summary>
public sealed class BitMask
{
    public BitMask(int width, int height)
    {
        Width = width;
        Height = height;
        Bits = new bool[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    public bool[] Bits { get; }

    /// <summary>The pixel, or false outside the picture.</summary>
    public bool this[int x, int y]
    {
        get => (uint)x < (uint)Width && (uint)y < (uint)Height && Bits[(y * Width) + x];
        set => Bits[(y * Width) + x] = value;
    }

    public int Count()
    {
        var count = 0;
        foreach (var bit in Bits)
        {
            if (bit)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Pixels darker than <paramref name="threshold"/>.</summary>
    public static BitMask Ink(GrayImage image, byte threshold)
    {
        var mask = new BitMask(image.Width, image.Height);
        for (var i = 0; i < mask.Bits.Length; i++)
        {
            mask.Bits[i] = image.Pixels[i] < threshold;
        }

        return mask;
    }

    /// <summary>Every set pixel grown by <paramref name="radius"/> pixels in a square.</summary>
    public BitMask Dilate(int radius)
    {
        if (radius <= 0)
        {
            return Clone();
        }

        var horizontal = new BitMask(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;

            // Forward and backward passes: a pixel is set when a set pixel lies within the radius on either side.
            var near = int.MinValue;
            for (var x = 0; x < Width; x++)
            {
                if (Bits[row + x])
                {
                    near = x;
                }

                horizontal.Bits[row + x] = near != int.MinValue && x - near <= radius;
            }

            near = int.MaxValue;
            for (var x = Width - 1; x >= 0; x--)
            {
                if (Bits[row + x])
                {
                    near = x;
                }

                if (near != int.MaxValue && near - x <= radius)
                {
                    horizontal.Bits[row + x] = true;
                }
            }
        }

        var result = new BitMask(Width, Height);
        for (var x = 0; x < Width; x++)
        {
            var near = int.MinValue;
            for (var y = 0; y < Height; y++)
            {
                if (horizontal.Bits[(y * Width) + x])
                {
                    near = y;
                }

                result.Bits[(y * Width) + x] = near != int.MinValue && y - near <= radius;
            }

            near = int.MaxValue;
            for (var y = Height - 1; y >= 0; y--)
            {
                if (horizontal.Bits[(y * Width) + x])
                {
                    near = y;
                }

                if (near != int.MaxValue && near - y <= radius)
                {
                    result.Bits[(y * Width) + x] = true;
                }
            }
        }

        return result;
    }

    /// <summary>Only the runs of set pixels along a row that are at least <paramref name="length"/> and at most <paramref name="maxLength"/> long: the horizontal lines.</summary>
    public BitMask LongHorizontalRuns(int length, int maxLength = int.MaxValue)
    {
        var result = new BitMask(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            var x = 0;
            while (x < Width)
            {
                if (!Bits[(y * Width) + x])
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < Width && Bits[(y * Width) + x])
                {
                    x++;
                }

                if (x - start >= length && x - start <= maxLength)
                {
                    for (var i = start; i < x; i++)
                    {
                        result.Bits[(y * Width) + i] = true;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>Only the runs of set pixels down a column that are at least <paramref name="length"/> and at most <paramref name="maxLength"/> long: the vertical lines.</summary>
    public BitMask LongVerticalRuns(int length, int maxLength = int.MaxValue)
    {
        var result = new BitMask(Width, Height);
        for (var x = 0; x < Width; x++)
        {
            var y = 0;
            while (y < Height)
            {
                if (!Bits[(y * Width) + x])
                {
                    y++;
                    continue;
                }

                var start = y;
                while (y < Height && Bits[(y * Width) + x])
                {
                    y++;
                }

                if (y - start >= length && y - start <= maxLength)
                {
                    for (var i = start; i < y; i++)
                    {
                        result.Bits[(i * Width) + x] = true;
                    }
                }
            }
        }

        return result;
    }

    public BitMask Or(BitMask other)
    {
        var result = new BitMask(Width, Height);
        for (var i = 0; i < Bits.Length; i++)
        {
            result.Bits[i] = Bits[i] || other.Bits[i];
        }

        return result;
    }

    public BitMask Clone()
    {
        var copy = new BitMask(Width, Height);
        Array.Copy(Bits, copy.Bits, Bits.Length);
        return copy;
    }

    /// <summary>Clears every pixel inside <paramref name="rect"/>.</summary>
    public void Clear(PixelRect rect)
    {
        var area = rect.Clamp(Width, Height);
        for (var y = area.Top; y < area.Bottom; y++)
        {
            Array.Clear(Bits, (y * Width) + area.Left, Math.Max(0, area.Width));
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Architecture;

/// <summary>
/// Finds logos and the colored pictures of a title block: areas of strong color. Ink on a drawing is black or grey, so a
/// region of saturated pixels is something else. A logo that is only black can't be told from line-work and is not found.
/// </summary>
public static class LogoFinder
{
    /// <summary>A pixel is colored when its strongest and weakest channel differ by at least this much (0 to 255).</summary>
    public const int MinimumSaturation = 70;

    /// <summary>A region smaller than this share of the shorter side of the picture, on either side, is colored writing or a mark, not a logo.</summary>
    public const double MinimumSideShare = 0.03;

    /// <summary>A region this share of the picture or more is a photo or a render, not a logo on a drawing.</summary>
    public const double MaximumAreaShare = 0.5;

    public static List<PixelRect> Find(RasterPicture picture, CancellationToken ct)
    {
        var colored = new BitMask(picture.Width, picture.Height);
        for (var i = 0; i < colored.Bits.Length; i++)
        {
            var b = picture.Bgra[i * 4];
            var g = picture.Bgra[(i * 4) + 1];
            var r = picture.Bgra[(i * 4) + 2];
            colored.Bits[i] = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) >= MinimumSaturation;
        }

        var shorter = Math.Min(picture.Width, picture.Height);

        // Parts of one logo sit a little apart: grow them until they touch.
        var joined = colored.Dilate(Math.Max(2, shorter / 150));
        var blobs = ConnectedComponents.Label(joined, out _);

        var minimumSide = Math.Max(8, (int)(shorter * MinimumSideShare));
        var result = new List<PixelRect>();
        foreach (var blob in blobs)
        {
            ct.ThrowIfCancellationRequested();
            if (blob.Width < minimumSide || blob.Height < minimumSide)
            {
                continue;
            }

            if (blob.Bounds.Area > MaximumAreaShare * picture.Width * picture.Height)
            {
                continue;
            }

            // Back to the colored pixels themselves: the margin the growing added is not part of the logo.
            result.Add(Tighten(colored, blob.Bounds));
        }

        return result;
    }

    private static PixelRect Tighten(BitMask colored, PixelRect area)
    {
        var rect = area.Clamp(colored.Width, colored.Height);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                if (colored.Bits[(y * colored.Width) + x])
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        return maxX < 0 ? rect : new PixelRect(minX, minY, maxX + 1, maxY + 1);
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using UglyToad.PdfPig.Graphics.Colors;

namespace Condec.Core.Cad;

/// <summary>
/// PDF colours to AutoCAD Color Index. The R2000 output format has no true colour, so each colour goes to
/// the nearest of the seven basic index colours and the grey ramp. Black and white both become index 7,
/// which CAD programs draw as their foreground colour.
/// </summary>
internal static class CadColors
{
    private static readonly (short Index, byte R, byte G, byte B)[] Palette =
    [
        (1, 255, 0, 0),
        (2, 255, 255, 0),
        (3, 0, 255, 0),
        (4, 0, 255, 255),
        (5, 0, 0, 255),
        (6, 255, 0, 255),
        (7, 0, 0, 0),
        (7, 255, 255, 255),
        (8, 128, 128, 128),
        (9, 192, 192, 192),
        (250, 51, 51, 51),
        (251, 91, 91, 91),
        (252, 132, 132, 132),
        (253, 173, 173, 173),
        (254, 214, 214, 214),
        (30, 255, 127, 0),
        (40, 255, 191, 0),
        (50, 191, 255, 0),
        (90, 0, 255, 127),
        (130, 0, 191, 255),
        (150, 0, 127, 255),
        (170, 63, 0, 255),
        (190, 127, 0, 255),
        (210, 191, 0, 255),
        (230, 255, 0, 191),
        (12, 165, 0, 0),
        (32, 165, 82, 0),
        (72, 82, 165, 0),
        (92, 0, 165, 82),
        (152, 0, 82, 165),
        (172, 41, 0, 165),
    ];

    /// <summary>
    /// The colours of the full index range that are not greys: 1 to 6 and 10 to 249, as ACadSharp lists them. Used for fills
    /// that should keep their look, such as logos, where seven basic colours are too few.
    /// </summary>
    private static readonly (short Index, byte R, byte G, byte B)[] Hues = Enumerable.Range(1, 6).Concat(Enumerable.Range(10, 240))
        .Select(index =>
        {
            var rgb = Color.GetIndexRGB((byte)index);
            return ((short)index, rgb[0], rgb[1], rgb[2]);
        })
        .ToArray();

    /// <summary>Strongest minus weakest channel below this: a grey, which <see cref="FromRgbFine"/> leaves to the grey ramp and index 7.</summary>
    private const int GreySpread = 24;

    public static Color FromPdf(IColor? color)
    {
        if (color is null)
        {
            return new Color(7);
        }

        var (r, g, b) = color.ToRGBValues();
        return FromRgb(To255(r), To255(g), To255(b));
    }

    public static Color FromRgb(byte r, byte g, byte b) => new(Nearest(r, g, b, Palette).Index);

    /// <summary>
    /// The nearest index colour over the whole range of hues, for a filled area that should look like its source (a logo).
    /// Greys and black go where <see cref="FromRgb"/> puts them, so black is still index 7.
    /// </summary>
    public static Color FromRgbFine(byte r, byte g, byte b) =>
        Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < GreySpread ? FromRgb(r, g, b) : new(Nearest(r, g, b, Hues).Index);

    /// <summary>
    /// True for a colour that goes to white: a light tint that is barely visible on paper but becomes index 7,
    /// which CAD programs draw in their foreground colour. Greys from about 235 up count.
    /// </summary>
    public static bool IsPale(IColor? color)
    {
        if (color is null)
        {
            return false;
        }

        var (r, g, b) = color.ToRGBValues();
        return Nearest(To255(r), To255(g), To255(b), Palette) is { Index: 7, R: 255 };
    }

    private static (short Index, byte R, byte G, byte B) Nearest(byte r, byte g, byte b, (short Index, byte R, byte G, byte B)[] palette)
    {
        var best = palette[0];
        var bestDistance = double.MaxValue;
        foreach (var entry in palette)
        {
            var distance = Sq(r - entry.R) + Sq(g - entry.G) + Sq(b - entry.B);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = entry;
            }
        }

        return best;
    }

    private static byte To255(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static double Sq(double v) => v * v;
}

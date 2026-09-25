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

    public static Color FromPdf(IColor? color)
    {
        if (color is null)
        {
            return new Color(7);
        }

        var (r, g, b) = color.ToRGBValues();
        return FromRgb(To255(r), To255(g), To255(b));
    }

    public static Color FromRgb(byte r, byte g, byte b)
    {
        var best = Palette[0];
        var bestDistance = double.MaxValue;
        foreach (var entry in Palette)
        {
            var distance = Sq(r - entry.R) + Sq(g - entry.G) + Sq(b - entry.B);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = entry;
            }
        }

        return new Color(best.Index);
    }

    private static byte To255(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

    private static double Sq(double v) => v * v;
}

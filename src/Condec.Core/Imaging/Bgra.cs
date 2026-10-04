// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Imaging;

/// <summary>Helpers for pixels held as 4 bytes each: blue, green, red, alpha.</summary>
internal static class Bgra
{
    /// <summary>Composites straight-alpha BGRA pixels onto white and makes them opaque.</summary>
    public static void FlattenOntoWhite(byte[] bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            int alpha = bgra[i + 3];
            if (alpha == 255)
            {
                continue;
            }

            for (var channel = i; channel < i + 3; channel++)
            {
                bgra[channel] = (byte)(((bgra[channel] * alpha) + (255 * (255 - alpha)) + 127) / 255);
            }

            bgra[i + 3] = 255;
        }
    }
}

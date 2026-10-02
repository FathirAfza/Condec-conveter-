// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Upscale;

/// <summary>
/// Enlarges a picture of any size with a network that takes one fixed-size tile. Every tile is the same size, so an
/// engine that compiles for a tensor shape (DirectML) does so once: the picture is read with its edges mirrored, and
/// each tile carries a border of its neighbors that is thrown away, so the tiles meet without seams.
/// </summary>
public static class TiledUpscaler
{
    /// <summary>Input pixels per tile edge. Measured on a GPU and a CPU: smaller tiles run faster per pixel, larger ones waste less on borders (DESIGN §6.2).</summary>
    public const int TileSize = 128;

    /// <summary>Border pixels read around each tile and discarded. 10 px keeps a tiled result within 53 dB of the whole picture, which no eye can tell apart.</summary>
    public const int TilePad = 10;

    /// <summary>Pixels of the picture each tile contributes.</summary>
    public const int InnerSize = TileSize - (2 * TilePad);

    public static int TileCount(int width, int height) => CeilDivide(width, InnerSize) * CeilDivide(height, InnerSize);

    /// <summary>
    /// Runs the model over a BGRA picture and returns the picture <see cref="IUpscaleModel.Scale"/> times larger, as BGRA
    /// with every pixel opaque (the network sees color only; alpha is the caller's business).
    /// </summary>
    /// <param name="progress">Called after each tile with the tiles done and the number of tiles.</param>
    public static byte[] Run(
        IUpscaleModel model,
        byte[] bgra,
        int width,
        int height,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bgra);
        if (width < 1 || height < 1 || bgra.LongLength != (long)width * height * 4)
        {
            throw new ArgumentException("The pixel data doesn't match the size.", nameof(bgra));
        }

        var scale = model.Scale;
        var outputWidth = checked(width * scale);
        var output = new byte[checked((long)outputWidth * height * scale * 4)];
        var plane = TileSize * TileSize;
        var input = new float[3 * plane];
        var total = TileCount(width, height);
        var done = 0;

        for (var tileY = 0; tileY < height; tileY += InnerSize)
        {
            for (var tileX = 0; tileX < width; tileX += InnerSize)
            {
                ct.ThrowIfCancellationRequested();
                FillTile(bgra, width, height, tileX - TilePad, tileY - TilePad, input);
                var result = model.RunTile(input);
                WriteInterior(result, output, outputWidth, scale, tileX, tileY, Math.Min(InnerSize, width - tileX), Math.Min(InnerSize, height - tileY));
                progress?.Report((++done, total));
            }
        }

        return output;
    }

    /// <summary>The picture's pixel at <paramref name="index"/> of <paramref name="length"/>, mirrored at the edges (the edge pixel itself is not repeated).</summary>
    internal static int Reflect(int index, int length)
    {
        if (length == 1)
        {
            return 0;
        }

        var period = (2 * length) - 2;
        var wrapped = ((index % period) + period) % period;
        return wrapped < length ? wrapped : period - wrapped;
    }

    private static void FillTile(byte[] bgra, int width, int height, int left, int top, float[] input)
    {
        var plane = TileSize * TileSize;
        for (var y = 0; y < TileSize; y++)
        {
            var row = Reflect(top + y, height) * width;
            for (var x = 0; x < TileSize; x++)
            {
                var pixel = (row + Reflect(left + x, width)) * 4;
                var at = (y * TileSize) + x;
                input[at] = bgra[pixel + 2] / 255f;
                input[plane + at] = bgra[pixel + 1] / 255f;
                input[(2 * plane) + at] = bgra[pixel] / 255f;
            }
        }
    }

    private static void WriteInterior(float[] result, byte[] output, int outputWidth, int scale, int tileX, int tileY, int width, int height)
    {
        var tileOut = TileSize * scale;
        var plane = tileOut * tileOut;
        var pad = TilePad * scale;
        for (var y = 0; y < height * scale; y++)
        {
            var source = ((pad + y) * tileOut) + pad;
            var target = ((((tileY * scale) + y) * outputWidth) + (tileX * scale)) * 4;
            for (var x = 0; x < width * scale; x++)
            {
                output[target] = ToByte(result[(2 * plane) + source + x]);
                output[target + 1] = ToByte(result[plane + source + x]);
                output[target + 2] = ToByte(result[source + x]);
                output[target + 3] = 255;
                target += 4;
            }
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static int CeilDivide(int value, int by) => (value + by - 1) / by;
}

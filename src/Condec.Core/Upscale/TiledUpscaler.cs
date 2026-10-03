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
    /// <summary>
    /// Input pixels per tile edge when memory is no concern. Measured on a GPU and a CPU: 128 is the fastest size; smaller
    /// tiles waste more on their borders but need less memory (DESIGN §6.2, §7.5).
    /// </summary>
    public const int DefaultTileSize = 128;

    /// <summary>The tile sizes a render may use, largest first. The memory limit in Settings picks the largest that fits (<see cref="UpscaleMemory"/>).</summary>
    public static IReadOnlyList<int> TileSizes { get; } = [128, 96, 64, 48];

    /// <summary>Border pixels read around each tile and discarded. 10 px keeps a tiled result within 53 dB of the whole picture, which no eye can tell apart.</summary>
    public const int TilePad = 10;

    /// <summary>Pixels of the picture a tile of <paramref name="tileSize"/> contributes.</summary>
    public static int InnerSize(int tileSize = DefaultTileSize) => tileSize - (2 * TilePad);

    public static int TileCount(int width, int height, int tileSize = DefaultTileSize) =>
        CeilDivide(width, InnerSize(tileSize)) * CeilDivide(height, InnerSize(tileSize));

    /// <summary>
    /// Runs the model over a BGRA picture and returns the picture <see cref="IUpscaleModel.Scale"/> times larger, as BGRA
    /// with every pixel opaque (the network sees color only; alpha is the caller's business).
    /// </summary>
    /// <param name="tileSize">One of <see cref="TileSizes"/>.</param>
    /// <param name="progress">Called after each tile with the tiles done and the number of tiles.</param>
    public static byte[] Run(
        IUpscaleModel model,
        byte[] bgra,
        int width,
        int height,
        int tileSize,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bgra);
        if (!TileSizes.Contains(tileSize))
        {
            throw new ArgumentOutOfRangeException(nameof(tileSize), tileSize, "Not a tile size the upscaler uses.");
        }

        if (width < 1 || height < 1 || bgra.LongLength != (long)width * height * 4)
        {
            throw new ArgumentException("The pixel data doesn't match the size.", nameof(bgra));
        }

        var scale = model.Scale;
        var outputWidth = checked(width * scale);
        var output = new byte[checked((long)outputWidth * height * scale * 4)];
        var inner = InnerSize(tileSize);
        var input = new float[3 * tileSize * tileSize];
        var total = TileCount(width, height, tileSize);
        var done = 0;

        for (var tileY = 0; tileY < height; tileY += inner)
        {
            for (var tileX = 0; tileX < width; tileX += inner)
            {
                ct.ThrowIfCancellationRequested();
                FillTile(bgra, width, height, tileX - TilePad, tileY - TilePad, tileSize, input);
                var result = model.RunTile(input, tileSize);
                WriteInterior(result, output, outputWidth, scale, tileSize, tileX, tileY, Math.Min(inner, width - tileX), Math.Min(inner, height - tileY));
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

    private static void FillTile(byte[] bgra, int width, int height, int left, int top, int tileSize, float[] input)
    {
        var plane = tileSize * tileSize;
        for (var y = 0; y < tileSize; y++)
        {
            var row = Reflect(top + y, height) * width;
            for (var x = 0; x < tileSize; x++)
            {
                var pixel = (row + Reflect(left + x, width)) * 4;
                var at = (y * tileSize) + x;
                input[at] = bgra[pixel + 2] / 255f;
                input[plane + at] = bgra[pixel + 1] / 255f;
                input[(2 * plane) + at] = bgra[pixel] / 255f;
            }
        }
    }

    private static void WriteInterior(float[] result, byte[] output, int outputWidth, int scale, int tileSize, int tileX, int tileY, int width, int height)
    {
        var tileOut = tileSize * scale;
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

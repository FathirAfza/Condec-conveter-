// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Upscale;

/// <summary>
/// Enlarges a picture of any size with a network that takes one fixed-size tile. Every tile is the same size, so an
/// engine that compiles for a tensor shape (DirectML) does so once: the picture is read with its edges mirrored.
/// </summary>
/// <remarks>
/// A network like Real-ESRGAN invents detail, and two tiles invent it a little differently where they meet. Cutting from
/// one tile to the next left a visible line across blurry pictures (DESIGN §6.2). So each tile throws away a thin border
/// (<see cref="TileMargin"/>), and neighboring tiles overlap by <see cref="TileOverlap"/> pixels, across which the result
/// fades from one tile to the other. Measured with the bundled model against the whole picture run at once, 128 px tiles
/// come 1.3 to 2.9 dB closer than cutting did, and no more tiles are run.
/// </remarks>
public static class TiledUpscaler
{
    /// <summary>
    /// Input pixels per tile edge when memory is no concern. Measured on a GPU and a CPU: 128 is the fastest size; smaller
    /// tiles waste more on their borders but need less memory (DESIGN §6.2, §7.5).
    /// </summary>
    public const int DefaultTileSize = 128;

    /// <summary>The tile sizes a render may use, largest first. The memory limit in Settings picks the largest that fits (<see cref="UpscaleMemory"/>).</summary>
    public static IReadOnlyList<int> TileSizes { get; } = [128, 96, 64, 48];

    /// <summary>Border pixels read around each tile and thrown away: the network sees too little around them.</summary>
    public const int TileMargin = 4;

    /// <summary>Pixels where neighboring tiles overlap and are faded into each other.</summary>
    public const int TileOverlap = 12;

    /// <summary>Pixels of the picture a tile's result covers: the tile without its margins.</summary>
    public static int KeptSize(int tileSize = DefaultTileSize) => tileSize - (2 * TileMargin);

    /// <summary>Pixels of the picture each further tile adds: the step from one tile to the next.</summary>
    public static int InnerSize(int tileSize = DefaultTileSize) => KeptSize(tileSize) - TileOverlap;

    public static int TileCount(int width, int height, int tileSize = DefaultTileSize) =>
        TilesAlong(width, tileSize) * TilesAlong(height, tileSize);

    /// <summary>Tiles needed along one side: the first covers <see cref="KeptSize"/> pixels, every next one <see cref="InnerSize"/> more.</summary>
    private static int TilesAlong(int length, int tileSize) =>
        length <= KeptSize(tileSize) ? 1 : 1 + CeilDivide(length - KeptSize(tileSize), InnerSize(tileSize));

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
        var columns = Spans(width, tileSize);
        var rows = Spans(height, tileSize);

        // A row of tiles is put together here where it fades into the row above, then faded in as a whole: done tile by
        // tile, the corner where four tiles meet would come out wrong.
        var fadeRows = rows.Length > 1 ? new byte[outputWidth * TileOverlap * scale * 4] : [];
        var input = new float[3 * tileSize * tileSize];
        var total = columns.Length * rows.Length;
        var done = 0;

        foreach (var row in rows)
        {
            foreach (var column in columns)
            {
                ct.ThrowIfCancellationRequested();
                FillTile(bgra, width, height, column.Origin, row.Origin, tileSize, input);
                var result = model.RunTile(input, tileSize);
                WriteTile(result, tileSize, scale, row, column, output, outputWidth, fadeRows);
                progress?.Report((++done, total));
            }

            if (row.FadesIn)
            {
                FadeInRows(output, fadeRows, outputWidth, row.From * scale, scale);
            }
        }

        return output;
    }

    /// <summary>
    /// Where the tiles along one side are read and what each writes. Tile k writes from k steps on to where the next one has
    /// faded in. The last tile is read up to the picture's edge, so it sees real pixels rather than mirrored ones, but it
    /// still fades in one step after the tile before it: no pixel is ever shared by more than two tiles along a side.
    /// </summary>
    private static Span[] Spans(int length, int tileSize)
    {
        var count = TilesAlong(length, tileSize);
        var step = InnerSize(tileSize);
        var spans = new Span[count];
        for (var k = 0; k < count; k++)
        {
            var last = k == count - 1;
            var origin = (last && count > 1 ? length - KeptSize(tileSize) : k * step) - TileMargin;
            spans[k] = new Span(origin, k * step, last ? length : ((k + 1) * step) + TileOverlap);
        }

        return spans;
    }

    /// <summary>One tile along one side, in source pixels.</summary>
    /// <param name="Origin">The first pixel the tile reads (below 0 where the picture is mirrored).</param>
    /// <param name="From">The first pixel the tile writes. Past the first tile, the first <see cref="TileOverlap"/> of them fade in.</param>
    /// <param name="To">One past the last pixel the tile writes.</param>
    private readonly record struct Span(int Origin, int From, int To)
    {
        public bool FadesIn => From > 0;
    }

    private static void WriteTile(float[] result, int tileSize, int scale, Span row, Span column, byte[] output, int outputWidth, byte[] fadeRows)
    {
        var tileOut = tileSize * scale;
        var plane = tileOut * tileOut;
        var fade = TileOverlap * scale;
        var top = row.From * scale;
        var left = column.From * scale;
        var right = column.To * scale;
        for (var y = top; y < row.To * scale; y++)
        {
            var intoFadeRows = row.FadesIn && y < top + fade;
            var target = intoFadeRows ? fadeRows : output;
            var at = ((((intoFadeRows ? y - top : y) * outputWidth) + left) * 4);
            var source = ((y - (row.Origin * scale)) * tileOut) + left - (column.Origin * scale);
            for (var x = left; x < right; x++)
            {
                var weight = column.FadesIn && x < left + fade ? (x - left + 0.5f) / fade : 1f;
                target[at] = Mix(target[at], result[(2 * plane) + source], weight);
                target[at + 1] = Mix(target[at + 1], result[plane + source], weight);
                target[at + 2] = Mix(target[at + 2], result[source], weight);
                target[at + 3] = 255;
                at += 4;
                source++;
            }
        }
    }

    private static void FadeInRows(byte[] output, byte[] fadeRows, int outputWidth, int top, int scale)
    {
        var fade = TileOverlap * scale;
        var rowBytes = outputWidth * 4;
        for (var i = 0; i < fade; i++)
        {
            var weight = (i + 0.5f) / fade;
            var at = (top + i) * rowBytes;
            var from = i * rowBytes;
            for (var x = 0; x < rowBytes; x += 4)
            {
                output[at + x] = Mix(output[at + x], fadeRows[from + x] / 255f, weight);
                output[at + x + 1] = Mix(output[at + x + 1], fadeRows[from + x + 1] / 255f, weight);
                output[at + x + 2] = Mix(output[at + x + 2], fadeRows[from + x + 2] / 255f, weight);
            }
        }
    }

    /// <summary><paramref name="previous"/> moved <paramref name="weight"/> of the way to <paramref name="next"/>; a weight of 1 is <paramref name="next"/> exactly.</summary>
    private static byte Mix(byte previous, float next, float weight) =>
        weight >= 1f ? ToByte(next) : ToByte((previous / 255f) + ((next - (previous / 255f)) * weight));

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

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    private static int CeilDivide(int value, int by) => (value + by - 1) / by;
}

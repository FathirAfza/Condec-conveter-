// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;

namespace Condec.Core.Upscale;

/// <summary>Which engine really renders, and how a render reports its tiles.</summary>
public static class UpscaleSupport
{
    /// <summary>
    /// The engine that does the work for the engine chosen in Settings. ONNX Runtime with DirectML runs on the CPU or a GPU;
    /// it can't be pointed at an NPU, so an NPU choice renders on the GPU (or the CPU without one) and the page says so
    /// (DESIGN §13).
    /// </summary>
    public static RenderEngine EffectiveEngine(RenderEngine chosen, DeviceProfile device) => chosen switch
    {
        RenderEngine.Npu => device.HasGpu ? RenderEngine.Gpu : RenderEngine.Cpu,
        RenderEngine.Gpu when !device.HasGpu => RenderEngine.Cpu,
        _ => chosen,
    };

    /// <summary>
    /// The most pixels a result can have: a picture is held as 4 bytes per pixel in one .NET array, which is just under 2 GB
    /// (the same limit as <c>ImageTooLargeException.MaximumPixels</c>).
    /// </summary>
    public const long MaximumOutputPixels = int.MaxValue / 4;

    /// <summary>The network's own result is 16 times the source, and is held whole before it is resized.</summary>
    public const long MaximumSourcePixels = MaximumOutputPixels / 16;

    /// <summary>Pixels the network produces for a picture: always 4 times each side, whatever size is asked for.</summary>
    public static long NetworkPixels(int width, int height) => 16L * width * height;

    /// <summary>
    /// Pixels of work the network really does: every tile runs at full size, also the part-filled ones at the right and bottom
    /// edges, so this is the tile count times one tile's useful result. The benchmark measures the same unit
    /// (<see cref="TileMegapixels"/>), which keeps the estimate in step with the render (DESIGN §8).
    /// </summary>
    public static long RenderedPixels(int width, int height, int tileSize = TiledUpscaler.DefaultTileSize) =>
        (long)TiledUpscaler.TileCount(width, height, tileSize) * (TiledUpscaler.InnerSize(tileSize) * 4L) * (TiledUpscaler.InnerSize(tileSize) * 4L);

    /// <summary>Megapixels of useful result one tile adds (the tile without its border, 4 times larger).</summary>
    public static double TileMegapixels(int scale, int tileSize = TiledUpscaler.DefaultTileSize) =>
        (double)TiledUpscaler.InnerSize(tileSize) * scale * TiledUpscaler.InnerSize(tileSize) * scale / 1_000_000d;

    // The converter reports tiles through the progress detail, so the page can say "Tile n of N".
    public static string TileDetail(int done, int total) => $"{done}/{total}";

    public static bool TryParseTiles(string? detail, out int done, out int total)
    {
        done = total = 0;
        var parts = detail?.Split('/');
        return parts is [var a, var b] && int.TryParse(a, out done) && int.TryParse(b, out total);
    }

    /// <summary>Where the tiles end inside the Encode stage of the converter; saving the result follows (DESIGN §6.2).</summary>
    public const double TilesEnd = 0.8;
}

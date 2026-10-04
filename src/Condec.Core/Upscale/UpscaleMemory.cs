// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;

namespace Condec.Core.Upscale;

/// <summary>How an upscale fits the memory limit: the tile size it will run at, what it needs at most, and whether that is within the limit.</summary>
/// <param name="TileSize">The largest of <see cref="TiledUpscaler.TileSizes"/> that fits; the smallest when none does.</param>
/// <param name="PeakBytes">The most memory the whole process is expected to hold at <paramref name="TileSize"/>.</param>
/// <param name="Fits"><see cref="PeakBytes"/> is within the limit.</param>
public sealed record MemoryPlan(int TileSize, long PeakBytes, bool Fits);

/// <summary>
/// The memory limit in Settings made real (DESIGN §7.5): what an upscale needs is worked out from the sizes involved, and the
/// largest tile that keeps it within the limit is the one used. A smaller tile is slower, never worse. A picture that doesn't
/// fit even with the smallest tile isn't started. The constants come from measurements on a CPU and a DirectML GPU
/// (DESIGN §13 #26); each is kept slightly above what was measured.
/// </summary>
public static class UpscaleMemory
{
    /// <summary>The app itself: WinUI, the pages, the .NET heap before any picture.</summary>
    public const long AppBytes = 300L * 1024 * 1024;

    /// <summary>The network's weights and the engine around them.</summary>
    public static long ModelBytes(RenderEngine engine) => (engine == RenderEngine.Gpu ? 180L : 120L) * 1024 * 1024;

    /// <summary>What running one tile holds, per pixel of the tile (activations of 23 dense blocks).</summary>
    public static long TileBytesPerPixel(RenderEngine engine) => engine == RenderEngine.Gpu ? 22L * 1024 : 14L * 1024;

    /// <summary>
    /// The most the process holds at once while a picture is rendered and saved: the source, the network's result (16 times the
    /// source), the first pass of the resize, the result in the asked size, and the copies that saving it makes.
    /// </summary>
    public static long PeakBytes(int width, int height, int outputWidth, int outputHeight, int tileSize, RenderEngine engine, bool keepsAlpha)
    {
        long source = 4L * width * height;
        long network = 64L * width * height;
        long firstPass = 16L * outputWidth * height;
        long result = 4L * outputWidth * outputHeight;
        long saving = 9L * outputWidth * outputHeight;
        long alpha = keepsAlpha ? (long)width * height + ((long)outputWidth * height) + ((long)outputWidth * outputHeight) : 0;
        return AppBytes + ModelBytes(engine) + (TileBytesPerPixel(engine) * tileSize * tileSize) + source + network + firstPass + result + saving + alpha;
    }

    /// <param name="engine">The engine that really renders (<see cref="UpscaleSupport.EffectiveEngine"/>).</param>
    /// <param name="keepsAlpha">The result is a PNG, so a transparent source needs its alpha kept and resized apart.</param>
    /// <param name="maxTileSize">The largest tile the performance mode allows (<see cref="RenderPace.MaxTileSize"/>).</param>
    public static MemoryPlan Plan(
        int width,
        int height,
        int outputWidth,
        int outputHeight,
        RenderEngine engine,
        bool keepsAlpha,
        long limitBytes,
        int maxTileSize = TiledUpscaler.DefaultTileSize)
    {
        if (!TiledUpscaler.TileSizes.Contains(maxTileSize))
        {
            throw new ArgumentOutOfRangeException(nameof(maxTileSize), maxTileSize, "Not a tile size the upscaler uses.");
        }

        MemoryPlan? last = null;
        foreach (var tileSize in TiledUpscaler.TileSizes.Where(t => t <= maxTileSize))
        {
            var peak = PeakBytes(width, height, outputWidth, outputHeight, tileSize, engine, keepsAlpha);
            last = new MemoryPlan(tileSize, peak, peak <= limitBytes);
            if (last.Fits)
            {
                return last;
            }
        }

        return last!;
    }
}

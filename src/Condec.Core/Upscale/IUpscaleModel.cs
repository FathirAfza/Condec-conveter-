// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Devices;

namespace Condec.Core.Upscale;

/// <summary>A super-resolution network that enlarges one fixed-size tile.</summary>
public interface IUpscaleModel : IDisposable
{
    /// <summary>How many times larger the result of a tile is (4 for Real-ESRGAN x4plus).</summary>
    int Scale { get; }

    /// <summary>
    /// Enlarges one tile. The input is planar RGB with values 0 to 1, <paramref name="tileSize"/> pixels square
    /// (3 x size x size floats); the result is planar RGB, <see cref="Scale"/> times larger on each side. Values may fall
    /// a little outside 0 to 1. The result may be the same array on every call: it is only good until the next tile.
    /// </summary>
    float[] RunTile(float[] input, int tileSize);
}

/// <summary>What the user chose for an upscale: the exact size of the result, the engine that renders it, and the network.</summary>
/// <param name="OutputWidth">Pixels. The result is rendered 4 times larger by the network and resized to this.</param>
/// <param name="OutputHeight">Pixels.</param>
/// <param name="Engine">The engine from Settings; when it can't start, the CPU takes over and a note says so.</param>
/// <param name="TileSize">One of <see cref="TiledUpscaler.TileSizes"/>: the largest that fits the memory limit (<see cref="UpscaleMemory"/>).</param>
/// <param name="Style">Which bundled network renders (<see cref="UpscaleStyle"/>).</param>
/// <param name="Duty">The share of the time the engine works, from the performance mode (<see cref="RenderPace.Duty"/>); 1 works without rest.</param>
/// <param name="Adaptive">Lower <paramref name="Duty"/> while other apps use the GPU or the memory is nearly full (DESIGN §7.6).</param>
public sealed record UpscaleOptions(
    int OutputWidth,
    int OutputHeight,
    RenderEngine Engine,
    int TileSize = TiledUpscaler.DefaultTileSize,
    UpscaleStyle Style = UpscaleStyle.Sharp,
    double Duty = 1,
    bool Adaptive = false) : ConversionOptions;

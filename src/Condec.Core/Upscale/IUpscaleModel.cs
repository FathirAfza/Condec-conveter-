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
    /// Enlarges one tile. The input is planar RGB with values 0 to 1, <see cref="TiledUpscaler.TileSize"/> pixels square
    /// (3 x size x size floats); the result is planar RGB, <see cref="Scale"/> times larger on each side. Values may fall
    /// a little outside 0 to 1.
    /// </summary>
    float[] RunTile(float[] input);
}

/// <summary>What the user chose for an upscale: the exact size of the result and the engine that renders it.</summary>
/// <param name="OutputWidth">Pixels. The result is rendered 4 times larger by the network and resized to this.</param>
/// <param name="OutputHeight">Pixels.</param>
/// <param name="Engine">The engine from Settings; when it can't start, the CPU takes over and a note says so.</param>
public sealed record UpscaleOptions(int OutputWidth, int OutputHeight, RenderEngine Engine) : ConversionOptions;

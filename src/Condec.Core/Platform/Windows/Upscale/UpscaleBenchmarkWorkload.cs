// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;

namespace Condec.Core.Upscale;

/// <summary>
/// The tile the engine benchmark times (DESIGN §8): the real network on a full-size tile of a made-up picture, on the
/// engine that will render. The first call loads the model, which <see cref="EngineBenchmark"/> leaves out of the timing.
/// Both styles are the same network, so the speed measured with one holds for the other.
/// </summary>
/// <param name="style">The network to time: the one the upscale is about to use, so it is known to be there.</param>
public sealed class UpscaleBenchmarkWorkload(UpscaleStyle style) : IBenchmarkWorkload, IDisposable
{
    private float[]? _tile;
    private OnnxUpscaleModel? _model;
    private RenderEngine? _engine;
    private int _tileSize;

    public Task<double> RunTileAsync(RenderEngine engine, int tileSize, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        if (_model is null || _engine != engine || _tileSize != tileSize)
        {
            _model?.Dispose();
            _model = OnnxUpscaleModel.Open(UpscaleModelLocator.ModelPath(style), engine == RenderEngine.Gpu, tileSize);
            _engine = engine;
            _tileSize = tileSize;
            _tile = MakeTile(tileSize);
            if (_model.Engine != engine)
            {
                // A speed measured on the CPU must never be stored as the GPU's.
                throw new InvalidOperationException($"The {engine} could not run the model: {_model.GpuFailure}");
            }
        }

        _model.RunTile(_tile!, tileSize);
        return UpscaleSupport.TileMegapixels(_model.Scale, tileSize);
    }, ct);

    public void Dispose() => _model?.Dispose();

    /// <summary>A smooth picture with a little noise, the same on every run.</summary>
    private static float[] MakeTile(int size)
    {
        var random = new Random(11);
        var data = new float[3 * size * size];
        for (var c = 0; c < 3; c++)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var value = 0.5 + (0.35 * Math.Sin((x + (c * 20)) / 17.0) * Math.Cos(y / 23.0)) + (random.NextDouble() * 0.04);
                    data[(c * size * size) + (y * size) + x] = (float)Math.Clamp(value, 0, 1);
                }
            }
        }

        return data;
    }
}

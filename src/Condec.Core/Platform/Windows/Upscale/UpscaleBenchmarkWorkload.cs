// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;

namespace Condec.Core.Upscale;

/// <summary>
/// The tile the engine benchmark times (DESIGN §8): the real network on a full-size tile of a made-up picture, on the
/// engine that will render. The first call loads the model, which <see cref="EngineBenchmark"/> leaves out of the timing.
/// </summary>
public sealed class UpscaleBenchmarkWorkload : IBenchmarkWorkload, IDisposable
{
    private readonly float[] _tile = MakeTile();
    private OnnxUpscaleModel? _model;
    private RenderEngine? _engine;

    public Task<double> RunTileAsync(RenderEngine engine, CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        if (_model is null || _engine != engine)
        {
            _model?.Dispose();
            _model = OnnxUpscaleModel.Open(UpscaleModelLocator.ModelPath, engine == RenderEngine.Gpu);
            _engine = engine;
            if (_model.Engine != engine)
            {
                // A speed measured on the CPU must never be stored as the GPU's.
                throw new InvalidOperationException($"The {engine} could not run the model: {_model.GpuFailure}");
            }
        }

        _model.RunTile(_tile);
        return UpscaleSupport.TileMegapixels(_model.Scale);
    }, ct);

    public void Dispose() => _model?.Dispose();

    /// <summary>A smooth picture with a little noise, the same on every run.</summary>
    private static float[] MakeTile()
    {
        var size = TiledUpscaler.TileSize;
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

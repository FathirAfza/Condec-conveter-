// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Condec.Core.Upscale;

/// <summary>
/// Real-ESRGAN x4plus in ONNX Runtime, on the CPU or through DirectML on a GPU. Everything runs inside this process, from
/// the file next to the app; nothing is downloaded or sent anywhere.
/// </summary>
public sealed class OnnxUpscaleModel : IUpscaleModel
{
    private const int NetworkScale = 4;

    private readonly InferenceSession _session;
    private readonly string _inputName;

    static OnnxUpscaleModel()
    {
        // The Windows build of ONNX Runtime can emit diagnostic events to the system by default; Condec sends no telemetry.
        OrtEnv.Instance().DisableTelemetryEvents();
    }

    private OnnxUpscaleModel(InferenceSession session, RenderEngine engine, string? gpuFailure)
    {
        _session = session;
        _inputName = session.InputMetadata.Keys.First();
        Engine = engine;
        GpuFailure = gpuFailure;
    }

    public int Scale => NetworkScale;

    /// <summary>The engine that runs the tiles: <see cref="RenderEngine.Gpu"/> or <see cref="RenderEngine.Cpu"/>.</summary>
    public RenderEngine Engine { get; }

    /// <summary>Why the GPU couldn't be used, when the CPU took over; null otherwise.</summary>
    public string? GpuFailure { get; }

    /// <param name="gpu">Run on the GPU. When DirectML can't start or can't run a tile, the CPU takes over (see <see cref="GpuFailure"/>).</param>
    public static OnnxUpscaleModel Open(string modelPath, bool gpu)
    {
        string? failure = null;
        if (gpu)
        {
            try
            {
                using var options = new SessionOptions
                {
                    // DirectML requires these two (ONNX Runtime documentation, DirectML execution provider).
                    EnableMemoryPattern = false,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                };
                options.AppendExecutionProvider_DML(0);
                var model = new OnnxUpscaleModel(new InferenceSession(modelPath, options), RenderEngine.Gpu, null);
                try
                {
                    // The first tile compiles the network for the GPU; a GPU that can't run it fails here, not in the middle of a picture.
                    model.RunTile(new float[3 * TiledUpscaler.TileSize * TiledUpscaler.TileSize]);
                    return model;
                }
                catch
                {
                    model.Dispose();
                    throw;
                }
            }
            catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                failure = ex.Message;
            }
        }

        using var cpuOptions = new SessionOptions();
        return new OnnxUpscaleModel(new InferenceSession(modelPath, cpuOptions), RenderEngine.Cpu, failure);
    }

    public float[] RunTile(float[] input)
    {
        var tensor = new DenseTensor<float>(input, [1, 3, TiledUpscaler.TileSize, TiledUpscaler.TileSize]);
        using var results = _session.Run([NamedOnnxValue.CreateFromTensor(_inputName, tensor)]);
        return results[0].AsTensor<float>().ToArray();
    }

    public void Dispose() => _session.Dispose();
}

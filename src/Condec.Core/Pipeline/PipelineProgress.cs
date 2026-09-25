// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Pipeline;

/// <summary>The four stages, in order. Their share of the overall progress is fixed (see <see cref="PipelineProgress"/>).</summary>
public enum PipelineStage
{
    /// <summary>0 to 35 %.</summary>
    Decode,

    /// <summary>35 to 70 %.</summary>
    Encode,

    /// <summary>70 to 90 %.</summary>
    VerifyChunks,

    /// <summary>90 to 100 %.</summary>
    VerifyIntegrity,
}

/// <param name="StageFraction">Progress within <paramref name="Stage"/>, from 0 to 1.</param>
/// <param name="OverallFraction">Progress of the whole conversion, from 0 to 1.</param>
/// <param name="ChunkNumber">During <see cref="PipelineStage.VerifyChunks"/>: the chunk just verified, counted from 1.</param>
/// <param name="ChunkCount">The number of chunks, once known (from <see cref="PipelineStage.VerifyChunks"/> on).</param>
/// <param name="Detail">Optional detail from the converter during Decode and Encode.</param>
public readonly record struct PipelineProgress(
    PipelineStage Stage,
    double StageFraction,
    double OverallFraction,
    int ChunkNumber = 0,
    int ChunkCount = 0,
    string? Detail = null)
{
    internal static (double Start, double End) GetRange(PipelineStage stage) => stage switch
    {
        PipelineStage.Decode => (0.00, 0.35),
        PipelineStage.Encode => (0.35, 0.70),
        PipelineStage.VerifyChunks => (0.70, 0.90),
        PipelineStage.VerifyIntegrity => (0.90, 1.00),
        _ => throw new ArgumentOutOfRangeException(nameof(stage)),
    };
}

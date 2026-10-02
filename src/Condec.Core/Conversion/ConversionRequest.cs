// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Conversion;

/// <param name="Output">
/// Forward-only stream owned by the pipeline. It can't seek: a converter whose encoder needs to seek
/// writes to its own staging buffer first, then copies the finished result here in one pass.
/// </param>
public sealed record ConversionRequest(
    string SourcePath,
    string SourceExtension,
    string TargetExtension,
    Stream Output,
    ConversionOptions? Options = null)
{
    /// <summary>Remarks about the result; they end up in <c>ConversionResult.Notes</c>.</summary>
    public ConversionNotes Notes { get; init; } = new();
}

/// <summary>Format-specific settings, such as the unit and scale for PDF to CAD.</summary>
public abstract record ConversionOptions;

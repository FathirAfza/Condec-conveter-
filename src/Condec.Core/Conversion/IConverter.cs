// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Conversion;

/// <summary>
/// Converts one family of formats. The pipeline owns everything around the conversion:
/// the temporary file, hashing, verification and the final move.
/// </summary>
public interface IConverter
{
    /// <summary>
    /// Target extensions (for example ".png") this converter can produce from <paramref name="sourceExtension"/>.
    /// Formats this machine can't produce, such as a missing codec, must not be returned at all.
    /// </summary>
    IReadOnlyList<string> GetTargets(string sourceExtension);

    /// <summary>
    /// Reads <see cref="ConversionRequest.SourcePath"/> and writes the complete output to
    /// <see cref="ConversionRequest.Output"/>. Only the Decode and Encode stages are reported here.
    /// </summary>
    Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct);
}

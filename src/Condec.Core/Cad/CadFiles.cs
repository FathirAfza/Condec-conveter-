// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.IO;
using Condec.Core.Conversion;
using Condec.Core.Formats;

namespace Condec.Core.Cad;

internal static class CadFiles
{
    /// <summary>Reads a DXF (ASCII or binary) or DWG file with ACadSharp.</summary>
    public static CadDocument Read(string path, string extension)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return extension == ".dwg" ? DwgReader.Read(stream) : DxfReader.Read(stream);
    }
}

/// <summary>Reopens a DXF or DWG with ACadSharp's reader and checks the drawing has entities.</summary>
public sealed class CadOutputValidator : IOutputValidator
{
    public bool CanValidate(string targetExtension) => FileExtension.Normalize(targetExtension) is ".dxf" or ".dwg";

    public Task ValidateAsync(string path, string targetExtension, CancellationToken ct) => Task.Run(
        () =>
        {
            var document = CadFiles.Read(path, FileExtension.Normalize(targetExtension));
            if (document.Entities.Count == 0)
            {
                throw new InvalidDataException("The drawing has no entities.");
            }
        },
        ct);
}

/// <summary>DWG to DXF and DXF to DWG, by reading the drawing and writing it again with ACadSharp.</summary>
public sealed class CadFileConverter : IConverter
{
    public IReadOnlyList<string> GetTargets(string sourceExtension) => FileExtension.Normalize(sourceExtension) switch
    {
        ".dwg" => [".dxf"],
        ".dxf" => [".dwg"],
        _ => [],
    };

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        progress.Report(new ConversionProgress(ConversionStage.Decode, 0, "Membaca gambar CAD"));
        var document = await Task.Run(() => CadFiles.Read(request.SourcePath, FileExtension.Normalize(request.SourceExtension)), ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0));
        var bytes = PdfToCadConverter.Write(document, FileExtension.Normalize(request.TargetExtension));
        await request.Output.WriteAsync(bytes, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }
}

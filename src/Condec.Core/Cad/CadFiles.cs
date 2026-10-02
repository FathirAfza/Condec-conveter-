// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Exceptions;
using ACadSharp.IO;
using Condec.Core.Conversion;
using Condec.Core.Formats;

namespace Condec.Core.Cad;

/// <summary>A drawing saved in a version ACadSharp can't read, such as a DWG older than AutoCAD R14.</summary>
public sealed class UnsupportedCadVersionException(string message, Exception inner) : Exception(message, inner);

internal static class CadFiles
{
    /// <summary>Reads a DXF (ASCII or binary) or DWG file with ACadSharp.</summary>
    /// <remarks>
    /// DXF exporters, R12 ones especially, often leave out the TABLES section. CreateDefaults adds the standard
    /// layer, linetypes and text style the file didn't define, which DwgWriter needs (without them it throws
    /// KeyNotFoundException for "Standard").
    /// </remarks>
    public static CadDocument Read(string path, string extension)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (extension == ".dwg")
            {
                return DwgReader.Read(stream);
            }

            using var reader = new DxfReader(stream, null) { Configuration = new DxfReaderConfiguration { CreateDefaults = true } };
            return reader.Read();
        }
        catch (CadNotSupportedException ex)
        {
            throw new UnsupportedCadVersionException(ex.Message, ex);
        }
    }

    /// <summary>
    /// The version to write <paramref name="version"/> as. ACadSharp 3.8.0's README lists what each writer supports:
    /// DwgWriter AC1014, AC1015, AC1018, AC1024, AC1027, AC1032; DxfWriter AC1012 and later. A drawing read from an
    /// older file (an R12 DXF is AC1009) keeps its version, so it has to move to one the writer can produce.
    /// </summary>
    public static ACadVersion WritableVersion(ACadVersion version, string target) => target == ".dwg"
        ? version switch
        {
            ACadVersion.AC1014 or ACadVersion.AC1015 or ACadVersion.AC1018
                or ACadVersion.AC1024 or ACadVersion.AC1027 or ACadVersion.AC1032 => version,
            // R2007: the nearest later version, so text stays Unicode.
            ACadVersion.AC1021 => ACadVersion.AC1024,
            _ => PdfToCadConverter.OutputVersion,
        }
        : version switch
        {
            ACadVersion.AC1012 or ACadVersion.AC1014 or ACadVersion.AC1015 or ACadVersion.AC1018
                or ACadVersion.AC1021 or ACadVersion.AC1024 or ACadVersion.AC1027 or ACadVersion.AC1032 => version,
            _ => PdfToCadConverter.OutputVersion,
        };

    /// <summary>
    /// Removes entities the writers throw NotImplementedException for. A SEQEND closes the vertex or attribute list of
    /// a POLYLINE or INSERT; ACadSharp's DXF reader also leaves it behind as an entity of its own, which the writers
    /// can't save and which carries no drawing data. The placeholder types are what the reader puts where it couldn't
    /// build the real entity.
    /// </summary>
    internal static int DropUnwritableEntities(CadDocument cad)
    {
        var dropped = 0;
        foreach (var record in cad.BlockRecords)
        {
            var unwritable = record.Entities.Where(IsUnwritable).ToList();
            record.Entities.Remove(unwritable);
            dropped += unwritable.Count;
        }

        return dropped;
    }

    private static bool IsUnwritable(Entity entity) =>
        entity is Seqend || entity.GetType().Name.EndsWith("Placeholder", StringComparison.Ordinal);

    /// <summary>
    /// Writes the drawing as DWG or ASCII DXF, first moving it to a version the writer supports (see
    /// <see cref="WritableVersion"/>). ACadSharp writers dispose the stream they write to, so they write to memory.
    /// </summary>
    public static byte[] Write(CadDocument cad, string target)
    {
        cad.Header.Version = WritableVersion(cad.Header.Version, target);
        DropUnwritableEntities(cad);

        using var buffer = new MemoryStream();
        if (target == ".dwg")
        {
            using var writer = new DwgWriter(buffer, cad);
            writer.Write();
        }
        else
        {
            using var writer = new DxfWriter(buffer, cad, false);
            writer.Write();
        }

        return buffer.ToArray();
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
        var bytes = CadFiles.Write(document, FileExtension.Normalize(request.TargetExtension));
        await request.Output.WriteAsync(bytes, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }
}

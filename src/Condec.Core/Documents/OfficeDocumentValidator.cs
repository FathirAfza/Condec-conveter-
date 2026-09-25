// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.IO.Compression;
using System.Text;
using System.Xml;
using Condec.Core.Conversion;
using Condec.Core.Formats;

namespace Condec.Core.Documents;

/// <summary>
/// Reopens Office Open XML (DOCX, XLSX, PPTX) and OpenDocument (ODT, ODS, ODP) files as ZIP packages and
/// parses their main XML part to the end. RTF is checked for its opening and closing group. The legacy binary
/// formats (DOC, XLS, PPT) are only checked for the compound file header: nothing in .NET or Windows parses them.
/// </summary>
public sealed class OfficeDocumentValidator : IOutputValidator
{
    private sealed record Package(string MainPart, string ExpectedRoot, string? OdfMimeType = null);

    private static readonly Dictionary<string, Package> Packages = new(StringComparer.Ordinal)
    {
        [".docx"] = new("word/document.xml", "document"),
        [".xlsx"] = new("xl/workbook.xml", "workbook"),
        [".pptx"] = new("ppt/presentation.xml", "presentation"),
        [".odt"] = new("content.xml", "document-content", "application/vnd.oasis.opendocument.text"),
        [".ods"] = new("content.xml", "document-content", "application/vnd.oasis.opendocument.spreadsheet"),
        [".odp"] = new("content.xml", "document-content", "application/vnd.oasis.opendocument.presentation"),
    };

    private static readonly string[] LegacyBinary = [".doc", ".xls", ".ppt"];

    /// <summary>The first bytes of every OLE compound file (MS-CFB 2.2).</summary>
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public bool CanValidate(string targetExtension)
    {
        var target = FileExtension.Normalize(targetExtension);
        return target == ".rtf" || LegacyBinary.Contains(target) || Packages.ContainsKey(target);
    }

    public async Task ValidateAsync(string path, string targetExtension, CancellationToken ct)
    {
        var target = FileExtension.Normalize(targetExtension);
        if (target == ".rtf")
        {
            await ValidateRtfAsync(path, ct).ConfigureAwait(false);
            return;
        }

        if (LegacyBinary.Contains(target))
        {
            await ValidateCompoundFileAsync(path, ct).ConfigureAwait(false);
            return;
        }

        var package = Packages.TryGetValue(target, out var p)
            ? p
            : throw new NotSupportedException($"'{targetExtension}' is not an office document target.");

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        using var zip = await ZipArchive.CreateAsync(file, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: null, ct).ConfigureAwait(false);

        if (package.OdfMimeType is { } mimeType)
        {
            var actual = await ReadEntryTextAsync(zip, "mimetype", ct).ConfigureAwait(false);
            if (actual != mimeType)
            {
                throw new InvalidDataException($"Expected ODF type '{mimeType}', found '{actual}'.");
            }
        }
        else if (zip.GetEntry("[Content_Types].xml") is null)
        {
            throw new InvalidDataException("The package has no [Content_Types].xml.");
        }

        var main = zip.GetEntry(package.MainPart) ?? throw new InvalidDataException($"The package has no {package.MainPart}.");
        await using var stream = await main.OpenAsync(ct).ConfigureAwait(false);
        var root = await ReadToEndAsync(stream, ct).ConfigureAwait(false);
        if (root != package.ExpectedRoot)
        {
            throw new InvalidDataException($"{package.MainPart} starts with <{root}>, expected <{package.ExpectedRoot}>.");
        }
    }

    /// <summary>Parses the whole part, so a truncated or malformed part fails here. Returns the root element's local name.</summary>
    private static async Task<string?> ReadToEndAsync(Stream stream, CancellationToken ct)
    {
        var settings = new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        string? root = null;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            if (root is null && reader.NodeType == XmlNodeType.Element)
            {
                root = reader.LocalName;
            }

            ct.ThrowIfCancellationRequested();
        }

        return root;
    }

    private static async Task<string> ReadEntryTextAsync(ZipArchive zip, string name, CancellationToken ct)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"The package has no {name}.");
        await using var stream = await entry.OpenAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    private static async Task ValidateCompoundFileAsync(string path, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var header = new byte[CompoundFileSignature.Length];
        await file.ReadExactlyAsync(header, ct).ConfigureAwait(false);

        // A 512-byte header plus at least a directory sector and a data sector.
        if (!header.AsSpan().SequenceEqual(CompoundFileSignature) || file.Length < 3 * 512)
        {
            throw new InvalidDataException("The file is not an OLE compound document.");
        }
    }

    private static async Task ValidateRtfAsync(string path, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(path, Encoding.Latin1, ct).ConfigureAwait(false);
        if (!text.StartsWith(@"{\rtf", StringComparison.Ordinal) || !text.TrimEnd('\r', '\n', ' ', '\0').EndsWith('}'))
        {
            throw new InvalidDataException("The file is not a complete RTF document.");
        }
    }
}

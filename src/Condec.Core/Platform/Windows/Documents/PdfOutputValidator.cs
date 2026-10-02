// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Formats;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Condec.Core.Documents;

/// <summary>Reopens a PDF with the Windows PDF renderer and renders its first page.</summary>
public sealed class PdfOutputValidator : IOutputValidator
{
    public bool CanValidate(string targetExtension) => FileExtension.Normalize(targetExtension) == ".pdf";

    public async Task ValidateAsync(string path, string targetExtension, CancellationToken ct)
    {
        // Loaded through StorageFile, not a wrapped .NET stream: a PdfDocument can outlive a stream disposed
        // here, and the test host then crashed with an access violation when it shut down. (The document itself also
        // has to stay alive after it renders: see RenderedPdfDocuments.)
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)).AsTask(ct).ConfigureAwait(false);
        var document = await PdfDocument.LoadFromFileAsync(file).AsTask(ct).ConfigureAwait(false);
        Pdf.RenderedPdfDocuments.Keep(document);

        if (document.IsPasswordProtected)
        {
            throw new InvalidDataException("The PDF is password protected.");
        }

        if (document.PageCount == 0)
        {
            throw new InvalidDataException("The PDF has no pages.");
        }

        // Rendering proves the page content decodes, not only the cross-reference table.
        using var page = document.GetPage(0);
        using var rendered = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(rendered).AsTask(ct).ConfigureAwait(false);
        if (rendered.Size == 0)
        {
            throw new InvalidDataException("The first page rendered to nothing.");
        }
    }
}

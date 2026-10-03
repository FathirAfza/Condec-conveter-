// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Imaging;
using Condec.Core.Pdf;

namespace Condec.Core.Architecture;

/// <summary>Reads a picture, or a page of a PDF, as pixels for the analysis, with the Windows Imaging Component and the Windows PDF renderer.</summary>
public static class WindowsPictureReader
{
    /// <summary>Resolution a scanned PDF page is read at: 200 dpi, what the PDF → CAD tracing uses.</summary>
    public const double PdfDpi = 200;

    public static async Task<RasterPicture> ReadAsync(string path, CancellationToken ct)
    {
        if (string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var page = await PdfPageRenderer.RenderAsync(path, 1, PdfDpi / 72, ct).ConfigureAwait(false);
            return new RasterPicture((int)page.Width, (int)page.Height, page.Bgra, PdfDpi, PdfDpi);
        }

        var decoded = await ImageConverter.DecodeAsync(path, ct).ConfigureAwait(false);

        // Transparent areas count as paper.
        ImageConverter.FlattenOntoWhite(decoded.Pixels);
        return new RasterPicture((int)decoded.Width, (int)decoded.Height, decoded.Pixels, decoded.DpiX, decoded.DpiY);
    }
}

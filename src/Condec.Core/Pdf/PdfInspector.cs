// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace Condec.Core.Pdf;

/// <summary>
/// A PDF protected with a password or with permission restrictions. Condec doesn't remove protection,
/// so such a file is refused before anything is converted.
/// </summary>
public sealed class LockedPdfException(string message, Exception? inner = null) : Exception(message, inner);

public enum PdfPageKind
{
    /// <summary>Drawn with lines, curves and text.</summary>
    Vector,

    /// <summary>Mostly one raster image with hardly any lines: a scanned or photographed page.</summary>
    Scan,

    /// <summary>Nothing to convert.</summary>
    Empty,
}

public static class PdfInspector
{
    /// <summary>A page with fewer path segments than this and a large image counts as a scan.</summary>
    internal const int ScanMaxSegments = 50;

    /// <summary>Share of the page one image must cover to count as a scan.</summary>
    internal const double ScanMinImageCoverage = 0.7;

    /// <summary>Opens a PDF for reading. Throws <see cref="LockedPdfException"/> for any encrypted file.</summary>
    public static PdfDocument Open(string path)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(path);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new LockedPdfException("The PDF needs a password to open.", ex);
        }

        // Also refuses files that open without a password but carry an owner password (permission restrictions).
        if (document.IsEncrypted)
        {
            document.Dispose();
            throw new LockedPdfException("The PDF is encrypted.");
        }

        return document;
    }

    /// <summary>The number of pages, after checking the file isn't locked.</summary>
    public static int CountPages(string path)
    {
        using var document = Open(path);
        return document.NumberOfPages;
    }

    public static PdfPageKind GetPageKind(string path, int pageNumber)
    {
        using var document = Open(path);
        return GetPageKind(document.GetPage(pageNumber));
    }

    public static PdfPageKind GetPageKind(Page page)
    {
        var segments = page.Paths.Where(path => !path.IsClipping).Sum(path => path.Sum(subpath => subpath.Commands.Count));
        var pageArea = page.Width * page.Height;
        var largestImage = page.GetImages().Select(image => image.BoundingBox.Area).DefaultIfEmpty(0).Max();

        if (segments < ScanMaxSegments && pageArea > 0 && largestImage / pageArea >= ScanMinImageCoverage)
        {
            return PdfPageKind.Scan;
        }

        return segments == 0 && page.Letters.Count == 0 && largestImage == 0 ? PdfPageKind.Empty : PdfPageKind.Vector;
    }
}

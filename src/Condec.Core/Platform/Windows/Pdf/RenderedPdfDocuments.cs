// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Windows.Data.Pdf;

namespace Condec.Core.Pdf;

/// <summary>
/// Keeps a <see cref="PdfDocument"/> that has rendered a page alive until the process ends.
/// Found while testing on an AMD Radeon with driver 30.0.13044.3001 (atidxx64.dll): once a PdfDocument that has rendered a
/// page is destroyed (by the garbage collector's finalizer, or by releasing it by hand), the process crashes with an access
/// violation a little later, usually when it exits. A document that is never destroyed doesn't. Nothing is locked by keeping
/// it (the source file can be deleted), so the price is the memory of the document, and the number kept is capped.
/// </summary>
internal static class RenderedPdfDocuments
{
    private const int MaximumKept = 64;

    private static readonly List<PdfDocument> Kept = [];

    public static void Keep(PdfDocument document)
    {
        lock (Kept)
        {
            if (Kept.Count < MaximumKept)
            {
                Kept.Add(document);
            }
        }
    }
}

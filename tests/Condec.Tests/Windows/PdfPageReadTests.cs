// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Architecture;

namespace Condec.Tests.Architecture;

/// <summary>Each page of a PDF in the Architecture queue is read on its own (owner decision 2026-10-03).</summary>
public sealed class PdfPageReadTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Page 1 is blank, page 2 is filled black.</summary>
    private string TwoPages() => TestPdf.WritePages(_dir.File("gambar.pdf"), ["", "0 0 200 100 re f\n"]);

    private static double DarkShare(byte[] bgra)
    {
        var dark = 0;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            if (bgra[i] < 64 && bgra[i + 1] < 64 && bgra[i + 2] < 64)
            {
                dark++;
            }
        }

        return dark / (bgra.Length / 4.0);
    }

    [Fact]
    public async Task ThePictureReaderReadsTheChosenPage()
    {
        var path = TwoPages();

        var first = await WindowsPictureReader.ReadAsync(path, Ct);
        var second = await WindowsPictureReader.ReadAsync(path, Ct, pageNumber: 2);

        Assert.True(DarkShare(first.Bgra) < 0.05);
        Assert.True(DarkShare(second.Bgra) > 0.95);
        Assert.Equal(WindowsPictureReader.PdfDpi, second.DpiX);
    }

    [Fact]
    public async Task ThePreviewDrawsTheChosenPage()
    {
        var path = TwoPages();

        var first = await ArchitectureFiles.RenderPdfAsync(path, Ct);
        var second = await ArchitectureFiles.RenderPdfAsync(path, Ct, pageNumber: 2);

        Assert.True(DarkShare(first.Bgra) < 0.05);
        Assert.True(DarkShare(second.Bgra) > 0.95);
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Condec.Core.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Core.Pdf;

/// <summary>One PDF page to PNG, JPG or HEIC (when HEIC works on this machine), rendered at 200 dpi.</summary>
public sealed class PdfToImageConverter : IConverter
{
    public const double Dpi = 200;

    private static readonly string[] Extensions = [".png", ".jpg", ".heic"];

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        FileExtension.Normalize(sourceExtension) == ".pdf"
            ? [.. ImageFormats.Targets.Select(target => target.Extension).Where(Extensions.Contains)]
            : [];

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var target = ImageFormats.FindTarget(FileExtension.Normalize(request.TargetExtension))
            ?? throw new NotSupportedException($"'{request.TargetExtension}' is not an image target.");
        var pageNumber = (request.Options as PdfPageOptions)?.PageNumber ?? 1;

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0, Loc.Format("Progress.RenderingPage", pageNumber)));

        // PdfPig refuses every encrypted PDF, including ones Windows would render without a password.
        var pageCount = PdfInspector.CountPages(request.SourcePath);
        if (pageNumber < 1 || pageNumber > pageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(request), $"Page {pageNumber} is outside 1..{pageCount}.");
        }

        var page = await PdfPageRenderer.RenderAsync(request.SourcePath, pageNumber, Dpi / 72, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0));
        using var staging = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(target.EncoderId, staging).AsTask(ct).ConfigureAwait(false);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, page.Width, page.Height, Dpi, Dpi, page.Bgra);
        await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 0.6));

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        await encoded.CopyToAsync(request.Output, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }
}

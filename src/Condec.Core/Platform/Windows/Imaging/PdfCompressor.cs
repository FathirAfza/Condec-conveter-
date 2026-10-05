// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Compression;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Localization;

namespace Condec.Core.Imaging;

/// <summary>
/// Compress (DESIGN §6.5) for a PDF: the PDF stays a PDF, and its pictures are encoded again as smaller JPEGs
/// (<see cref="PdfCompressSource"/>). Text, lines, fonts, bookmarks, links and form fields stay as they were.
/// </summary>
public sealed class PdfCompressor(IPdfPictureEncoder? encoder = null) : IConverter
{
    private readonly IPdfPictureEncoder _encoder = encoder ?? new PdfPictureEncoder();

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        FileExtension.Normalize(sourceExtension) == ".pdf" ? [".pdf"] : [];

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var options = request.Options as CompressOptions
            ?? throw new ArgumentException("Compress needs CompressOptions.", nameof(request));

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0));
        var source = await Task.Run(() => PdfCompressSource.Load(request.SourcePath, _encoder), ct).ConfigureAwait(false);
        if (source.PictureCount == 0)
        {
            throw new PdfNothingToCompressException();
        }

        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));
        progress.Report(new ConversionProgress(ConversionStage.Encode, 0));

        var result = options.TargetBytes is { } limit
            ? await source.FitAsync(limit, ct).ConfigureAwait(false) ?? throw new CompressTargetTooSmallException(limit, isPdf: true)
            : await source.EncodeAsync(options.Quality, options.ResolutionPercent, ct).ConfigureAwait(false);

        if (result.Data.LongLength >= source.FileLength)
        {
            request.Notes.Add(NoteSeverity.Informational, Loc.Format(
                "Note.CompressNotSmaller",
                DisplayFormat.FormatFileSize(source.FileLength),
                DisplayFormat.FormatFileSize(result.Data.LongLength)));
        }

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0.6));
        await request.Output.WriteAsync(result.Data, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }
}

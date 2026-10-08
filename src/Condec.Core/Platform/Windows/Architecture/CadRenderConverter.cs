// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Condec.Core.Pdf;

namespace Condec.Core.Architecture;

/// <summary>
/// A DWG or DXF drawing to PDF, PNG or JPG (DESIGN §6.3.2). The drawing is read with ACadSharp, flattened to shapes and text,
/// written as a vector PDF on A4 or A3, and for a picture result that PDF is drawn at the chosen resolution with the
/// Windows PDF renderer. LibreOffice is not involved: the user chooses layers, paper and background, which it can't.
/// </summary>
public sealed class CadRenderConverter : IConverter
{
    private static readonly string[] Targets = [".pdf", ".png", ".jpg"];

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        FileExtension.Normalize(sourceExtension) is ".dwg" or ".dxf" ? Targets : [];

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var options = request.Options as CadRenderOptions ?? new CadRenderOptions(PaperSize.A3, 300, false, new HashSet<string>());
        var target = FileExtension.Normalize(request.TargetExtension);
        if (!Targets.Contains(target))
        {
            throw new NotSupportedException($"'{request.TargetExtension}' is not a result a drawing can be rendered to.");
        }

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0, Loc.Get("Progress.ReadingDrawing")));
        var scene = await Task.Run(() => CadFlattener.Flatten(CadFiles.Read(request.SourcePath, FileExtension.Normalize(request.SourceExtension)), ct), ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 0.6, Loc.Get("Progress.DrawingSheet")));

        var pdf = await Task.Run(() => CadPdfWriter.Write(scene, name => !options.HiddenLayers.Contains(name), options.Paper), ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));
        ct.ThrowIfCancellationRequested();

        if (target == ".pdf")
        {
            progress.Report(new ConversionProgress(ConversionStage.Encode, 0));
            await request.Output.WriteAsync(pdf, ct).ConfigureAwait(false);
            progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
            return;
        }

        // A picture: the PDF drawn at the chosen resolution, through a temporary file Windows can open.
        var temporary = Path.Combine(Path.GetTempPath(), "condec-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await File.WriteAllBytesAsync(temporary, pdf, ct).ConfigureAwait(false);
            progress.Report(new ConversionProgress(ConversionStage.Encode, 0, Loc.Format("Progress.RenderingPage", 1)));

            var imageTarget = ImageFormats.FindTarget(target) ?? throw new NotSupportedException($"'{target}' is not an image target.");
            var transparent = options.TransparentBackground && imageTarget.KeepsTransparency;
            var page = await PdfPageRenderer.RenderAsync(temporary, 1, options.Dpi / 72.0, ct, transparent).ConfigureAwait(false);
            progress.Report(new ConversionProgress(ConversionStage.Encode, 0.5));

            using var staging = await ImageConverter.EncodeAsync(imageTarget, page.Bgra, page.Width, page.Height, options.Dpi, options.Dpi, ct).ConfigureAwait(false);
            progress.Report(new ConversionProgress(ConversionStage.Encode, 0.8));

            using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
            await encoded.CopyToAsync(request.Output, ct).ConfigureAwait(false);
            progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
            }
        }
    }
}

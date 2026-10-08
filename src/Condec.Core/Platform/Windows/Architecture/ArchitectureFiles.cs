// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;
using Condec.Core.Formats;
using Condec.Core.Imaging;
using Condec.Core.Pdf;

namespace Condec.Core.Architecture;

/// <summary>A picture ready to show: straight BGRA pixels, top row first.</summary>
public sealed record PreviewImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// What the Architecture page needs from files, in one place: reading a drawing, drawing a preview of it, and writing the
/// picture an upscale starts from. The converters in this folder do the same work for the conversions themselves.
/// </summary>
public static class ArchitectureFiles
{
    /// <summary>The longer side of a preview, in pixels: sharp on a large screen, small enough to keep in memory.</summary>
    public const int PreviewLongSide = 1600;

    /// <summary>Reads a DWG or DXF and flattens it to shapes and text. Throws what the CAD reader throws for a file it can't read.</summary>
    public static Task<CadScene> ReadDrawingAsync(string path, CancellationToken ct) =>
        Task.Run(() => CadFlattener.Flatten(CadFiles.Read(path, FileExtension.FromPath(path)), ct), ct);

    /// <summary>The drawing as it would be on paper, with the layers <paramref name="visible"/> says yes to.</summary>
    public static async Task<PreviewImage> RenderDrawingAsync(CadScene scene, Func<string, bool> visible, PaperSize paper, CancellationToken ct)
    {
        var pdf = await Task.Run(() => CadPdfWriter.Write(scene, visible, paper), ct).ConfigureAwait(false);
        var temporary = Path.Combine(Path.GetTempPath(), "condec-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            await File.WriteAllBytesAsync(temporary, pdf, ct).ConfigureAwait(false);
            return await RenderPdfAsync(temporary, ct).ConfigureAwait(false);
        }
        finally
        {
            Delete(temporary);
        }
    }

    /// <summary>A page of a PDF, at preview size.</summary>
    /// <param name="pageNumber">The page to draw, 1 for the first.</param>
    public static async Task<PreviewImage> RenderPdfAsync(string path, CancellationToken ct, int pageNumber = 1)
    {
        var page = await PdfPageRenderer.RenderAsync(path, pageNumber, 1, ct, fitLongSide: PreviewLongSide).ConfigureAwait(false);
        return new PreviewImage((int)page.Width, (int)page.Height, page.Bgra);
    }

    /// <summary>Saves a picture as a PNG at <paramref name="path"/>, the way an upscale takes its input.</summary>
    public static async Task WritePngAsync(RasterPicture picture, string path, CancellationToken ct)
    {
        var target = ImageFormats.FindTarget(".png") ?? throw new NotSupportedException("PNG is not an image target.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var staging = await ImageConverter.EncodeAsync(target, picture.Bgra, (uint)picture.Width, (uint)picture.Height, picture.DpiX, picture.DpiY, ct).ConfigureAwait(false);
        await using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        await encoded.CopyToAsync(output, ct).ConfigureAwait(false);
    }

    /// <summary>A path in the render cache (the folder "Clear cache" in Settings empties) for a file that lives for one operation. The folder is made.</summary>
    public static string CachePath(string extension)
    {
        var folder = Path.Combine(CondecPaths.RenderCacheDirectory, "architecture");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
    }

    /// <summary>Removes a temporary file; one that can't be removed now is left for "Clear cache".</summary>
    public static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

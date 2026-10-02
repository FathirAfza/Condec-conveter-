// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Types.Units;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Localization;
using CSMath;

namespace Condec.Core.Cad;

/// <param name="Gray">The picture in grayscale, transparent areas already on white.</param>
/// <param name="DpiX">Pixels per inch across, as the file states it; 0 when it doesn't.</param>
/// <param name="DpiY">Pixels per inch down.</param>
public sealed record GrayPicture(GrayImage Gray, double DpiX, double DpiY);

/// <summary>Reads a picture file for tracing. Implemented with the Windows Imaging Component on Windows.</summary>
public interface IImageRasterizer
{
    /// <summary>Whether this machine can decode files with this extension (HEIC and WebP need optional Windows codecs).</summary>
    bool CanDecode(string extension);

    Task<GrayPicture> ReadAsync(string path, CancellationToken ct);
}

/// <summary>
/// A picture (PNG, JPG, HEIC…) to DXF or DWG. The dark shapes are traced into closed LWPOLYLINE outlines, the way
/// a scanned PDF page is. The drawing is in millimeters, sized from the picture's resolution.
/// </summary>
/// <remarks>
/// A DWG goes through a DXF first: the traced drawing is written as DXF, read back, and that DXF is converted
/// to DWG, the same steps as converting the DXF file yourself.
/// </remarks>
public sealed class ImageToCadConverter(IImageRasterizer rasterizer) : IConverter
{
    /// <summary>The longest side tracing works on, in pixels. A larger picture is averaged down first: tracing a 12 MP photo pixel by pixel would take minutes and memory for no visible gain.</summary>
    internal const int MaxTracePixels = 3000;

    /// <summary>How far a traced outline may deviate from the pixel contour, in pixels.</summary>
    internal const double TraceTolerancePixels = 1.0;

    /// <summary>The resolution assumed when the file doesn't state a usable one: the Windows default, 96 pixels per inch.</summary>
    internal const double DefaultDpi = 96;

    internal const double MillimetersPerInch = 25.4;

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        rasterizer.CanDecode(FileExtension.Normalize(sourceExtension)) ? [".dxf", ".dwg"] : [];

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var target = FileExtension.Normalize(request.TargetExtension);

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0, Loc.Get("Progress.ReadingImage")));
        var image = await rasterizer.ReadAsync(request.SourcePath, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0.4, Loc.Get("Progress.TracingOutlines")));
        var (cad, outlineCount) = await Task.Run(() => Trace(image, ct), ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));
        ct.ThrowIfCancellationRequested();

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0, Loc.Format("Progress.WritingDxf", outlineCount)));
        var bytes = CadFiles.Write(cad, ".dxf");
        if (target == ".dwg")
        {
            progress.Report(new ConversionProgress(ConversionStage.Encode, 0.5, Loc.Get("Progress.ConvertingDxfToDwg")));
            bytes = CadFiles.Write(CadFiles.ReadDxf(bytes), ".dwg");
        }

        await request.Output.WriteAsync(bytes, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }

    /// <summary>The traced drawing, and how many outlines it holds.</summary>
    internal static (CadDocument Cad, int OutlineCount) Trace(GrayPicture image, CancellationToken ct)
    {
        var (gray, reduction) = image.Gray.ReduceTo(MaxTracePixels);
        gray = gray.DarkOnLight();

        var outlines = ScanVectorizer.Trace(gray, TraceTolerancePixels, ct);
        if (outlines.Count == 0)
        {
            throw new NothingToTraceException("The image has no shapes to trace.");
        }

        // Pixel rows run down the picture as shown; drawing Y runs up. One traced pixel stands for
        // `reduction` original pixels, each 25.4 / dpi millimeters wide (or high).
        var mmPerPixelX = MillimetersPerInch / UsableDpi(image.DpiX) * reduction;
        var mmPerPixelY = MillimetersPerInch / UsableDpi(image.DpiY) * reduction;

        var cad = new CadDocument(PdfToCadConverter.OutputVersion);
        cad.Header.InsUnits = UnitsType.Millimeters;
        foreach (var outline in outlines)
        {
            var points = outline.Select(p => (IVector)new XY(p.X * mmPerPixelX, (gray.Height - p.Y) * mmPerPixelY));
            cad.Entities.Add(new LwPolyline(points) { IsClosed = true });
        }

        PdfToCadConverter.SetExtents(cad);
        return (cad, outlines.Count);
    }

    /// <summary>A resolution of 0, negative or not a number means the file doesn't say; absurd ones are held to a range a drawing can use.</summary>
    internal static double UsableDpi(double dpi) =>
        double.IsFinite(dpi) && dpi > 0 ? Math.Clamp(dpi, 10, 2400) : DefaultDpi;
}

internal static class GrayImageTracing
{
    /// <summary>
    /// The picture averaged down by a whole factor so neither side is longer than <paramref name="maxSide"/>,
    /// and that factor (1 when the picture already fits).
    /// </summary>
    public static (GrayImage Image, int Factor) ReduceTo(this GrayImage image, int maxSide)
    {
        var factor = (int)Math.Ceiling((double)Math.Max(image.Width, image.Height) / maxSide);
        if (factor <= 1)
        {
            return (image, 1);
        }

        var width = (image.Width + factor - 1) / factor;
        var height = (image.Height + factor - 1) / factor;
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0;
                var count = 0;
                for (var sy = y * factor; sy < Math.Min(image.Height, (y + 1) * factor); sy++)
                {
                    for (var sx = x * factor; sx < Math.Min(image.Width, (x + 1) * factor); sx++)
                    {
                        sum += image[sx, sy];
                        count++;
                    }
                }

                pixels[(y * width) + x] = (byte)((sum + (count / 2)) / count);
            }
        }

        return (new GrayImage(width, height, pixels), factor);
    }

    /// <summary>
    /// The picture with light shapes on a dark background flipped, so the shapes are what gets traced and the
    /// background isn't: ink is the smaller part of a picture.
    /// </summary>
    public static GrayImage DarkOnLight(this GrayImage image)
    {
        var threshold = ScanVectorizer.OtsuThreshold(image.Pixels);
        var dark = 0L;
        foreach (var value in image.Pixels)
        {
            if (value < threshold)
            {
                dark++;
            }
        }

        if (dark * 2 <= image.Pixels.Length)
        {
            return image;
        }

        var flipped = new byte[image.Pixels.Length];
        for (var i = 0; i < flipped.Length; i++)
        {
            flipped[i] = (byte)(255 - image.Pixels[i]);
        }

        return image with { Pixels = flipped };
    }
}

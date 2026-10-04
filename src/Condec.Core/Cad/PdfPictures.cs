// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.IO.Compression;
using ACadSharp.Entities;
using Condec.Core.Architecture;
using Condec.Core.Imaging;
using Condec.Core.Pdf;
using CSMath;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace Condec.Core.Cad;

/// <summary>Turns the file bytes of a picture (PNG or JPEG) into pixels. The app uses the Windows Imaging Component.</summary>
public interface IPictureDecoder
{
    /// <summary>
    /// The picture as straight-alpha BGRA, averaged down to at most <paramref name="maximumPixels"/>; null when it can't be
    /// read.
    /// </summary>
    Task<RasterPicture?> DecodeAsync(byte[] encoded, int maximumPixels, CancellationToken ct);
}

/// <summary>
/// The pictures of a vector PDF page (logos, stamps, photos) as solid HATCH areas in their own colors, the way a logo in a
/// picture becomes CAD (DESIGN §6.3.3, owner decision 2026-10-04). Each picture is read, reduced to a few colors by
/// <see cref="LogoPainter"/>, and placed where the page shows it. White paper inside a picture stays open.
/// </summary>
internal static class PdfPictures
{
    /// <summary>A picture pixel lighter than this gray level and not colored is paper. `[ASUMSI]`</summary>
    internal const byte PaperGray = 230;

    /// <summary>The pictures painted, and how many were left out because they could not be read.</summary>
    internal sealed record Result(List<Entity> Entities, int LeftOut);

    /// <param name="decoder">Reads the pictures; without one every picture on the page is left out.</param>
    internal static async Task<Result> ReadAsync(Page page, CadOptions options, IPictureDecoder? decoder, CancellationToken ct)
    {
        var transform = new PageTransform(options.UnitsPerPoint);
        var pageBox = new Rect(0, 0, page.Width, page.Height);
        var pageArea = page.Width * page.Height;
        var entities = new List<Entity>();
        var leftOut = 0;
        foreach (var image in page.GetImages())
        {
            ct.ThrowIfCancellationRequested();
            var box = PdfToCadConverter.ToRect(image.BoundingBox);
            if (!pageBox.Intersects(box))
            {
                continue;
            }

            // A picture under the whole page is a background or a scan, not something drawn on it.
            var shown = box.Intersect(pageBox);
            var covers = (shown.MaxX - shown.MinX) * (shown.MaxY - shown.MinY) >= PdfToCadConverter.BackgroundCoverage * pageArea;
            var picture = decoder is null || covers ? null : await ReadAsync(image, decoder, ct).ConfigureAwait(false);
            if (picture is null)
            {
                leftOut++;
                continue;
            }

            // Pixel (x, y up) to the page: along the picture's bottom edge and up its left edge, so a turned picture turns too.
            var (origin, right, up) = (image.BoundingBox.BottomLeft, image.BoundingBox.BottomRight, image.BoundingBox.TopLeft);
            XY Map((double X, double Y) p)
            {
                var (u, v) = (p.X / picture.Width, p.Y / picture.Height);
                return transform.Map(
                    origin.X + (u * (right.X - origin.X)) + (v * (up.X - origin.X)),
                    origin.Y + (u * (right.Y - origin.Y)) + (v * (up.Y - origin.Y)));
            }

            var fills = LogoPainter.Paint(picture, 1, PaperGray, new PixelRect(0, 0, picture.Width, picture.Height), picture.Height, ct);
            foreach (var fill in fills.OfType<FillPrimitive>())
            {
                if (ArchitectureCadBuilder.ToHatch(fill, Map) is { } hatch)
                {
                    entities.Add(hatch);
                }
            }
        }

        return new Result(entities, leftOut);
    }

    /// <summary>The picture's pixels on white, with its soft mask applied; null when it can't be read.</summary>
    private static async Task<RasterPicture?> ReadAsync(IPdfImage image, IPictureDecoder decoder, CancellationToken ct)
    {
        if (image.IsImageMask || Encoded(image) is not { } encoded)
        {
            return null;
        }

        var picture = await decoder.DecodeAsync(encoded, LogoPainter.MaximumPixels, ct).ConfigureAwait(false);
        if (picture is null || picture.Width <= 0 || picture.Height <= 0)
        {
            return null;
        }

        // A soft mask that the decoded picture doesn't carry yet: its gray level is the opacity.
        if (image.MaskImage is { } mask && IsOpaque(picture) && mask.TryGetPng(out var maskPng)
            && await decoder.DecodeAsync(maskPng, LogoPainter.MaximumPixels, ct).ConfigureAwait(false) is { Width: > 0, Height: > 0 } alpha)
        {
            ApplyMask(picture, alpha);
        }

        Bgra.FlattenOntoWhite(picture.Bgra);
        return picture;
    }

    /// <summary>
    /// The picture as a file the decoder reads: a PNG when PdfPig can make one, otherwise the JPEG inside, unpacked from Flate
    /// when the producer packed it twice. Other encodings (JPEG 2000, CCITT, a CMYK JPEG) give null.
    /// </summary>
    internal static byte[]? Encoded(IPdfImage image)
    {
        if (image.TryGetPng(out var png))
        {
            return png;
        }

        var filters = Names(image.ImageDictionary, NameToken.Filter, NameToken.F);
        if (filters.Count == 0 || filters[^1] != "DCTDecode" || filters.Take(filters.Count - 1).Any(f => f != "FlateDecode"))
        {
            return null;
        }

        // CMYK JPEGs in PDFs are often stored inverted (Adobe); their colors can't be trusted.
        if (image.ColorSpaceDetails?.NumberOfColorComponents == 4)
        {
            return null;
        }

        // A Flate step with a predictor is not plain zlib data.
        if (filters.Count > 1 && HasPredictor(image.ImageDictionary))
        {
            return null;
        }

        try
        {
            var bytes = image.RawMemory.ToArray();
            for (var i = 0; i < filters.Count - 1; i++)
            {
                using var input = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var output = new MemoryStream();
                input.CopyTo(output);
                bytes = output.ToArray();
            }

            return bytes;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static List<string> Names(DictionaryToken dictionary, NameToken key, NameToken shortKey)
    {
        var token = dictionary.Data.GetValueOrDefault(key.Data) ?? dictionary.Data.GetValueOrDefault(shortKey.Data);
        return token switch
        {
            NameToken name => [name.Data],
            ArrayToken array => array.Data.OfType<NameToken>().Select(n => n.Data).ToList(),
            _ => [],
        };
    }

    private static bool HasPredictor(DictionaryToken dictionary)
    {
        var token = dictionary.Data.GetValueOrDefault(NameToken.DecodeParms.Data) ?? dictionary.Data.GetValueOrDefault(NameToken.Dp.Data);
        IEnumerable<IToken> parameters = token switch
        {
            DictionaryToken one => [one],
            ArrayToken many => many.Data,
            _ => [],
        };
        return parameters.OfType<DictionaryToken>()
            .Any(p => p.Data.GetValueOrDefault(NameToken.Predictor.Data) is NumericToken { Int: > 1 });
    }

    private static bool IsOpaque(RasterPicture picture)
    {
        for (var i = 3; i < picture.Bgra.Length; i += 4)
        {
            if (picture.Bgra[i] != 255)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Sets each pixel's opacity to the mask's gray level there; the mask is stretched to the picture's size.</summary>
    private static void ApplyMask(RasterPicture picture, RasterPicture mask)
    {
        for (var y = 0; y < picture.Height; y++)
        {
            var my = Math.Min(mask.Height - 1, (int)((y + 0.5) * mask.Height / picture.Height));
            for (var x = 0; x < picture.Width; x++)
            {
                var mx = Math.Min(mask.Width - 1, (int)((x + 0.5) * mask.Width / picture.Width));
                var at = ((my * mask.Width) + mx) * 4;
                var gray = ((299 * mask.Bgra[at + 2]) + (587 * mask.Bgra[at + 1]) + (114 * mask.Bgra[at]) + 500) / 1000;
                picture.Bgra[(((y * picture.Width) + x) * 4) + 3] = (byte)(gray * mask.Bgra[at + 3] / 255);
            }
        }
    }
}

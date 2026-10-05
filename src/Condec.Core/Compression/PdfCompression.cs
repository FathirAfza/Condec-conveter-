// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Condec.Core.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;
using UglyToad.PdfPig.Writer;

namespace Condec.Core.Compression;

/// <summary>A picture inside a PDF, as the encoder takes it: a JPEG file, or 8-bit samples (gray or RGB) row after row.</summary>
public sealed record PdfPictureInput(int Width, int Height, bool IsGray, bool IsJpeg, byte[] Data);

/// <summary>A picture encoded again as an RGB JPEG, and the size it now has.</summary>
public sealed record PdfPictureOutput(byte[] Jpeg, int Width, int Height);

/// <summary>Encodes a picture of a PDF again; the Windows one uses WIC.</summary>
public interface IPdfPictureEncoder
{
    /// <returns>Null when the picture can't be read.</returns>
    Task<PdfPictureOutput?> EncodeAsync(PdfPictureInput input, int quality, int resolutionPercent, CancellationToken ct);
}

/// <summary>A compressed PDF, still in memory.</summary>
/// <param name="PicturesCompressed">Pictures written smaller; the others stay as they were.</param>
/// <param name="Preview">The largest picture written smaller, as it is now in the file; null when none was.</param>
public sealed record CompressedPdf(byte[] Data, int PageCount, int PictureCount, int PicturesCompressed, CompressSetting Setting, PdfPictureOutput? Preview);

/// <summary>The PDF has no picture that can be encoded again, so it can't be made smaller.</summary>
public sealed class PdfNothingToCompressException() : Exception("The PDF has no picture that can be made smaller.");

/// <summary>
/// A PDF made smaller by encoding its pictures again (DESIGN §6.5). Every object of the file is written again under its
/// own number, so pages, text, fonts, bookmarks, links, forms and tags stay as they were; each picture that can be read is
/// written again as a JPEG at the chosen quality and resolution, only when that is smaller. Pictures that are masks of
/// others, stencils, or in encodings that can't be read stay as they are. Pictures are read one at a time while the file
/// is written, so a PDF of many scanned pages never has all of them in memory.
/// </summary>
public sealed class PdfCompressSource
{
    /// <summary>Pictures smaller than this, in pixels or in bytes, are not worth encoding again.</summary>
    internal const int MinimumSide = 32;

    internal const int MinimumBytes = 4 * 1024;

    private readonly string _path;
    private readonly IPdfPictureEncoder _encoder;
    private readonly IReadOnlyDictionary<string, Picture> _pictures;

    private PdfCompressSource(string path, IPdfPictureEncoder encoder, IReadOnlyDictionary<string, Picture> pictures, int pageCount, long fileLength)
    {
        _path = path;
        _encoder = encoder;
        _pictures = pictures;
        PageCount = pageCount;
        FileLength = fileLength;
    }

    public int PageCount { get; }

    /// <summary>The pictures that can be encoded again, each counted once however many pages show it.</summary>
    public int PictureCount => _pictures.Count;

    public long FileLength { get; }

    /// <summary>The size of the largest picture, in pixels; null when there is none.</summary>
    public (int Width, int Height)? LargestPicture =>
        _pictures.Count == 0 ? null : _pictures.Values.MaxBy(p => (long)p.Width * p.Height) is { } largest ? (largest.Width, largest.Height) : null;

    /// <summary>Finds the pictures of the PDF. Throws <see cref="LockedPdfException"/> for an encrypted file.</summary>
    public static PdfCompressSource Load(string path, IPdfPictureEncoder encoder)
    {
        using var document = PdfInspector.Open(path);
        var pictures = new Dictionary<string, Picture>(StringComparer.Ordinal);
        var masks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in document.GetPages())
        {
            foreach (var image in page.GetImages())
            {
                if (image.MaskImage is { } mask)
                {
                    masks.Add(Key(mask.RawMemory.Span, mask.WidthInSamples, mask.HeightInSamples));
                }

                if (Read(image) is { } picture)
                {
                    pictures.TryAdd(picture.Key, picture);
                }
            }
        }

        // A soft mask is opacity, not a picture: blurring it with JPEG would show as fringes.
        foreach (var mask in masks)
        {
            pictures.Remove(mask);
        }

        return new PdfCompressSource(path, encoder, pictures, document.NumberOfPages, new FileInfo(path).Length);
    }

    /// <summary>Encodes every picture at <paramref name="quality"/> and a percentage of its resolution, and writes the PDF.</summary>
    public async Task<CompressedPdf> EncodeAsync(int quality, int resolutionPercent, CancellationToken ct)
    {
        quality = Math.Clamp(quality, CompressOptions.MinimumQuality, CompressOptions.MaximumQuality);
        resolutionPercent = Math.Clamp(resolutionPercent, 1, CompressOptions.MaximumPercent);
        var (data, encoded) = await WriteAsync(quality, resolutionPercent, ct).ConfigureAwait(false);
        var written = encoded.Where(e => e.Value is not null).ToList();
        var preview = written.Count == 0 ? null : written.MaxBy(e => (long)_pictures[e.Key].Width * _pictures[e.Key].Height).Value;
        return new CompressedPdf(data, PageCount, PictureCount, written.Count, new CompressSetting(quality, resolutionPercent), preview);
    }

    /// <summary>The best result no larger than <paramref name="targetBytes"/> (<see cref="CompressSearch"/>), or null when none fits.</summary>
    public async Task<CompressedPdf?> FitAsync(long targetBytes, CancellationToken ct)
    {
        var fitting = new Dictionary<CompressSetting, CompressedPdf>();
        var found = await CompressSearch.FitAsync(
            lossy: true,
            targetBytes,
            async (setting, token) =>
            {
                var pdf = await EncodeAsync(setting.Quality, setting.ResolutionPercent, token).ConfigureAwait(false);
                if (pdf.Data.LongLength <= targetBytes)
                {
                    fitting[setting] = pdf;
                }

                return pdf.Data.LongLength;
            },
            ct).ConfigureAwait(false);
        return found is null ? null : fitting[found.Setting];
    }

    /// <summary>Non-stream objects packed into one object stream, at most.</summary>
    private const int ObjectsPerStream = 100;

    /// <summary>
    /// Writes the objects the document uses again under their own numbers, a picture as its smaller JPEG when there is
    /// one. Streams are written on their own; every other object goes into Flate-compressed object streams with a
    /// cross-reference stream (ISO 32000-1, 7.5.7 and 7.5.8), as most PDF producers write them today. Objects nothing
    /// points at, such as leftovers of earlier edits, are not copied.
    /// </summary>
    /// <returns>The file, and each picture met with the JPEG it became, or null when it stayed as it was.</returns>
    private async Task<(byte[] Data, Dictionary<string, PdfPictureOutput?> Encoded)> WriteAsync(int quality, int resolutionPercent, CancellationToken ct)
    {
        using var document = PdfInspector.Open(_path);
        var structure = document.Structure;
        var root = structure.Trailer.Root;
        var information = structure.Trailer.Info is IndirectReferenceToken info ? info.Data : (IndirectReference?)null;
        var objects = Reachable(structure, information is { } i ? [root, i] : [root], ct);

        var writer = new TokenWriter();
        var encoded = new Dictionary<string, PdfPictureOutput?>(StringComparer.Ordinal);
        var entries = new Dictionary<long, (int Type, long Field2, int Field3)>();
        var packed = new List<(long Number, byte[] Text)>();
        using var output = new MemoryStream();

        // Object streams need PDF 1.5. The binary comment tells transfer programs the file is not text (7.5.2).
        var version = Math.Max(document.Version, 1.5);
        output.Write(Encoding.ASCII.GetBytes($"%PDF-{version.ToString("0.0", CultureInfo.InvariantCulture)}\n"));
        output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        foreach (var (reference, data) in objects)
        {
            ct.ThrowIfCancellationRequested();
            if (data is StreamToken stream)
            {
                var written = await PictureAsync(structure, stream, quality, resolutionPercent, encoded, ct).ConfigureAwait(false) ?? WithLength(stream);
                entries[reference.ObjectNumber] = (1, output.Position, reference.Generation);
                writer.WriteToken(new ObjectToken(XrefLocation.File(output.Position), reference, written), output);
            }
            else if (reference.Generation == 0)
            {
                using var text = new MemoryStream();
                writer.WriteToken(data, text);
                packed.Add((reference.ObjectNumber, text.ToArray()));
            }
            else
            {
                // An object stream holds generation 0 only (7.5.7).
                entries[reference.ObjectNumber] = (1, output.Position, reference.Generation);
                writer.WriteToken(new ObjectToken(XrefLocation.File(output.Position), reference, data), output);
            }
        }

        var next = objects.Count == 0 ? 1 : objects.Max(o => o.Reference.ObjectNumber) + 1;
        foreach (var chunk in packed.Chunk(ObjectsPerStream))
        {
            var number = next++;
            var header = new StringBuilder();
            using var body = new MemoryStream();
            for (var index = 0; index < chunk.Length; index++)
            {
                header.Append(CultureInfo.InvariantCulture, $"{chunk[index].Number} {body.Length} ");
                body.Write(chunk[index].Text);
                body.WriteByte((byte)'\n');
                entries[chunk[index].Number] = (2, number, index);
            }

            var first = Encoding.ASCII.GetBytes(header.ToString());
            var packedStream = Deflate([.. first, .. body.ToArray()]);
            var dictionary = new DictionaryToken(new Dictionary<NameToken, IToken>
            {
                [NameToken.Type] = NameToken.ObjStm,
                [NameToken.N] = new NumericToken(chunk.Length),
                [NameToken.First] = new NumericToken(first.Length),
                [NameToken.Filter] = NameToken.FlateDecode,
                [NameToken.Length] = new NumericToken(packedStream.Length),
            });
            entries[number] = (1, output.Position, 0);
            writer.WriteToken(new ObjectToken(XrefLocation.File(output.Position), new IndirectReference(number, 0), new StreamToken(dictionary, packedStream)), output);
        }

        // Only an information dictionary that was copied: its number may otherwise belong to a new object stream.
        var copied = information is { } kept && objects.Exists(o => o.Reference.Equals(kept)) ? kept : (IndirectReference?)null;
        WriteCrossReferenceStream(writer, output, entries, next, root, copied);
        return (output.ToArray(), encoded);
    }

    /// <summary>The cross-reference stream as the last object, then where it starts (7.5.8).</summary>
    private static void WriteCrossReferenceStream(
        TokenWriter writer,
        MemoryStream output,
        Dictionary<long, (int Type, long Field2, int Field3)> entries,
        long number,
        IndirectReference root,
        IndirectReference? information)
    {
        var start = output.Position;
        entries[number] = (1, start, 0);
        var size = number + 1;
        var width = 1;
        while (entries.Values.Max(e => e.Field2) >= 1L << (8 * width))
        {
            width++;
        }

        var rows = new byte[size * (1 + width + 2)];
        for (long n = 0, at = 0; n < size; n++)
        {
            // Object 0 heads the list of free objects; numbers not written read as free too.
            var (type, field2, field3) = entries.TryGetValue(n, out var entry) ? entry : (0, 0L, n == 0 ? 65535 : 0);
            rows[at++] = (byte)type;
            for (var b = width - 1; b >= 0; b--)
            {
                rows[at++] = (byte)(field2 >> (8 * b));
            }

            rows[at++] = (byte)(field3 >> 8);
            rows[at++] = (byte)field3;
        }

        var data = Deflate(rows);
        var dictionary = new Dictionary<NameToken, IToken>
        {
            [NameToken.Type] = NameToken.Xref,
            [NameToken.Size] = new NumericToken(size),
            [NameToken.W] = new ArrayToken([new NumericToken(1), new NumericToken(width), new NumericToken(2)]),
            [NameToken.Root] = new IndirectReferenceToken(root),
            [NameToken.Filter] = NameToken.FlateDecode,
            [NameToken.Length] = new NumericToken(data.Length),
        };
        if (information is { } info)
        {
            dictionary[NameToken.Info] = new IndirectReferenceToken(info);
        }

        writer.WriteToken(new ObjectToken(XrefLocation.File(start), new IndirectReference(number, 0), new StreamToken(new DictionaryToken(dictionary), data)), output);
        output.Write(Encoding.ASCII.GetBytes($"startxref\n{start.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n"));
    }

    /// <summary>
    /// The objects reachable from <paramref name="roots"/>, in number order. The length of a stream is not followed: it is
    /// written into the stream's own dictionary. Object and cross-reference streams of the source are never reached.
    /// </summary>
    private static List<(IndirectReference Reference, IToken Data)> Reachable(Structure structure, IReadOnlyList<IndirectReference> roots, CancellationToken ct)
    {
        var found = new Dictionary<IndirectReference, IToken>();
        var pending = new Stack<IndirectReference>(roots);
        var tokens = new Stack<IToken>();
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var reference = pending.Pop();
            if (found.ContainsKey(reference))
            {
                continue;
            }

            IToken data;
            try
            {
                data = structure.GetObject(reference).Data;
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
            {
                // A reference to nothing readable reads as null (7.3.10); it stays as it is.
                continue;
            }

            found[reference] = data;
            tokens.Push(data);
            while (tokens.Count > 0)
            {
                switch (tokens.Pop())
                {
                    case IndirectReferenceToken next:
                        pending.Push(next.Data);
                        break;
                    case ArrayToken array:
                        foreach (var item in array.Data) { tokens.Push(item); }
                        break;
                    case DictionaryToken dictionary:
                        dictionary.Data.Values.ToList().ForEach(tokens.Push);
                        break;
                    case StreamToken stream:
                        stream.StreamDictionary.Data.Where(p => p.Key != NameToken.Length.Data).Select(p => p.Value).ToList().ForEach(tokens.Push);
                        break;
                }
            }
        }

        return [.. found.OrderBy(o => o.Key.ObjectNumber).ThenBy(o => o.Key.Generation).Select(o => (o.Key, o.Value))];
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>The stream as a smaller JPEG, or null when it is not one of the pictures or the JPEG would not be smaller.</summary>
    private async Task<StreamToken?> PictureAsync(
        Structure structure,
        StreamToken stream,
        int quality,
        int resolutionPercent,
        Dictionary<string, PdfPictureOutput?> encoded,
        CancellationToken ct)
    {
        var dictionary = stream.StreamDictionary;
        if (_pictures.Count == 0
            || dictionary.Data.GetValueOrDefault(NameToken.Subtype.Data) is not NameToken { Data: "Image" }
            || dictionary.Data.GetValueOrDefault(NameToken.Width.Data) is not NumericToken width
            || dictionary.Data.GetValueOrDefault(NameToken.Height.Data) is not NumericToken height
            || !_pictures.TryGetValue(Key(stream.Data.Span, width.Int, height.Int), out var picture)

            // The same bytes may be drawn by another object that inverts or masks them; that one stays as it is.
            || dictionary.ContainsKey(NameToken.Mask) || dictionary.ContainsKey(NameToken.ImageMask) || HasCustomDecode(dictionary))
        {
            return null;
        }

        if (!encoded.TryGetValue(picture.Key, out var output))
        {
            output = Input(structure, stream, picture) is { } input
                && await _encoder.EncodeAsync(input, quality, resolutionPercent, ct).ConfigureAwait(false) is { } jpeg
                && jpeg.Jpeg.Length < stream.Data.Length
                    ? jpeg
                    : null;
            encoded[picture.Key] = output;
        }

        return output is null ? null : Replacement(dictionary, output, picture.IsGray);
    }

    /// <summary>The picture as the encoder takes it, read again from its stream; null when it can't be read.</summary>
    private static PdfPictureInput? Input(Structure structure, StreamToken stream, Picture picture)
    {
        var raw = stream.Data.ToArray();
        if (picture.FlateBeforeJpeg is { } flateSteps)
        {
            return Unpack(raw, flateSteps) is { } jpeg ? new PdfPictureInput(picture.Width, picture.Height, picture.IsGray, IsJpeg: true, jpeg) : null;
        }

        return Samples(Resolved(structure, stream.StreamDictionary), raw, picture.Width, picture.Height, picture.IsGray) is { } samples
            ? new PdfPictureInput(picture.Width, picture.Height, picture.IsGray, IsJpeg: false, samples)
            : null;
    }

    /// <summary>The filter parameters as values, not references, so the filters can read them.</summary>
    private static DictionaryToken Resolved(Structure structure, DictionaryToken dictionary)
    {
        foreach (var name in new[] { NameToken.DecodeParms, NameToken.Dp, NameToken.Filter })
        {
            if (dictionary.Data.GetValueOrDefault(name.Data) is IndirectReferenceToken reference)
            {
                dictionary = dictionary.With(name, structure.GetObject(reference.Data).Data);
            }
        }

        return dictionary;
    }

    /// <summary>The stream with its length written as a number: a length kept in another object could be stale.</summary>
    private static StreamToken WithLength(StreamToken stream) =>
        new(stream.StreamDictionary.With(NameToken.Length, new NumericToken(stream.Data.Length)), stream.Data);

    /// <summary>
    /// The picture's stream for an RGB JPEG: same soft mask, new size, no other filters. Built from the picture's own
    /// dictionary, whose references keep their numbers in the new file. A gray picture becomes DeviceRGB, because the
    /// Windows JPEG encoder writes three channels only (Gray8 is refused); its color space must match the JPEG's components.
    /// </summary>
    internal static StreamToken Replacement(DictionaryToken dictionary, PdfPictureOutput output, bool wasGray)
    {
        var updated = dictionary
            .Without(NameToken.Decode)
            .Without(NameToken.D)
            .Without(NameToken.DecodeParms)
            .Without(NameToken.Dp)
            .Without(NameToken.F)
            .With(NameToken.Filter, NameToken.DctDecode)
            .With(NameToken.Width, new NumericToken(output.Width))
            .With(NameToken.Height, new NumericToken(output.Height))
            .With(NameToken.BitsPerComponent, new NumericToken(8))
            .With(NameToken.Length, new NumericToken(output.Jpeg.Length));
        if (wasGray)
        {
            updated = updated.With(NameToken.ColorSpace, NameToken.Devicergb);
        }

        return new StreamToken(updated, output.Jpeg);
    }

    /// <summary>The same picture shown on several pages, or met again while writing, has one key.</summary>
    internal static string Key(ReadOnlySpan<byte> encoded, int width, int height) =>
        $"{width}x{height}:{Convert.ToHexString(SHA256.HashData(encoded))}";

    /// <summary>How the picture is read again while writing, or null when it is one that stays as it is.</summary>
    internal static Picture? Read(IPdfImage image)
    {
        var dictionary = image.ImageDictionary;
        if (image.IsInlineImage || image.IsImageMask || image.BitsPerComponent != 8
            || image.WidthInSamples < MinimumSide || image.HeightInSamples < MinimumSide || image.RawMemory.Length < MinimumBytes
            || dictionary.ContainsKey(NameToken.Mask) || HasCustomDecode(image)
            || image.ColorSpaceDetails is not { } colors || !IsPlainColor(colors))
        {
            return null;
        }

        var gray = colors.NumberOfColorComponents == 1;
        var filters = Filters(dictionary);
        var key = Key(image.RawMemory.Span, image.WidthInSamples, image.HeightInSamples);
        if (filters.Count > 0 && filters[^1] == NameToken.DctDecode.Data)
        {
            // A JPEG, maybe packed in Flate first; a predictor in front of the JPEG is not plain zlib data.
            var flateSteps = filters.Count - 1;
            return filters.Take(flateSteps).All(f => f == NameToken.FlateDecode.Data) && (flateSteps == 0 || !HasPredictor(dictionary))
                && Unpack(image.RawMemory.ToArray(), flateSteps) is not null
                    ? new Picture(key, image.WidthInSamples, image.HeightInSamples, gray, flateSteps)
                    : null;
        }

        if (filters.All(f => f == NameToken.FlateDecode.Data))
        {
            // Read once here to know it can be; read again while writing, so the samples are not kept.
            return Samples(dictionary, image.RawMemory.ToArray(), image.WidthInSamples, image.HeightInSamples, gray) is not null
                ? new Picture(key, image.WidthInSamples, image.HeightInSamples, gray, FlateBeforeJpeg: null)
                : null;
        }

        // JPEG 2000, JBIG2, CCITT, LZW, ASCII: left as they are.
        return null;
    }

    /// <summary>Gray, RGB, or an ICC profile of one or three components; not Lab, indexed, separations, or CMYK.</summary>
    private static bool IsPlainColor(ColorSpaceDetails colors) =>
        colors.Type is ColorSpace.DeviceGray or ColorSpace.DeviceRGB or ColorSpace.CalGray or ColorSpace.CalRGB or ColorSpace.ICCBased
        && colors.NumberOfColorComponents is 1 or 3;

    /// <summary>A Decode array other than [0 1 …] inverts or remaps the samples, which a JPEG can't carry.</summary>
    private static bool HasCustomDecode(IPdfImage image) =>
        image.Decode.Select((value, i) => value != (i % 2 == 0 ? 0 : 1)).Any(differs => differs);

    private static bool HasCustomDecode(DictionaryToken dictionary) =>
        (dictionary.Data.GetValueOrDefault(NameToken.Decode.Data) ?? dictionary.Data.GetValueOrDefault(NameToken.D.Data)) switch
        {
            null => false,
            ArrayToken array => array.Data.Select((value, i) => value is not NumericToken number || number.Double != (i % 2 == 0 ? 0 : 1)).Any(differs => differs),
            _ => true,
        };

    private static byte[]? Samples(DictionaryToken dictionary, byte[] raw, int width, int height, bool gray)
    {
        try
        {
            var samples = new StreamToken(dictionary, raw).Decode(DefaultFilterProvider.Instance);
            var expected = (long)width * height * (gray ? 1 : 3);
            return samples.Length >= expected ? samples.Span[..(int)expected].ToArray() : null;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or OperationCanceledException))
        {
            return null;
        }
    }

    private static byte[]? Unpack(byte[] bytes, int flateSteps)
    {
        try
        {
            for (var i = 0; i < flateSteps; i++)
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

    private static List<string> Filters(DictionaryToken dictionary)
    {
        var token = dictionary.Data.GetValueOrDefault(NameToken.Filter.Data) ?? dictionary.Data.GetValueOrDefault(NameToken.F.Data);
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

    /// <summary>A picture that can be encoded again, without its pixels.</summary>
    /// <param name="FlateBeforeJpeg">For a JPEG, how many Flate layers are around it; null for plain samples.</param>
    internal sealed record Picture(string Key, int Width, int Height, bool IsGray, int? FlateBeforeJpeg);
}

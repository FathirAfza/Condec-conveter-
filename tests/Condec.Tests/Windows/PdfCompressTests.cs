// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Compression;
using Condec.Core.Conversion;
using Condec.Core.Documents;
using Condec.Core.Imaging;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Tests.Imaging;

/// <summary>Compressing a PDF by encoding its pictures again (DESIGN §6.5): text and pages stay, pictures get smaller.</summary>
public sealed class PdfCompressTests : IDisposable
{
    private const int PhotoWidth = 1200;
    private const int PhotoHeight = 900;

    private readonly TempDirectory _dir = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task AScan_GetsSmaller_AndKeepsItsPagesTextAndOnePictureForBothPages()
    {
        var path = await WriteScanAsync("scan.pdf", gray: false);
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());
        Assert.Equal(2, source.PageCount);
        Assert.Equal(1, source.PictureCount);

        var result = await source.EncodeAsync(60, 50, Ct);

        Assert.True(result.Data.LongLength < source.FileLength / 2, $"{result.Data.LongLength} of {source.FileLength}");
        Assert.Equal(1, result.PicturesCompressed);
        using var document = PdfDocument.Open(result.Data);
        Assert.Equal(2, document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            Assert.Contains("Kartu Peserta", page.Text, StringComparison.Ordinal);
            var picture = Assert.Single(page.GetImages());
            Assert.Equal(PhotoWidth / 2, picture.WidthInSamples);
            Assert.Equal(PhotoHeight / 2, picture.HeightInSamples);
        }
    }

    [Fact]
    public async Task TheCompressedPages_LookLikeTheOriginal()
    {
        var path = await WriteScanAsync("look.pdf", gray: false);
        var result = await PdfCompressSource.Load(path, new PdfPictureEncoder()).EncodeAsync(60, 50, Ct);
        var compressed = _dir.File("look-small.pdf");
        await File.WriteAllBytesAsync(compressed, result.Data, Ct);

        var before = await PdfPageRenderer.RenderAsync(path, 1, 0.5, Ct);
        var after = await PdfPageRenderer.RenderAsync(compressed, 1, 0.5, Ct);

        Assert.Equal(before.Width, after.Width);
        Assert.Equal(before.Height, after.Height);
        var difference = MeanDifference(before.Bgra, after.Bgra);
        Assert.True(difference < 6, $"mean difference {difference:0.00}");
    }

    [Fact]
    public async Task AGrayPicture_BecomesAnRgbJpeg_ThatLooksTheSame()
    {
        // The Windows JPEG encoder writes three channels only, so the color space has to follow.
        var path = _dir.File("gray.pdf");
        var samples = new byte[512 * 512];
        var random = new Random(5);
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)Math.Clamp(((i % 512) / 2) + random.Next(-12, 13), 0, 255);
        }

        TestPdf.Write(path, "q 200 0 0 200 0 0 cm /Im Do Q", width: 200, height: 200, picture: ("/Width 512 /Height 512 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [0 1]", samples));
        var result = await PdfCompressSource.Load(path, new PdfPictureEncoder()).EncodeAsync(60, 100, Ct);
        var compressed = _dir.File("gray-small.pdf");
        await File.WriteAllBytesAsync(compressed, result.Data, Ct);

        using var document = PdfDocument.Open(result.Data);
        var picture = Assert.Single(document.GetPage(1).GetImages());
        Assert.Equal(1, result.PicturesCompressed);
        Assert.Equal(3, picture.ColorSpaceDetails?.NumberOfColorComponents);
        Assert.Equal(3, JpegChannels(picture.RawMemory.ToArray()));
        var before = await PdfPageRenderer.RenderAsync(path, 1, 1, Ct);
        var after = await PdfPageRenderer.RenderAsync(compressed, 1, 1, Ct);
        var difference = MeanDifference(before.Bgra, after.Bgra);
        Assert.True(difference < 6, $"mean difference {difference:0.00}");
    }

    [Fact]
    public async Task APictureThatWouldGrow_StaysAsItWas()
    {
        var path = await WriteScanAsync("small.pdf", gray: false, sourceQuality: 0.3f);
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        var result = await source.EncodeAsync(100, 100, Ct);

        Assert.Equal(0, result.PicturesCompressed);
        using var original = PdfDocument.Open(path);
        using var document = PdfDocument.Open(result.Data);
        Assert.Equal(original.GetPage(1).GetImages().Single().RawMemory.ToArray(), document.GetPage(1).GetImages().Single().RawMemory.ToArray());
    }

    [Fact]
    public async Task APngWithTransparency_KeepsItsMaskAsItWas()
    {
        var path = _dir.File("logo.pdf");
        var png = await EncodeAsync(BitmapEncoder.PngEncoderId, Photo(withHole: true), BitmapAlphaMode.Straight);
        WritePdf(path, png, isPng: true);
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        var result = await source.EncodeAsync(60, 50, Ct);

        using var original = PdfDocument.Open(path);
        using var document = PdfDocument.Open(result.Data);
        var before = original.GetPage(1).GetImages().Single();
        var after = document.GetPage(1).GetImages().Single();
        Assert.Equal(1, result.PicturesCompressed);
        Assert.Equal(PhotoWidth / 2, after.WidthInSamples);
        Assert.NotNull(after.MaskImage);
        Assert.Equal(before.MaskImage!.RawMemory.ToArray(), after.MaskImage!.RawMemory.ToArray());
        Assert.Equal(PhotoWidth, after.MaskImage.WidthInSamples);
    }

    [Fact]
    public async Task ASizeLimit_IsMetAndMeasured()
    {
        var path = await WriteScanAsync("limit.pdf", gray: false);
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());
        var roomy = await source.EncodeAsync(CompressSearch.LowestQuality, 100, Ct);
        var limit = roomy.Data.LongLength / 2;

        var result = await source.FitAsync(limit, Ct);

        Assert.NotNull(result);
        Assert.True(result.Data.LongLength <= limit);
        Assert.Equal(CompressSearch.LowestQuality, result.Setting.Quality);
        Assert.True(result.Setting.ResolutionPercent < 100);
    }

    [Fact]
    public async Task AnImpossibleLimit_GivesNothing()
    {
        var path = await WriteScanAsync("tiny.pdf", gray: false);
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        Assert.Null(await source.FitAsync(100, Ct));
    }

    [Fact]
    public async Task Pipeline_SavesAVerifiedPdfUnderTheLimit_WithBothPages()
    {
        var path = await WriteScanAsync("kartu.pdf", gray: false);
        var destination = _dir.File("kartu (dikompres).pdf");
        var limit = new FileInfo(path).Length / 3;

        var result = await CreatePipeline().RunAsync(new ConversionJob(path, ".pdf", destination, new CompressOptions(80, 100, limit)), null, Ct);

        Assert.True(new FileInfo(destination).Length <= limit);
        Assert.Equal(new FileInfo(destination).Length, result.Length);
        Assert.Empty(result.Notes ?? []);
        using var document = PdfDocument.Open(destination);
        Assert.Equal(2, document.NumberOfPages);
    }

    [Fact]
    public async Task APdfWithoutPictures_HasNothingToCompress()
    {
        var path = TestPdf.Write(_dir.File("teks.pdf"), "BT /F1 12 Tf 10 50 Td (Surat tugas) Tj ET");
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());
        var destination = _dir.File("teks (dikompres).pdf");

        var written = await source.EncodeAsync(60, 100, Ct);

        Assert.Equal(0, source.PictureCount);
        Assert.Null(source.LargestPicture);
        Assert.Null(written.Preview);
        await Assert.ThrowsAsync<PdfNothingToCompressException>(() =>
            CreatePipeline().RunAsync(new ConversionJob(path, ".pdf", destination, new CompressOptions(60, 100)), null, Ct));
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Pipeline_RefusesALimitThatCantBeMet_AndSaysItIsAPdf()
    {
        var path = await WriteScanAsync("besar.pdf", gray: false);
        var destination = _dir.File("besar (dikompres).pdf");

        var error = await Assert.ThrowsAsync<CompressTargetTooSmallException>(() =>
            CreatePipeline().RunAsync(new ConversionJob(path, ".pdf", destination, new CompressOptions(80, 100, 500)), null, Ct));

        Assert.True(error.IsPdf);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ThePreview_IsTheLargestPicture_AsWritten()
    {
        var path = await WriteScanAsync("pratinjau.pdf", gray: false);
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        var result = await source.EncodeAsync(60, 50, Ct);

        Assert.Equal((PhotoWidth, PhotoHeight), source.LargestPicture);
        Assert.NotNull(result.Preview);
        Assert.Equal(PhotoWidth / 2, result.Preview.Width);
        using var document = PdfDocument.Open(result.Data);
        Assert.Equal(result.Preview.Jpeg, document.GetPage(1).GetImages().Single().RawMemory.ToArray());
    }

    [Fact]
    public async Task APictureThatIsAlsoASoftMask_StaysAsItWas()
    {
        // /Ma is drawn on the page and is also the soft mask of /Im: as a mask it must not be blurred by JPEG.
        var path = _dir.File("mask-drawn.pdf");
        var mask = Noise(80 * 80, seed: 7);
        WriteRawPdf(path, "q 100 0 0 100 0 0 cm /Im Do Q q 100 0 0 100 100 0 cm /Ma Do Q", "/Im 6 0 R /Ma 7 0 R", new Dictionary<int, string>
        {
            [6] = RawStream("/Type /XObject /Subtype /Image /Width 80 /Height 80 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 7 0 R", Noise(80 * 80 * 3, seed: 8)),
            [7] = RawStream("/Type /XObject /Subtype /Image /Width 80 /Height 80 /ColorSpace /DeviceGray /BitsPerComponent 8", mask),
        });
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        var result = await source.EncodeAsync(60, 50, Ct);

        Assert.Equal(1, source.PictureCount);
        Assert.Equal(1, result.PicturesCompressed);
        using var document = PdfDocument.Open(result.Data);
        var pictures = document.GetPage(1).GetImages().ToList();
        Assert.Equal(40, pictures.Single(p => p.MaskImage is not null).WidthInSamples);
        Assert.Equal(mask, pictures.Single(p => p.MaskImage is null).RawMemory.ToArray());
    }

    [Fact]
    public async Task TheSameBytesWithAnInvertingDecode_StayAsTheyWere()
    {
        var path = _dir.File("decode-twice.pdf");
        var samples = Noise(80 * 80, seed: 9);
        WriteRawPdf(path, "q 100 0 0 100 0 0 cm /A Do Q q 100 0 0 100 100 0 cm /B Do Q", "/A 6 0 R /B 7 0 R", new Dictionary<int, string>
        {
            [6] = RawStream("/Type /XObject /Subtype /Image /Width 80 /Height 80 /ColorSpace /DeviceGray /BitsPerComponent 8", samples),
            [7] = RawStream("/Type /XObject /Subtype /Image /Width 80 /Height 80 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [1 0]", samples),
        });
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        var result = await source.EncodeAsync(60, 50, Ct);

        Assert.Equal(1, result.PicturesCompressed);
        using var document = PdfDocument.Open(result.Data);
        var inverted = document.GetPage(1).GetImages().Single(p => p.WidthInSamples == 80);
        Assert.Equal([1.0, 0.0], inverted.Decode);
        Assert.Equal(samples, inverted.RawMemory.ToArray());
    }

    [Fact]
    public async Task AnInformationEntryThatPointsAtNothing_IsLeftOut()
    {
        // The trailer names object 7, which isn't there; 7 is also the number the first new object stream gets.
        var path = _dir.File("info-missing.pdf");
        WriteRawPdf(
            path,
            "q 100 0 0 100 0 0 cm /Im Do Q BT /F1 12 Tf 10 10 Td (Kartu Peserta) Tj ET",
            "/Im 6 0 R",
            new Dictionary<int, string>
            {
                [6] = RawStream("/Type /XObject /Subtype /Image /Width 80 /Height 80 /ColorSpace /DeviceRGB /BitsPerComponent 8", Noise(80 * 80 * 3, seed: 10)),
            },
            trailer: " /Info 7 0 R");
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        var result = await source.EncodeAsync(60, 50, Ct);

        Assert.DoesNotContain("/Info", System.Text.Encoding.Latin1.GetString(result.Data), StringComparison.Ordinal);
        using var document = PdfDocument.Open(result.Data);
        Assert.Contains("Kartu Peserta", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(" /Decode [0 1]", true)]
    [InlineData(" /Decode [1 0]", false)]
    [InlineData(" /Mask [0 10]", false)]
    public void OnlyPicturesAJpegCanCarry_AreEncodedAgain(string extra, bool expected)
    {
        var path = _dir.File("gray-raw.pdf");
        var samples = new byte[256 * 256];
        new Random(3).NextBytes(samples);
        TestPdf.Write(path, "q 100 0 0 100 0 0 cm /Im Do Q", picture: ("/Width 256 /Height 256 /ColorSpace /DeviceGray /BitsPerComponent 8" + extra, samples));

        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        Assert.Equal(expected ? 1 : 0, source.PictureCount);
    }

    [Fact]
    public async Task BookmarksLinksAndFormFields_StayAsTheyWere_AlsoFromObjectStreams()
    {
        var path = _dir.File("structured.pdf");
        var jpeg = await EncodeAsync(BitmapEncoder.JpegEncoderId, Photo(withHole: false), BitmapAlphaMode.Ignore);
        WriteStructuredPdf(path, jpeg);
        var source = PdfCompressSource.Load(path, new PdfPictureEncoder());

        var result = await source.EncodeAsync(60, 50, Ct);
        var compressed = _dir.File("structured-small.pdf");
        await File.WriteAllBytesAsync(compressed, result.Data, Ct);

        Assert.Equal(1, result.PicturesCompressed);
        Assert.True(result.Data.LongLength < source.FileLength / 2, $"{result.Data.LongLength} of {source.FileLength}");
        using var document = PdfDocument.Open(result.Data);
        Assert.Equal(2, document.NumberOfPages);
        Assert.True(document.TryGetBookmarks(out var bookmarks));
        Assert.Equal("Bagian 2", Assert.Single(bookmarks.GetNodes()).Title);
        Assert.True(document.TryGetForm(out var form));
        Assert.Single(form.Fields);
        Assert.Equal(2, document.GetPage(1).GetAnnotations().Count());
        Assert.Contains("Kartu Peserta", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.True(document.Structure.Catalog.CatalogDictionary.ContainsKey(UglyToad.PdfPig.Tokens.NameToken.PageLabels));

        // The Windows renderer reads the rewritten file, and the page looks the same.
        var before = await PdfPageRenderer.RenderAsync(path, 1, 0.5, Ct);
        var after = await PdfPageRenderer.RenderAsync(compressed, 1, 0.5, Ct);
        var difference = MeanDifference(before.Bgra, after.Bgra);
        Assert.True(difference < 6, $"mean difference {difference:0.00}");
    }

    /// <summary>
    /// Two pages with a photo, a bookmark and a link to page 2, a text field, and page labels. The bookmarks are kept in an
    /// object stream and the cross-reference is a stream, as most PDF producers write them today.
    /// </summary>
    private static void WriteStructuredPdf(string path, byte[] jpeg)
    {
        const string Content = "q 495 0 0 371 50 300 cm /Im Do Q BT /F1 14 Tf 50 750 Td (Kartu Peserta 2026) Tj ET";
        const string PageResources = "/MediaBox [0 0 595 842] /Contents 5 0 R /Resources << /Font << /F1 6 0 R >> /XObject << /Im 11 0 R >> >>";
        string Stream(string dictionary, string data) => $"<< {dictionary} /Length {data.Length} >>\nstream\n{data}\nendstream";

        var inStream = new[]
        {
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>",
            "<< /Title (Bagian 2) /Parent 7 0 R /Dest [4 0 R /Fit] >>",
        };
        var header = $"7 0 8 {inStream[0].Length + 1} ";
        var objects = new Dictionary<int, string>
        {
            [1] = "<< /Type /Catalog /Pages 2 0 R /Outlines 7 0 R /AcroForm << /Fields [10 0 R] >> /PageLabels << /Nums [0 << /S /r >>] >> >>",
            [2] = "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            [3] = $"<< /Type /Page /Parent 2 0 R {PageResources} /Annots [9 0 R 10 0 R] >>",
            [4] = $"<< /Type /Page /Parent 2 0 R {PageResources} >>",
            [5] = Stream("", Content),
            [6] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            [9] = "<< /Type /Annot /Subtype /Link /Rect [50 700 200 720] /Border [0 0 0] /Dest [4 0 R /Fit] >>",
            [10] = "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Peserta) /V (Nomor 12) /Rect [50 650 300 670] /P 3 0 R >>",
            [11] = Stream($"/Type /XObject /Subtype /Image /Width {PhotoWidth} /Height {PhotoHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", System.Text.Encoding.Latin1.GetString(jpeg)),
            [12] = Stream($"/Type /ObjStm /N 2 /First {header.Length}", header + inStream[0] + "\n" + inStream[1]),
        };

        var output = new System.Text.StringBuilder("%PDF-1.5\n");
        var offsets = new Dictionary<int, int>();
        foreach (var (number, text) in objects)
        {
            offsets[number] = output.Length;
            output.Append($"{number} 0 obj\n{text}\nendobj\n");
        }

        // Entries of /W [1 4 2]: type, then offset or object stream number, then generation or index.
        offsets[13] = output.Length;
        var entries = new List<byte>();
        void Entry(int type, int field2, int field3) =>
            entries.AddRange([(byte)type, (byte)(field2 >> 24), (byte)(field2 >> 16), (byte)(field2 >> 8), (byte)field2, (byte)(field3 >> 8), (byte)field3]);
        for (var number = 0; number <= 13; number++)
        {
            if (number == 0)
            {
                Entry(0, 0, 65535);
            }
            else if (number is 7 or 8)
            {
                Entry(2, 12, number - 7);
            }
            else
            {
                Entry(1, offsets[number], 0);
            }
        }

        output.Append("13 0 obj\n" + Stream("/Type /XRef /Size 14 /W [1 4 2] /Root 1 0 R", System.Text.Encoding.Latin1.GetString(entries.ToArray())) + "\nendobj\n");
        output.Append($"startxref\n{offsets[13]}\n%%EOF\n");
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes(output.ToString()));
    }

    /// <summary>
    /// A one-page PDF: 1 is the catalog, 2 the page tree, 3 the page, 4 its <paramref name="content"/>, 5 Helvetica as /F1,
    /// and <paramref name="pictures"/> from 6 on, named on the page by <paramref name="xobjects"/>.
    /// </summary>
    private static void WriteRawPdf(string path, string content, string xobjects, IReadOnlyDictionary<int, string> pictures, string trailer = "")
    {
        var all = new SortedDictionary<int, string>
        {
            [1] = "<< /Type /Catalog /Pages 2 0 R >>",
            [2] = "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            [3] = $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> /XObject << {xobjects} >> >> >>",
            [4] = RawStream("", System.Text.Encoding.Latin1.GetBytes(content)),
            [5] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        foreach (var (number, text) in pictures)
        {
            all[number] = text;
        }

        var output = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new Dictionary<int, int>();
        foreach (var (number, text) in all)
        {
            offsets[number] = output.Length;
            output.Append($"{number} 0 obj\n{text}\nendobj\n");
        }

        var size = all.Keys.Max() + 1;
        var xref = output.Length;
        output.Append($"xref\n0 {size}\n0000000000 65535 f \n");
        for (var number = 1; number < size; number++)
        {
            output.Append(offsets.TryGetValue(number, out var offset) ? $"{offset:0000000000} 00000 n \n" : "0000000000 00000 f \n");
        }

        output.Append($"trailer\n<< /Size {size} /Root 1 0 R{trailer} >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes(output.ToString()));
    }

    private static string RawStream(string dictionary, byte[] data) =>
        $"<< {dictionary} /Length {data.Length} >>\nstream\n{System.Text.Encoding.Latin1.GetString(data)}\nendstream";

    private static byte[] Noise(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private ConversionPipeline CreatePipeline() => new(
        new ConverterRegistry([new ImageCompressor(), new PdfCompressor()], [new ImageOutputValidator(), new PdfOutputValidator()]),
        new TempFileJournal(_dir.File("journal")));

    private async Task<string> WriteScanAsync(string name, bool gray, float sourceQuality = 0.95f)
    {
        var path = _dir.File(name);
        var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(sourceQuality, global::Windows.Foundation.PropertyType.Single) };
        var jpeg = await EncodeAsync(BitmapEncoder.JpegEncoderId, Photo(withHole: false), BitmapAlphaMode.Ignore, options, gray);
        WritePdf(path, jpeg, isPng: false, pages: 2);
        return path;
    }

    private static void WritePdf(string path, byte[] picture, bool isPng, int pages = 1)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var place = new PdfRectangle(50, 300, 545, 671);
        PdfPageBuilder.AddedImage? added = null;
        for (var i = 0; i < pages; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            if (added is null)
            {
                added = isPng ? page.AddPng(picture, place) : page.AddJpeg(picture, place);
            }
            else
            {
                page.AddImage(added, place);
            }

            page.AddText("Kartu Peserta 2026", 14, new PdfPoint(50, 750), font);
        }

        File.WriteAllBytes(path, builder.Build());
    }

    /// <summary>Smooth gradients with some grain, so JPEG quality and size behave as they do on a photo.</summary>
    private static byte[] Photo(bool withHole)
    {
        var random = new Random(11);
        var pixels = new byte[PhotoWidth * PhotoHeight * 4];
        for (var y = 0; y < PhotoHeight; y++)
        {
            for (var x = 0; x < PhotoWidth; x++)
            {
                var i = ((y * PhotoWidth) + x) * 4;
                var grain = random.Next(-24, 25);
                pixels[i] = (byte)Math.Clamp((x * 255 / PhotoWidth) + grain, 0, 255);
                pixels[i + 1] = (byte)Math.Clamp((y * 255 / PhotoHeight) + grain, 0, 255);
                pixels[i + 2] = (byte)Math.Clamp((((x + y) % 97) * 2) + grain, 0, 255);
                pixels[i + 3] = withHole && x > PhotoWidth / 3 && x < PhotoWidth * 2 / 3 ? (byte)0 : (byte)255;
            }
        }

        return pixels;
    }

    private static async Task<byte[]> EncodeAsync(Guid encoderId, byte[] bgra, BitmapAlphaMode alpha, BitmapPropertySet? options = null, bool gray = false)
    {
        using var staging = new InMemoryRandomAccessStream();
        var encoder = options is null ? await BitmapEncoder.CreateAsync(encoderId, staging) : await BitmapEncoder.CreateAsync(encoderId, staging, options);
        if (gray)
        {
            var g = new byte[bgra.Length / 4];
            for (var i = 0; i < g.Length; i++)
            {
                g[i] = bgra[(i * 4) + 1];
            }

            encoder.SetPixelData(BitmapPixelFormat.Gray8, BitmapAlphaMode.Ignore, PhotoWidth, PhotoHeight, 96, 96, g);
        }
        else
        {
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, alpha, PhotoWidth, PhotoHeight, 96, 96, bgra);
        }

        await encoder.FlushAsync();
        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        using var memory = new MemoryStream();
        await encoded.CopyToAsync(memory, Ct);
        return memory.ToArray();
    }

    /// <summary>The number of components in a JPEG's frame header (SOF0, SOF1 or SOF2).</summary>
    private static int JpegChannels(byte[] jpeg)
    {
        for (var i = 2; i + 9 < jpeg.Length;)
        {
            if (jpeg[i] != 0xFF)
            {
                break;
            }

            var marker = jpeg[i + 1];
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                return jpeg[i + 9];
            }

            i += 2 + ((jpeg[i + 2] << 8) | jpeg[i + 3]);
        }

        return -1;
    }

    private static double MeanDifference(byte[] a, byte[] b)
    {
        long sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }

        return (double)sum / a.Length;
    }
}

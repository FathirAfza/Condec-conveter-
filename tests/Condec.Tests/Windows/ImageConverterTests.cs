// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Condec.Core.Pipeline;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Tests.Imaging;

/// <summary>Runs the real Windows Imaging Component through the full pipeline.</summary>
public sealed class ImageConverterTests : IDisposable
{
    private const uint Size = 16;
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, bool> Conversions => new()
    {
        { ".png", ".jpg", false },
        { ".png", ".bmp", false },
        { ".png", ".gif", false },
        { ".png", ".tif", true },
        { ".tif", ".png", true },
    };

    [Theory]
    [MemberData(nameof(Conversions))]
    public async Task Converts_AndKeepsOrFlattensTransparency(string source, string target, bool keepsTransparency)
    {
        var sourcePath = _dir.File("gambar" + source);
        await WriteTestImageAsync(sourcePath, EncoderFor(source));
        var destination = _dir.File("hasil" + target);

        var result = await CreatePipeline().RunAsync(new ConversionJob(sourcePath, target, destination), null, Ct);

        var image = await ReadImageAsync(destination);
        Assert.Equal(DecoderFor(target), image.Codec);
        Assert.Equal((Size, Size), (image.Width, image.Height));
        Assert.True(result.ChunkCount >= 1);

        // Right half: opaque blue survives every format (JPEG within its lossy tolerance).
        AssertColor(image.Pixel(12, 8), blue: 255, green: 0, red: 0, alpha: 255);

        // Left half: fully transparent. Formats without alpha show it on white, never black.
        if (keepsTransparency)
        {
            Assert.Equal(0, image.Pixel(3, 8)[3]);
        }
        else
        {
            AssertColor(image.Pixel(3, 8), blue: 255, green: 255, red: 255, alpha: 255);
        }
    }

    [Fact]
    public async Task CompleteSingleFrameImage_HasNoNotes()
    {
        var sourcePath = _dir.File("gambar.png");
        await WriteTestImageAsync(sourcePath, BitmapEncoder.PngEncoderId);

        var result = await CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".jpg", _dir.File("hasil.jpg")), null, Ct);

        Assert.Empty(result.Notes ?? []);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.5)]
    [InlineData(0.2)]
    public async Task CutOffJpeg_IsConvertedWithAWarning(double keep)
    {
        // Windows decodes a cut-off JPEG without an error and fills in the rest, so the note is the only sign.
        var sourcePath = _dir.File("terpotong.jpg");
        await WriteNoisyImageAsync(sourcePath, BitmapEncoder.JpegEncoderId);
        var whole = await File.ReadAllBytesAsync(sourcePath, Ct);
        await File.WriteAllBytesAsync(sourcePath, whole[..(int)(whole.Length * keep)], Ct);

        var result = await CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".png", _dir.File("hasil.png")), null, Ct);

        var note = Assert.Single(result.Notes!);
        Assert.Equal(NoteSeverity.Warning, note.Severity);
        Assert.Equal(Loc.Get("Note.SourceIncomplete"), note.Message);
        Assert.True(File.Exists(_dir.File("hasil.png")));
    }

    [Fact]
    public async Task WholeJpeg_HasNoWarning()
    {
        var sourcePath = _dir.File("utuh.jpg");
        await WriteNoisyImageAsync(sourcePath, BitmapEncoder.JpegEncoderId);

        var result = await CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".png", _dir.File("hasil.png")), null, Ct);

        Assert.Empty(result.Notes ?? []);
    }

    [Theory]
    [InlineData(".gif", 3)]
    [InlineData(".tif", 2)]
    public async Task SeveralFrames_ConvertTheFirstAndSayHowManyThereWere(string extension, int frames)
    {
        var sourcePath = _dir.File("banyak" + extension);
        await WriteFramesAsync(sourcePath, extension == ".gif" ? BitmapEncoder.GifEncoderId : BitmapEncoder.TiffEncoderId, frames);

        var result = await CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".png", _dir.File("hasil.png")), null, Ct);

        var note = Assert.Single(result.Notes!);
        Assert.Equal(NoteSeverity.Informational, note.Severity);
        Assert.Equal(Loc.Format("Note.FirstFrameOnly", frames), note.Message);

        // The first frame is the red one.
        var image = await ReadImageAsync(_dir.File("hasil.png"));
        Assert.Equal((Size, Size), (image.Width, image.Height));
        AssertColor(image.Pixel(1, 1), blue: 0, green: 0, red: 255, alpha: 255);
    }

    [Fact]
    public async Task PictureWithMorePixelsThanFitInMemory_IsRefusedBeforeDecoding()
    {
        // 30,000 x 30,000 is 900 MP: 3.6 GB as BGRA, more than one array can hold. The header says so; no pixels are read.
        var sourcePath = _dir.File("raksasa.png");
        await File.WriteAllBytesAsync(sourcePath, GrayscalePngHeaderOnly(30_000, 30_000), Ct);
        var destination = _dir.File("hasil.jpg");

        var error = await Assert.ThrowsAsync<ImageTooLargeException>(
            () => CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".jpg", destination), null, Ct));

        Assert.Equal(900, error.Megapixels);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.condec-tmp"));
    }

    [Theory]
    [InlineData(1_000_000, 1)]
    [InlineData(1_499_999, 1)]
    [InlineData(1_500_000, 2)]
    [InlineData(900_000_000, 900)]
    public void TooLargeError_RoundsToWholeMegapixels(long pixels, long megapixels)
    {
        Assert.Equal(megapixels, new ImageTooLargeException(pixels).Megapixels);
    }

    [Fact]
    public void Targets_ExcludeTheSourceFormat()
    {
        var converter = new ImageConverter();

        // HEIC is listed only where the HEVC codec lets Windows write it.
        string[] heic = ImageFormats.FindTarget(".heic") is null ? [] : [".heic"];
        Assert.Equal([".jpg", ".bmp", ".gif", ".tif", .. heic], converter.GetTargets(".png"));
        Assert.DoesNotContain(".jpg", converter.GetTargets(".JPEG"));
        Assert.DoesNotContain(".tif", converter.GetTargets(".tiff"));
        Assert.DoesNotContain(".heic", converter.GetTargets(".heif"));
        Assert.Empty(converter.GetTargets(".docx"));
    }

    [Fact]
    public async Task Png_ToHeic_WhenThisMachineCanWriteHeic()
    {
        Assert.SkipWhen(ImageFormats.FindTarget(".heic") is null, "HEIC can't be written on this machine.");
        var sourcePath = _dir.File("gambar.png");
        await WriteTestImageAsync(sourcePath, BitmapEncoder.PngEncoderId);

        await CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".heic", _dir.File("hasil.heic")), null, Ct);

        var image = await ReadImageAsync(_dir.File("hasil.heic"));
        Assert.Equal(BitmapDecoder.HeifDecoderId, image.Codec);
    }

    [Fact]
    public void OptionalSources_AreOfferedOnlyWhenTheirDecoderIsInstalled()
    {
        var installed = BitmapDecoder.GetDecoderInformationEnumerator().Select(codec => codec.CodecId).ToHashSet();
        var converter = new ImageConverter();

        Assert.Equal(installed.Contains(BitmapDecoder.WebpDecoderId), converter.GetTargets(".webp").Count > 0);
        Assert.Equal(installed.Contains(BitmapDecoder.HeifDecoderId), converter.GetTargets(".heic").Count > 0);
    }

    [Fact]
    public async Task Validator_RejectsAnImageOfAnotherFormat()
    {
        var png = _dir.File("sebenarnya-png.jpg");
        await WriteTestImageAsync(png, BitmapEncoder.PngEncoderId);

        await Assert.ThrowsAsync<InvalidDataException>(() => new ImageOutputValidator().ValidateAsync(png, ".jpg", Ct));
        await new ImageOutputValidator().ValidateAsync(png, ".png", Ct);
    }

    [Fact]
    public async Task Validator_RejectsBytesThatAreNoImage()
    {
        var path = _dir.File("rusak.png");
        await File.WriteAllBytesAsync(path, TestData.Bytes(500), Ct);

        await Assert.ThrowsAnyAsync<Exception>(() => new ImageOutputValidator().ValidateAsync(path, ".png", Ct));
    }

    [Fact]
    public void FlattenOntoWhite_BlendsBySourceAlpha()
    {
        byte[] pixels = [0, 0, 255, 128, 10, 20, 30, 255, 0, 0, 0, 0];

        ImageConverter.FlattenOntoWhite(pixels);

        Assert.Equal([127, 127, 255, 255, 10, 20, 30, 255, 255, 255, 255, 255], pixels);
    }

    private ConversionPipeline CreatePipeline() => new(
        new ConverterRegistry([new ImageConverter()], [new ImageOutputValidator()]),
        new TempFileJournal(_dir.File("journal")));

    private static Guid EncoderFor(string extension) => extension switch
    {
        ".png" => BitmapEncoder.PngEncoderId,
        ".tif" => BitmapEncoder.TiffEncoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(extension)),
    };

    private static Guid DecoderFor(string extension) => extension switch
    {
        ".jpg" => BitmapDecoder.JpegDecoderId,
        ".png" => BitmapDecoder.PngDecoderId,
        ".bmp" => BitmapDecoder.BmpDecoderId,
        ".gif" => BitmapDecoder.GifDecoderId,
        ".tif" => BitmapDecoder.TiffDecoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(extension)),
    };

    /// <summary>Left half fully transparent red, right half opaque blue.</summary>
    private static async Task WriteTestImageAsync(string path, Guid encoderId)
    {
        var pixels = new byte[Size * Size * 4];
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = (int)((y * Size) + x) * 4;
                if (x < Size / 2)
                {
                    pixels[i + 2] = 255;
                }
                else
                {
                    pixels[i] = 255;
                    pixels[i + 3] = 255;
                }
            }
        }

        using var staging = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, staging);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, Size, Size, 96, 96, pixels);
        await encoder.FlushAsync();

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        using var file = File.Create(path);
        await encoded.CopyToAsync(file, Ct);
    }

    /// <summary>Pseudo-random pixels, so that the compressed data is most of the file and a cut lands inside it.</summary>
    private static async Task WriteNoisyImageAsync(string path, Guid encoderId)
    {
        const uint Side = 128;
        var pixels = new byte[Side * Side * 4];
        new Random(7).NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }

        using var staging = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, staging);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, Side, Side, 96, 96, pixels);
        await encoder.FlushAsync();

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        using var file = File.Create(path);
        await encoded.CopyToAsync(file, Ct);
    }

    /// <summary>The first frame is red, the others are blue. Opaque throughout.</summary>
    private static async Task WriteFramesAsync(string path, Guid encoderId, int frames)
    {
        using var staging = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, staging);
        for (var frame = 0; frame < frames; frame++)
        {
            var pixels = new byte[Size * Size * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var red = frame == 0;
                pixels[i] = (byte)(red ? 0 : 255);
                pixels[i + 2] = (byte)(red ? 255 : 0);
                pixels[i + 3] = 255;
            }

            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, Size, Size, 96, 96, pixels);
            if (frame < frames - 1)
            {
                await encoder.GoToNextFrameAsync();
            }
        }

        await encoder.FlushAsync();
        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        using var file = File.Create(path);
        await encoded.CopyToAsync(file, Ct);
    }

    /// <summary>
    /// A PNG whose header (IHDR) declares an 8-bit grayscale picture of this size, and whose pixel data is a stub.
    /// Windows reads the size from the header alone, which is all the converter needs to refuse it.
    /// </summary>
    private static byte[] GrayscalePngHeaderOnly(uint width, uint height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        WriteBigEndian(header, 0, width);
        WriteBigEndian(header, 4, height);
        header[8] = 8; // bit depth
        header[9] = 0; // color type: grayscale
        WriteChunk(output, "IHDR", header);

        using var data = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(data, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.WriteByte(0);
        }

        WriteChunk(output, "IDAT", data.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var typeAndData = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);

        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32(typeAndData));
        output.Write(length);
        output.Write(typeAndData);
        output.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static async Task<DecodedImage> ReadImageAsync(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return new DecodedImage(decoder.DecoderInformation.CodecId, decoder.PixelWidth, decoder.PixelHeight, data.DetachPixelData());
    }

    private static void AssertColor(byte[] bgra, int blue, int green, int red, int alpha)
    {
        const int Tolerance = 24;
        Assert.InRange(bgra[0], blue - Tolerance, blue + Tolerance);
        Assert.InRange(bgra[1], green - Tolerance, green + Tolerance);
        Assert.InRange(bgra[2], red - Tolerance, red + Tolerance);
        Assert.Equal(alpha, bgra[3]);
    }

    private sealed record DecodedImage(Guid Codec, uint Width, uint Height, byte[] Pixels)
    {
        public byte[] Pixel(int x, int y) => Pixels.AsSpan((int)(((y * Width) + x) * 4), 4).ToArray();
    }
}

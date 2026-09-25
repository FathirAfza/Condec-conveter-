using Condec.Core.Conversion;
using Condec.Core.Imaging;
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

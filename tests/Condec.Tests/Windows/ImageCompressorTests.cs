// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Compression;
using Condec.Core.Conversion;
using Condec.Core.Imaging;
using Condec.Core.Pipeline;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Tests.Imaging;

/// <summary>Compress Image (DESIGN §6.5) on the real Windows Imaging Component.</summary>
public sealed class ImageCompressorTests : IDisposable
{
    private const int PhotoWidth = 400;
    private const int PhotoHeight = 300;
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Sources_AreWhatWindowsCanRead_AndTargetsAlwaysIncludeJpgAndPng()
    {
        Assert.Contains(".jpg", ImageCompressor.SourceExtensions);
        Assert.Contains(".png", ImageCompressor.SourceExtensions);
        Assert.Contains(".jpeg", ImageCompressor.SourceExtensions);
        Assert.DoesNotContain(".pdf", ImageCompressor.SourceExtensions);
        Assert.All(ImageCompressor.SourceExtensions, e => Assert.Equal(e.ToLowerInvariant(), e));

        // A JPG may become a JPG here, unlike in Convert File.
        var targets = new ImageCompressor().GetTargets(".jpg");
        Assert.Equal([".jpg", ".png"], targets.Take(2));
        Assert.Empty(new ImageCompressor().GetTargets(".pdf"));
    }

    [Fact]
    public void RawCameraFiles_AreSources_WhenTheirCodecIsInstalled()
    {
        var raw = BitmapDecoder.GetDecoderInformationEnumerator().FirstOrDefault(c => c.FileExtensions.Any(e => e.Equals(".cr2", StringComparison.OrdinalIgnoreCase)));
        Assert.SkipWhen(raw is null, "The Raw Image Extension is not installed on this PC.");
        Assert.Contains(".cr2", ImageCompressor.SourceExtensions);
        Assert.Contains(".nef", ImageCompressor.SourceExtensions);
        Assert.Contains(".dng", ImageCompressor.SourceExtensions);
    }

    [Fact]
    public async Task LowerQuality_MakesASmallerJpg()
    {
        var source = await LoadPhotoAsync();

        var high = await source.EncodeAsync(".jpg", 90, 100, Ct);
        var low = await source.EncodeAsync(".jpg", 40, 100, Ct);

        Assert.True(low.Data.Length < high.Data.Length * 0.7, $"{low.Data.Length} vs {high.Data.Length}");
        Assert.Equal((PhotoWidth, PhotoHeight), (low.Width, low.Height));
        Assert.Equal((PhotoWidth, PhotoHeight), await SizeOfAsync(low.Data));
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    public async Task ResolutionPercent_SetsTheResultsSize(string target)
    {
        var source = await LoadPhotoAsync();

        var half = await source.EncodeAsync(target, 80, 50, Ct);
        var full = await source.EncodeAsync(target, 80, 100, Ct);

        Assert.Equal((200, 150), (half.Width, half.Height));
        Assert.Equal((200, 150), await SizeOfAsync(half.Data));
        Assert.True(half.Data.Length < full.Data.Length);
    }

    [Fact]
    public async Task ShrinkingATransparentPng_KeepsItsEdgesClean()
    {
        // Transparent black up to x = 34, opaque white after. At a quarter of the size, result pixel 8 is half of each. A
        // scaler that mixed straight alpha as plain color would make it gray; the Windows one weighs it by alpha.
        var path = _dir.File("logo.png");
        var pixels = new byte[64 * 16 * 4];
        for (var y = 0; y < 16; y++)
        {
            for (var x = 34; x < 64; x++)
            {
                var i = ((y * 64) + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = 255;
            }
        }

        await WriteAsync(path, BitmapEncoder.PngEncoderId, pixels, 64, 16, BitmapAlphaMode.Straight);
        var source = await CompressSource.LoadAsync(path, Ct);
        Assert.True(source.HasTransparency);

        var result = await source.EncodeAsync(".png", 80, 25, Ct);
        var decoded = await PixelsOfAsync(result.Data);

        Assert.Equal((16, 4), (result.Width, result.Height));
        for (var x = 0; x < 16; x++)
        {
            var i = ((2 * 16) + x) * 4;
            if (decoded[i + 3] > 0)
            {
                // Every pixel that shows is white; only its alpha says how much of it shows.
                Assert.True(decoded[i] >= 250 && decoded[i + 1] >= 250 && decoded[i + 2] >= 250, $"x {x}: {decoded[i]},{decoded[i + 1]},{decoded[i + 2]},{decoded[i + 3]}");
            }
        }

        Assert.Equal(0, decoded[(2 * 16 * 4) + 3]);
        Assert.InRange(decoded[(((2 * 16) + 8) * 4) + 3], 100, 160);
        Assert.Equal(255, decoded[(((2 * 16) + 15) * 4) + 3]);
    }

    [Fact]
    public async Task TransparencyBecomesWhite_InAJpg()
    {
        var path = _dir.File("logo.png");
        await WriteAsync(path, BitmapEncoder.PngEncoderId, new byte[32 * 32 * 4], 32, 32, BitmapAlphaMode.Straight);
        var source = await CompressSource.LoadAsync(path, Ct);

        var decoded = await PixelsOfAsync((await source.EncodeAsync(".jpg", 80, 100, Ct)).Data);

        Assert.True(decoded[0] >= 250 && decoded[1] >= 250 && decoded[2] >= 250);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".png")]
    public async Task Fit_FindsTheBestResultUnderTheLimit(string target)
    {
        var source = await LoadPhotoAsync();
        var full = await source.EncodeAsync(target, CompressSearch.HighestQuality, 100, Ct);
        var limit = full.Data.Length / 3;

        var fitted = await source.FitAsync(target, limit, Ct);

        Assert.NotNull(fitted);
        Assert.True(fitted.Data.Length <= limit, $"{fitted.Data.Length} > {limit}");

        // The bytes are what that setting really makes, not a guess.
        var again = await source.EncodeAsync(target, fitted.Setting.Quality, fitted.Setting.ResolutionPercent, Ct);
        Assert.Equal(again.Data, fitted.Data);
    }

    [Fact]
    public async Task Fit_KeepsTheResolution_WhenQualityIsEnough()
    {
        var source = await LoadPhotoAsync();
        var floor = await source.EncodeAsync(".jpg", CompressSearch.LowestQuality, 100, Ct);
        var top = await source.EncodeAsync(".jpg", CompressSearch.HighestQuality, 100, Ct);
        var limit = (floor.Data.Length + top.Data.Length) / 2;

        var fitted = await source.FitAsync(".jpg", limit, Ct);

        Assert.NotNull(fitted);
        Assert.Equal(100, fitted.Setting.ResolutionPercent);
        Assert.InRange(fitted.Setting.Quality, CompressSearch.LowestQuality, CompressSearch.HighestQuality - 1);
    }

    [Fact]
    public async Task Fit_GivesNothing_WhenTheLimitIsTooSmall()
    {
        var source = await LoadPhotoAsync();

        Assert.Null(await source.FitAsync(".jpg", 10, Ct));
    }

    [Fact]
    public async Task Pipeline_SavesAVerifiedFileUnderTheLimit()
    {
        var sourcePath = await WritePhotoAsync("foto.png");
        var destination = _dir.File("foto (dikompres).jpg");
        const long Limit = 12_000;

        var result = await CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".jpg", destination, new CompressOptions(80, 100, Limit)), null, Ct);

        Assert.True(new FileInfo(destination).Length <= Limit);
        Assert.Equal(new FileInfo(destination).Length, result.Length);
        Assert.Empty(result.Notes ?? []);
    }

    [Fact]
    public async Task Pipeline_SameFormat_WithQualityAndResolution()
    {
        var sourcePath = _dir.File("foto.jpg");
        await WriteAsync(sourcePath, BitmapEncoder.JpegEncoderId, Photo(), PhotoWidth, PhotoHeight, BitmapAlphaMode.Ignore);
        var destination = _dir.File("foto (dikompres).jpg");

        await CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".jpg", destination, new CompressOptions(50, 40)), null, Ct);

        Assert.Equal((160, 120), await SizeOfAsync(await File.ReadAllBytesAsync(destination, Ct)));
        Assert.True(new FileInfo(destination).Length < new FileInfo(sourcePath).Length);
    }

    [Fact]
    public async Task Pipeline_SaysSo_WhenTheResultIsNotSmaller()
    {
        // A JPG saved at a very low quality grows when it is written again as a PNG.
        var sourcePath = _dir.File("kecil.jpg");
        var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(0.1f, Windows.Foundation.PropertyType.Single) };
        await WriteAsync(sourcePath, BitmapEncoder.JpegEncoderId, Photo(), PhotoWidth, PhotoHeight, BitmapAlphaMode.Ignore, options);

        var result = await CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".png", _dir.File("kecil.png"), new CompressOptions(100, 100)), null, Ct);

        var note = Assert.Single(result.Notes ?? []);
        Assert.Equal(NoteSeverity.Informational, note.Severity);
    }

    [Fact]
    public async Task Pipeline_RefusesALimitThatCantBeMet()
    {
        var sourcePath = await WritePhotoAsync("foto.png");
        var destination = _dir.File("foto.jpg");

        var error = await Assert.ThrowsAsync<CompressTargetTooSmallException>(() =>
            CreatePipeline().RunAsync(new ConversionJob(sourcePath, ".jpg", destination, new CompressOptions(80, 100, 10)), null, Ct));

        Assert.Equal(10, error.TargetBytes);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Heic_HasQualityAndResolution_WhenThisPcCanWriteIt()
    {
        Assert.SkipUnless(ImageCompressor.TargetExtensions.Contains(".heic"), "This PC can't write HEIC.");
        Assert.True(ImageCompressor.HasQuality(".heic"));
        var source = await LoadPhotoAsync();

        var full = await source.EncodeAsync(".heic", 80, 100, Ct);
        var low = await source.EncodeAsync(".heic", 30, 100, Ct);
        var half = await source.EncodeAsync(".heic", 80, 50, Ct);

        // The WIC HEIF encoder takes ImageQuality (Win32 docs, "Encoding overview"); 2026-10-04 on the test PC: 7, 41 and 95 KB at 20, 50 and 90%.
        Assert.True(low.Data.Length < full.Data.Length * 0.7, $"{low.Data.Length} vs {full.Data.Length}");
        Assert.True(half.Data.Length < full.Data.Length);
        Assert.Equal((200, 150), await SizeOfAsync(half.Data));
    }

    [Fact]
    public void Png_HasNoQuality()
    {
        Assert.False(ImageCompressor.HasQuality(".png"));
        Assert.True(ImageCompressor.HasQuality(".jpg"));
        Assert.True(ImageCompressor.HasQuality(".JPG"));
    }

    private ConversionPipeline CreatePipeline() => new(
        new ConverterRegistry([new ImageCompressor()], [new ImageOutputValidator()]),
        new TempFileJournal(_dir.File("journal")));

    private async Task<CompressSource> LoadPhotoAsync() => await CompressSource.LoadAsync(await WritePhotoAsync("foto.png"), Ct);

    private async Task<string> WritePhotoAsync(string name)
    {
        var path = _dir.File(name);
        await WriteAsync(path, BitmapEncoder.PngEncoderId, Photo(), PhotoWidth, PhotoHeight, BitmapAlphaMode.Ignore);
        return path;
    }

    /// <summary>Smooth gradients with some grain, so JPG quality and size behave as they do on a photo.</summary>
    private static byte[] Photo()
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
                pixels[i + 3] = 255;
            }
        }

        return pixels;
    }

    private static async Task WriteAsync(string path, Guid encoderId, byte[] pixels, int width, int height, BitmapAlphaMode alpha, BitmapPropertySet? options = null)
    {
        using var staging = new InMemoryRandomAccessStream();
        var encoder = options is null
            ? await BitmapEncoder.CreateAsync(encoderId, staging)
            : await BitmapEncoder.CreateAsync(encoderId, staging, options);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, alpha, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        using var file = File.Create(path);
        await encoded.CopyToAsync(file, Ct);
    }

    private static async Task<(int Width, int Height)> SizeOfAsync(byte[] data)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(data.AsBuffer());
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
    }

    private static async Task<byte[]> PixelsOfAsync(byte[] data)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(data.AsBuffer());
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return pixels.DetachPixelData();
    }
}

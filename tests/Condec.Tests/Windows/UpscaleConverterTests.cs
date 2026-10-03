// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Devices;
using Condec.Core.Imaging;
using Condec.Core.Pipeline;
using Condec.Core.Upscale;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Tests.Upscale;

/// <summary>
/// The upscale converter through the real pipeline and the real Windows imaging, with a stand-in network (nearest
/// neighbor) so the checks are about size, transparency and notes. The real network has its own tests below, which skip
/// when the model hasn't been fetched.
/// </summary>
public sealed class UpscaleConverterTests : IDisposable
{
    private const uint Size = 16;
    private const long Gib = 1024L * 1024 * 1024;
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class NearestNeighborModel : IUpscaleModel
    {
        public int Scale => 4;

        public HashSet<int> TileSizes { get; } = [];

        public float[] RunTile(float[] input, int size)
        {
            TileSizes.Add(size);
            var big = size * Scale;
            var result = new float[3 * big * big];
            for (var c = 0; c < 3; c++)
            {
                for (var y = 0; y < big; y++)
                {
                    for (var x = 0; x < big; x++)
                    {
                        result[(c * big * big) + (y * big) + x] = input[(c * size * size) + (y / Scale * size) + (x / Scale)];
                    }
                }
            }

            return result;
        }

        public void Dispose()
        {
        }
    }

    private static DeviceProfile Device(bool gpu = true, bool npu = false) =>
        new("cpu", 16 * Gib, gpu ? new GpuInfo("gpu", 8 * Gib, IsIntegrated: false) : null, npu ? "npu" : null);

    private ConversionPipeline CreatePipeline(DeviceProfile? device = null, Func<bool, IUpscaleModel>? factory = null) => new(
        new ConverterRegistry(
            [new UpscaleConverter(device ?? Device(), factory ?? (_ => new NearestNeighborModel()))],
            [new ImageOutputValidator()]),
        new TempFileJournal(_dir.File("journal")));

    [Fact]
    public void ItOffersPngAndJpg_ForEveryImageSource_IncludingTheSameFormat()
    {
        var converter = new UpscaleConverter(Device());
        var registry = new ConverterRegistry([converter], []);

        Assert.Equal([".png", ".jpg"], registry.GetTargetOptions(".png").Select(o => o.Extension));
        Assert.Equal([".png", ".jpg"], registry.GetTargetOptions(".jpg").Select(o => o.Extension));
        Assert.Empty(registry.GetTargetOptions(".mp3"));
    }

    [Theory]
    [InlineData(".png", 40, 30)]
    [InlineData(".png", 24, 24)]
    [InlineData(".jpg", 64, 64)]
    [InlineData(".jpg", 100, 20)]
    public async Task TheResult_HasExactlyTheAskedSize(string target, int width, int height)
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);

        var result = await CreatePipeline().RunAsync(
            new ConversionJob(source, target, _dir.File("hasil" + target), new UpscaleOptions(width, height, RenderEngine.Cpu)), null, Ct);

        var image = await ReadAsync(result.OutputPath);
        Assert.Equal(((uint)width, (uint)height), (image.Width, image.Height));
        Assert.Empty(result.Notes ?? []);
    }

    [Fact]
    public async Task Png_KeepsItsTransparency()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);

        var result = await CreatePipeline().RunAsync(
            new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(64, 64, RenderEngine.Cpu)), null, Ct);

        var image = await ReadAsync(result.OutputPath);
        Assert.Equal(0, image.Pixel(8, 32).A);
        var blue = image.Pixel(56, 32);
        Assert.Equal((255, 0, 0, 255), (blue.B, blue.G, blue.R, blue.A));
    }

    [Fact]
    public async Task Jpg_ShowsTransparencyOnWhite()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);

        var result = await CreatePipeline().RunAsync(
            new ConversionJob(source, ".jpg", _dir.File("hasil.jpg"), new UpscaleOptions(64, 64, RenderEngine.Cpu)), null, Ct);

        var white = (await ReadAsync(result.OutputPath)).Pixel(8, 32);
        Assert.All([white.B, white.G, white.R], value => Assert.InRange(value, 245, 255));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(96)]
    [InlineData(64)]
    [InlineData(48)]
    public async Task TheTileSizeInTheOptions_IsTheOneTheNetworkRuns(int tileSize)
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        var model = new NearestNeighborModel();

        var result = await CreatePipeline(factory: _ => model).RunAsync(
            new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(64, 64, RenderEngine.Cpu, tileSize)), null, Ct);

        Assert.Equal([tileSize], model.TileSizes);
        var image = await ReadAsync(result.OutputPath);
        Assert.Equal((64u, 64u), (image.Width, image.Height));
    }

    [Fact]
    public async Task ATileSizeTheUpscalerDoesNotUse_IsRefusedBeforeAnythingIsLoaded()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        var opened = false;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreatePipeline(factory: _ =>
        {
            opened = true;
            return new NearestNeighborModel();
        }).RunAsync(new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(64, 64, RenderEngine.Cpu, 100)), null, Ct));

        Assert.False(opened);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    public async Task AShareOfTimeOutsideZeroToOne_IsRefusedBeforeAnythingIsLoaded(double duty)
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        var opened = false;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => CreatePipeline(factory: _ =>
        {
            opened = true;
            return new NearestNeighborModel();
        }).RunAsync(new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(64, 64, RenderEngine.Cpu, Duty: duty)), null, Ct));

        Assert.False(opened);
    }

    [Fact]
    public async Task APacedRender_GivesTheSamePicture_AndPutsThePriorityBack()
    {
        // 100 × 100 at tile 48 is 4 × 4 tiles, so the paced render rests 15 times.
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source, 100);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var priority = process.PriorityClass;

        await CreatePipeline(factory: _ => new NearestNeighborModel()).RunAsync(
            new ConversionJob(source, ".png", _dir.File("tanpa-jeda.png"), new UpscaleOptions(200, 200, RenderEngine.Cpu, 48)), null, Ct);
        await CreatePipeline(factory: _ => new NearestNeighborModel()).RunAsync(
            new ConversionJob(source, ".png", _dir.File("dengan-jeda.png"), new UpscaleOptions(200, 200, RenderEngine.Cpu, 48, Duty: 0.5)), null, Ct);

        Assert.Equal(await File.ReadAllBytesAsync(_dir.File("tanpa-jeda.png"), Ct), await File.ReadAllBytesAsync(_dir.File("dengan-jeda.png"), Ct));
        process.Refresh();
        Assert.Equal(priority, process.PriorityClass);
    }

    [Fact]
    public async Task AnNpuChoice_RendersOnTheGpu_AndSaysSo()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        bool? gpuAsked = null;

        var result = await CreatePipeline(Device(gpu: true, npu: true), gpu =>
        {
            gpuAsked = gpu;
            return new NearestNeighborModel();
        }).RunAsync(new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(48, 48, RenderEngine.Npu)), null, Ct);

        Assert.True(gpuAsked);
        var note = Assert.Single(result.Notes!);
        Assert.Equal(NoteSeverity.Informational, note.Severity);
        Assert.NotEmpty(note.Message);
    }

    [Fact]
    public async Task AnNpuChoice_WithoutAGpu_RendersOnTheCpu()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        bool? gpuAsked = null;

        await CreatePipeline(Device(gpu: false, npu: true), gpu =>
        {
            gpuAsked = gpu;
            return new NearestNeighborModel();
        }).RunAsync(new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(48, 48, RenderEngine.Npu)), null, Ct);

        Assert.False(gpuAsked);
    }

    [Fact]
    public async Task ProgressNamesTheTiles()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        var details = new List<string>();
        var progress = new SynchronousProgress<PipelineProgress>(p =>
        {
            if (p.Detail is { } detail)
            {
                details.Add(detail);
            }
        });

        await CreatePipeline().RunAsync(
            new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(48, 48, RenderEngine.Cpu)), progress, Ct);

        Assert.Contains(UpscaleSupport.TileDetail(1, 1), details);
    }

    [Fact]
    public async Task APictureWhoseResultCannotBeHeld_IsRefusedBeforeAnyWork()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        var loaded = false;

        var pipeline = CreatePipeline(factory: _ =>
        {
            loaded = true;
            return new NearestNeighborModel();
        });
        await Assert.ThrowsAsync<ImageTooLargeException>(() => pipeline.RunAsync(
            new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(30_000, 30_000, RenderEngine.Cpu)), null, Ct));

        Assert.False(loaded);
        Assert.False(File.Exists(_dir.File("hasil.png")));
    }

    [Fact]
    public async Task WithoutUpscaleOptions_ItRefusesToGuessASize()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);

        await Assert.ThrowsAsync<ArgumentException>(() => CreatePipeline().RunAsync(
            new ConversionJob(source, ".png", _dir.File("hasil.png")), null, Ct));
    }

    [Fact]
    public async Task ACancelledRun_LeavesNoFile()
    {
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreatePipeline().RunAsync(
            new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(48, 48, RenderEngine.Cpu)), null, cts.Token));

        Assert.Empty(Directory.GetFiles(_dir.Path, "hasil*"));
    }

    // ---- The real network (skipped until tools\fetch-model.ps1 has put it next to the tests) ----

    private static void SkipWithout(UpscaleStyle style) =>
        Assert.SkipWhen(!File.Exists(UpscaleModelLocator.ModelPath(style)), $"The {style} upscale model has not been fetched (tools\\fetch-model.ps1).");

    [Theory]
    [InlineData(UpscaleStyle.Sharp)]
    [InlineData(UpscaleStyle.Faithful)]
    public void TheBundledModel_IsTheFileThatWasReleased(UpscaleStyle style)
    {
        SkipWithout(style);

        Assert.Equal(UpscaleModelStatus.Ready, UpscaleModelLocator.GetStatus(style));
    }

    [Fact]
    public void EachStyle_HasItsOwnFile_AndItsOwnHash()
    {
        Assert.NotEqual(UpscaleModelLocator.ModelPath(UpscaleStyle.Sharp), UpscaleModelLocator.ModelPath(UpscaleStyle.Faithful));
        Assert.NotEqual(UpscaleModelLocator.ExpectedSha256(UpscaleStyle.Sharp), UpscaleModelLocator.ExpectedSha256(UpscaleStyle.Faithful));
        Assert.EndsWith(Path.Combine("Models", "realesrnet-x4plus", "model.onnx"), UpscaleModelLocator.ModelPath(UpscaleStyle.Faithful));
    }

    [Fact]
    public void AModelThatIsNotTheReleasedFile_IsReportedDamaged()
    {
        var fake = _dir.File("model.onnx");
        File.WriteAllText(fake, "not a network");

        Assert.Equal(UpscaleModelStatus.Damaged, UpscaleModelLocator.GetStatus(fake, UpscaleModelLocator.SharpSha256));
        Assert.Equal(UpscaleModelStatus.Missing, UpscaleModelLocator.GetStatus(_dir.File("absent.onnx"), UpscaleModelLocator.SharpSha256));
    }

    [Fact]
    public void TheOtherStylesFile_IsNotAcceptedInItsPlace()
    {
        SkipWithout(UpscaleStyle.Sharp);

        Assert.Equal(UpscaleModelStatus.Damaged, UpscaleModelLocator.GetStatus(UpscaleModelLocator.ModelPath(UpscaleStyle.Sharp), UpscaleModelLocator.FaithfulSha256));
    }

    [Theory]
    [InlineData(UpscaleStyle.Sharp)]
    [InlineData(UpscaleStyle.Faithful)]
    public async Task TheRealNetwork_EnlargesAPictureOnTheCpu_AndKeepsItsShapes(UpscaleStyle style)
    {
        SkipWithout(style);
        var source = _dir.File("gambar.png");
        await WriteSourceAsync(source);
        var pipeline = new ConversionPipeline(
            new ConverterRegistry([new UpscaleConverter(Device(gpu: false))], [new ImageOutputValidator()]),
            new TempFileJournal(_dir.File("journal")));

        var result = await pipeline.RunAsync(
            new ConversionJob(source, ".png", _dir.File("hasil.png"), new UpscaleOptions(64, 64, RenderEngine.Cpu, Style: style)), null, Ct);

        var image = await ReadAsync(result.OutputPath);
        Assert.Equal((64u, 64u), (image.Width, image.Height));
        var blue = image.Pixel(56, 32);
        Assert.True(blue.B > 200 && blue.R < 60 && blue.A == 255, $"expected blue, got {blue}");
        Assert.True(image.Pixel(8, 32).A < 10);
    }

    [Fact]
    public void TheTwoStyles_AreDifferentNetworks()
    {
        SkipWithout(UpscaleStyle.Sharp);
        SkipWithout(UpscaleStyle.Faithful);
        using var sharp = OnnxUpscaleModel.Open(UpscaleModelLocator.ModelPath(UpscaleStyle.Sharp), gpu: false, tileSize: 48);
        using var faithful = OnnxUpscaleModel.Open(UpscaleModelLocator.ModelPath(UpscaleStyle.Faithful), gpu: false, tileSize: 48);

        var tile = new float[3 * 48 * 48];
        var random = new Random(7);
        for (var i = 0; i < tile.Length; i++)
        {
            tile[i] = (float)random.NextDouble();
        }

        // The result array is reused per model, so the first is copied before the second runs.
        var fromSharp = (float[])sharp.RunTile(tile, 48).Clone();
        var fromFaithful = faithful.RunTile(tile, 48);
        Assert.Equal(fromSharp.Length, fromFaithful.Length);
        Assert.True(fromSharp.Zip(fromFaithful).Average(p => Math.Abs(p.First - p.Second)) > 0.01, "The two styles gave the same result.");
    }

    [Fact]
    public async Task TheRealNetwork_OnTheGpu_AgreesWithTheCpu()
    {
        SkipWithout(UpscaleStyle.Sharp);
        using var cpu = OnnxUpscaleModel.Open(UpscaleModelLocator.ModelPath(UpscaleStyle.Sharp), gpu: false);
        using var gpu = OnnxUpscaleModel.Open(UpscaleModelLocator.ModelPath(UpscaleStyle.Sharp), gpu: true);
        Assert.SkipWhen(gpu.Engine != RenderEngine.Gpu, "No GPU can run DirectML here: " + gpu.GpuFailure);

        const int size = TiledUpscaler.DefaultTileSize;
        var tile = new float[3 * size * size];
        var random = new Random(3);
        for (var i = 0; i < tile.Length; i++)
        {
            tile[i] = (float)random.NextDouble();
        }

        var fromCpu = await Task.Run(() => cpu.RunTile(tile, size), Ct);
        var fromGpu = await Task.Run(() => gpu.RunTile(tile, size), Ct);

        double squares = 0;
        for (var i = 0; i < fromCpu.Length; i++)
        {
            var difference = Math.Clamp(fromCpu[i], 0, 1) - Math.Clamp(fromGpu[i], 0, 1);
            squares += difference * difference;
        }

        var psnr = 10 * Math.Log10(1 / (squares / fromCpu.Length));
        Assert.True(psnr > 40, $"The GPU differs from the CPU (PSNR {psnr:0.0} dB).");
    }

    // ---- Helpers ----

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private readonly record struct Image(uint Width, uint Height, byte[] Pixels)
    {
        public (byte B, byte G, byte R, byte A) Pixel(int x, int y)
        {
            var at = (int)((y * Width) + x) * 4;
            return (Pixels[at], Pixels[at + 1], Pixels[at + 2], Pixels[at + 3]);
        }
    }

    private static async Task<Image> ReadAsync(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        return new Image(decoder.PixelWidth, decoder.PixelHeight, data.DetachPixelData());
    }

    /// <summary>A square picture, 16 × 16 unless asked: transparent on the left half, opaque blue on the right.</summary>
    private async Task WriteSourceAsync(string path, uint size = Size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = (int)size / 2; x < size; x++)
            {
                var i = (int)((y * size) + x) * 4;
                pixels[i] = 255;
                pixels[i + 3] = 255;
            }
        }

        using var staging = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, staging);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, size, size, 96, 96, pixels);
        await encoder.FlushAsync();

        using var encoded = staging.GetInputStreamAt(0).AsStreamForRead();
        using var file = File.Create(path);
        await encoded.CopyToAsync(file, Ct);
    }
}

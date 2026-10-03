// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;
using Condec.Core.Upscale;

namespace Condec.Tests;

/// <summary>The tiler, the resampler and the engine rules, with a stand-in network so they run on any machine.</summary>
public class UpscaleEngineTests
{
    private const long Gib = 1024L * 1024 * 1024;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Repeats every pixel 4 times in both directions: the result of a tile is known exactly.</summary>
    private sealed class NearestNeighborModel : IUpscaleModel
    {
        public int Tiles { get; private set; }

        public int Scale => 4;

        public Func<float, float>? Distort { get; init; }

        public float[] RunTile(float[] input, int size)
        {
            Tiles++;
            var big = size * Scale;
            var result = new float[3 * big * big];
            for (var c = 0; c < 3; c++)
            {
                for (var y = 0; y < big; y++)
                {
                    for (var x = 0; x < big; x++)
                    {
                        var value = input[(c * size * size) + (y / Scale * size) + (x / Scale)];
                        result[(c * big * big) + (y * big) + x] = Distort?.Invoke(value) ?? value;
                    }
                }
            }

            return result;
        }

        public void Dispose()
        {
        }
    }

    private static byte[] Picture(int width, int height)
    {
        var random = new Random(5);
        var pixels = new byte[width * height * 4];
        random.NextBytes(pixels);
        return pixels;
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 7)]
    [InlineData(108, 108)]
    [InlineData(109, 30)]
    [InlineData(230, 150)]
    public void Tiles_MeetWithoutSeams_AndReproduceTheNetworksResultExactly(int width, int height) =>
        AssertSeamless(width, height, TiledUpscaler.DefaultTileSize);

    [Theory]
    [InlineData(96, 230, 150)]
    [InlineData(64, 230, 150)]
    [InlineData(48, 100, 61)]
    public void EverySizeOfTile_ReproducesTheNetworksResultExactly(int tileSize, int width, int height) =>
        AssertSeamless(width, height, tileSize);

    private static void AssertSeamless(int width, int height, int tileSize)
    {
        var source = Picture(width, height);
        var model = new NearestNeighborModel();

        var result = TiledUpscaler.Run(model, source, width, height, tileSize, null, Ct);

        Assert.Equal(width * 4 * height * 4 * 4, result.Length);
        for (var y = 0; y < height * 4; y++)
        {
            for (var x = 0; x < width * 4; x++)
            {
                var at = ((y * width * 4) + x) * 4;
                var from = (((y / 4) * width) + (x / 4)) * 4;
                Assert.Equal(source[from], result[at]);
                Assert.Equal(source[from + 1], result[at + 1]);
                Assert.Equal(source[from + 2], result[at + 2]);
                Assert.Equal(255, result[at + 3]);
            }
        }

        Assert.Equal(TiledUpscaler.TileCount(width, height, tileSize), model.Tiles);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(108, 108, 1)]
    [InlineData(109, 108, 2)]
    [InlineData(1280, 720, 12 * 7)]
    public void TileCount_CoversThePictureInBordersOf108(int width, int height, int expected) =>
        Assert.Equal(expected, TiledUpscaler.TileCount(width, height));

    [Theory]
    [InlineData(128, 108)]
    [InlineData(96, 76)]
    [InlineData(64, 44)]
    [InlineData(48, 28)]
    public void EachTileSize_ContributesItsSizeWithoutTheBorder(int tileSize, int inner) =>
        Assert.Equal(inner, TiledUpscaler.InnerSize(tileSize));

    [Fact]
    public void ATileSizeTheUpscalerDoesNotUse_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TiledUpscaler.Run(new NearestNeighborModel(), Picture(4, 4), 4, 4, 100, null, Ct));

    [Fact]
    public void Progress_IsReportedAfterEveryTile()
    {
        var reports = new List<(int Done, int Total)>();
        var progress = new SynchronousProgress<(int Done, int Total)>(reports.Add);

        TiledUpscaler.Run(new NearestNeighborModel(), Picture(230, 150), 230, 150, TiledUpscaler.DefaultTileSize, progress, Ct);

        Assert.Equal([(1, 6), (2, 6), (3, 6), (4, 6), (5, 6), (6, 6)], reports);
    }

    [Fact]
    public void ACancelledRun_StopsBeforeTheNextTile()
    {
        using var cts = new CancellationTokenSource();
        var model = new NearestNeighborModel();
        var progress = new SynchronousProgress<(int Done, int Total)>(_ => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() => TiledUpscaler.Run(model, Picture(230, 150), 230, 150, TiledUpscaler.DefaultTileSize, progress, cts.Token));
        Assert.Equal(1, model.Tiles);
    }

    [Fact]
    public void ValuesOutsideZeroToOne_AreClamped()
    {
        var model = new NearestNeighborModel { Distort = value => value < 0.5f ? -0.3f : 1.4f };
        var result = TiledUpscaler.Run(model, Picture(4, 4), 4, 4, TiledUpscaler.DefaultTileSize, null, Ct);

        Assert.All(result.Where((_, i) => i % 4 != 3), value => Assert.True(value is 0 or 255));
        Assert.Contains((byte)0, result);
        Assert.Contains((byte)255, result);
    }

    [Fact]
    public void PixelData_MustMatchTheSize() =>
        Assert.Throws<ArgumentException>(() => TiledUpscaler.Run(new NearestNeighborModel(), new byte[10], 4, 4, TiledUpscaler.DefaultTileSize, null, Ct));

    [Theory]
    [InlineData(-1, 5, 1)]
    [InlineData(-4, 5, 4)]
    [InlineData(-5, 5, 3)]
    [InlineData(0, 5, 0)]
    [InlineData(4, 5, 4)]
    [InlineData(5, 5, 3)]
    [InlineData(8, 5, 0)]
    [InlineData(-9, 5, 1)]
    [InlineData(7, 1, 0)]
    [InlineData(-3, 1, 0)]
    public void Reflect_MirrorsAtTheEdgesWithoutRepeatingTheEdgePixel(int index, int length, int expected) =>
        Assert.Equal(expected, TiledUpscaler.Reflect(index, length));

    // ---- Resampler ----

    [Theory]
    [InlineData(10, 10, 25, 17)]
    [InlineData(40, 30, 9, 7)]
    [InlineData(8, 8, 8, 20)]
    [InlineData(1, 1, 5, 5)]
    public void Resize_OfAFlatPicture_StaysFlat(int width, int height, int newWidth, int newHeight)
    {
        var source = new byte[width * height * 3];
        for (var i = 0; i < source.Length; i += 3)
        {
            (source[i], source[i + 1], source[i + 2]) = (10, 130, 250);
        }

        var result = Resampler.Resize(source, width, height, 3, newWidth, newHeight, Ct);

        Assert.Equal(newWidth * newHeight * 3, result.Length);
        for (var i = 0; i < result.Length; i += 3)
        {
            Assert.Equal((10, 130, 250), (result[i], result[i + 1], result[i + 2]));
        }
    }

    [Fact]
    public void Resize_ToTheSameSize_ReturnsACopy()
    {
        byte[] source = [1, 2, 3, 4];
        var result = Resampler.Resize(source, 2, 2, 1, 2, 2, Ct);

        Assert.Equal(source, result);
        Assert.NotSame(source, result);
    }

    [Fact]
    public void Shrinking_AveragesInsteadOfSkipping()
    {
        // A one-pixel checkerboard has no detail a half-size picture can keep, so it must come out mid-gray.
        var source = new byte[64 * 64];
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                source[(y * 64) + x] = (byte)(((x + y) % 2) * 255);
            }
        }

        var result = Resampler.Resize(source, 64, 64, 1, 32, 32, Ct);

        Assert.All(result.Skip(32 * 4).Take(32 * 24), value => Assert.InRange(value, 120, 135));
    }

    [Fact]
    public void Enlarging_ARamp_StaysMonotonicEnoughAndKeepsItsEnds()
    {
        var source = new byte[16];
        for (var x = 0; x < 16; x++)
        {
            source[x] = (byte)(x * 17);
        }

        var result = Resampler.Resize(source, 16, 1, 1, 64, 1, Ct);

        Assert.InRange(result[0], 0, 4);
        Assert.InRange(result[63], 251, 255);
        Assert.InRange(result[32], 110, 145);
    }

    [Fact]
    public void Channels_AreResizedIndependently()
    {
        // Two channels: the first is a horizontal ramp, the second constant.
        var source = new byte[8 * 2];
        for (var x = 0; x < 8; x++)
        {
            source[x * 2] = (byte)(x * 36);
            source[(x * 2) + 1] = 77;
        }

        var result = Resampler.Resize(source, 8, 1, 2, 16, 2, Ct);

        for (var i = 1; i < result.Length; i += 2)
        {
            Assert.Equal(77, result[i]);
        }
    }

    [Fact]
    public void Resize_RejectsPixelDataThatDoesNotMatch() =>
        Assert.Throws<ArgumentException>(() => Resampler.Resize(new byte[5], 2, 2, 1, 4, 4, Ct));

    [Fact]
    public void Resize_CanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Resampler.Resize(new byte[100 * 100], 100, 100, 1, 300, 300, cts.Token));
    }

    // ---- Engine rules ----

    private static DeviceProfile Device(bool gpu, bool npu) =>
        new("cpu", 16 * Gib, gpu ? new GpuInfo("gpu", 8 * Gib, IsIntegrated: false) : null, npu ? "npu" : null);

    [Theory]
    [InlineData(RenderEngine.Cpu, true, true, RenderEngine.Cpu)]
    [InlineData(RenderEngine.Gpu, true, false, RenderEngine.Gpu)]
    [InlineData(RenderEngine.Gpu, false, false, RenderEngine.Cpu)]
    [InlineData(RenderEngine.Npu, true, true, RenderEngine.Gpu)]
    [InlineData(RenderEngine.Npu, false, true, RenderEngine.Cpu)]
    public void TheEngineThatRenders_FollowsWhatTheDeviceHas(RenderEngine chosen, bool gpu, bool npu, RenderEngine expected) =>
        Assert.Equal(expected, UpscaleSupport.EffectiveEngine(chosen, Device(gpu, npu)));

    [Fact]
    public void NetworkWork_IsSixteenTimesThePictureWhateverTheScale() =>
        Assert.Equal(16L * 1280 * 720, UpscaleSupport.NetworkPixels(1280, 720));

    [Fact]
    public void RenderedWork_CountsWholeTilesAlsoAtTheEdges()
    {
        // 1280 x 720 is 12 x 7 tiles of 108 pixels; the last column and row are only partly filled but run whole.
        Assert.Equal(12L * 7 * 432 * 432, UpscaleSupport.RenderedPixels(1280, 720));
        // A smaller tile repeats more border: 76 pixels of picture per tile of 96.
        Assert.Equal(17L * 10 * 304 * 304, UpscaleSupport.RenderedPixels(1280, 720, 96));
        Assert.Equal(432L * 432, UpscaleSupport.RenderedPixels(1, 1));
        Assert.Equal(UpscaleSupport.TileMegapixels(4) * 4 * 1_000_000, UpscaleSupport.RenderedPixels(216, 216), 3);
    }

    [Fact]
    public void ATile_AddsItsInnerPartAtFourTimes() =>
        Assert.Equal(0.186624, UpscaleSupport.TileMegapixels(4), 6);

    [Theory]
    [InlineData(3, 12)]
    [InlineData(12, 12)]
    public void TileProgress_RoundTripsThroughItsText(int done, int total)
    {
        Assert.True(UpscaleSupport.TryParseTiles(UpscaleSupport.TileDetail(done, total), out var parsedDone, out var parsedTotal));
        Assert.Equal((done, total), (parsedDone, parsedTotal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Writing .png")]
    [InlineData("1/2/3")]
    public void TileProgress_IgnoresOtherDetails(string? detail) =>
        Assert.False(UpscaleSupport.TryParseTiles(detail, out _, out _));

    /// <summary>Reports on the calling thread, unlike <see cref="Progress{T}"/>, so a test can count reports without waiting.</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Compression;

namespace Condec.Tests;

/// <summary>The size-limit search of Compress (DESIGN §6.5), on a size model instead of a real encoder.</summary>
public sealed class CompressSearchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A photo-like model: the size grows with the quality and with the pixel count.</summary>
    private static long Model(CompressSetting s) =>
        (long)(1_000_000 * Math.Pow(s.Quality / 100.0, 3) * Math.Pow(s.ResolutionPercent / 100.0, 2));

    private static async Task<(CompressMeasure? Found, List<CompressSetting> Tried)> FitAsync(bool lossy, long target, Func<CompressSetting, long>? size = null)
    {
        var tried = new List<CompressSetting>();
        var found = await CompressSearch.FitAsync(
            lossy,
            target,
            (s, _) =>
            {
                tried.Add(s);
                return Task.FromResult((size ?? Model)(s));
            },
            Ct);
        return (found, tried);
    }

    [Fact]
    public async Task RoomyLimit_TakesTheHighestQuality_AtFullResolution()
    {
        var (found, tried) = await FitAsync(lossy: true, 900_000);

        Assert.Equal(new CompressSetting(CompressSearch.HighestQuality, 100), found!.Setting);
        Assert.Single(tried);
    }

    [Fact]
    public async Task LowersTheQualityFirst_AndFindsTheHighestThatFits()
    {
        // 0.7^3 = 343,000 fits 350,000; 0.71^3 = 357,911 doesn't.
        var (found, tried) = await FitAsync(lossy: true, 350_000);

        Assert.Equal(new CompressSetting(70, 100), found!.Setting);
        Assert.Equal(Model(found.Setting), found.Bytes);
        Assert.True(tried.Count <= 8, $"{tried.Count} encodes");
        Assert.All(tried, s => Assert.Equal(100, s.ResolutionPercent));
    }

    [Fact]
    public async Task ShrinksTheResolution_AtTheLowestQuality_WhenQualityIsNotEnough()
    {
        // At 60%: 216,000 × p². 100,000 fits up to p = 68 (99,878); 69 is 102,838.
        var (found, tried) = await FitAsync(lossy: true, 100_000);

        Assert.Equal(new CompressSetting(CompressSearch.LowestQuality, 68), found!.Setting);
        Assert.True(found.Bytes <= 100_000);
        Assert.True(tried.Count <= 10, $"{tried.Count} encodes");
    }

    [Fact]
    public async Task Png_OnlyShrinksTheResolution()
    {
        // At 100: 1,000,000 × p². 250,000 fits at exactly 50.
        var (found, tried) = await FitAsync(lossy: false, 250_000);

        Assert.Equal(50, found!.Setting.ResolutionPercent);
        Assert.All(tried, s => Assert.Equal(CompressOptions.MaximumQuality, s.Quality));
    }

    [Fact]
    public async Task ImpossibleLimit_GivesNothing()
    {
        var (found, _) = await FitAsync(lossy: true, 10);

        Assert.Null(found);
    }

    [Fact]
    public async Task UnevenSizes_StillGiveOnlyAMeasuredFit()
    {
        // Real encoders are not always smaller at a smaller size. Whatever the search lands on, it was measured to fit.
        static long Bumpy(CompressSetting s) => Model(s) + (s.ResolutionPercent % 7 == 0 ? 200_000 : 0);
        var (found, _) = await FitAsync(lossy: true, 120_000, Bumpy);

        Assert.NotNull(found);
        Assert.Equal(Bumpy(found.Setting), found.Bytes);
        Assert.True(found.Bytes <= 120_000);
    }

    [Fact]
    public async Task Cancel_Stops()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CompressSearch.FitAsync(true, 1000, (s, _) => Task.FromResult(Model(s)), cancelled.Token));
    }

    [Theory]
    [InlineData(4032, 3024, 50, 2016, 1512)]
    [InlineData(4032, 3024, 33, 1331, 998)]
    [InlineData(3, 1, 10, 1, 1)]
    [InlineData(1920, 1080, 100, 1920, 1080)]
    public void ScaledSize_RoundsAndNeverDropsToZero(int width, int height, int percent, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), CompressOptions.ScaledSize(width, height, percent));
    }

    [Theory]
    [InlineData(1_000_000, "1 MB", "1 MB")]
    [InlineData(2_500_000, "2.5 MB", "2,5 MB")]
    [InlineData(500_000, "500 KB", "500 KB")]
    [InlineData(1_000, "1 KB", "1 KB")]
    [InlineData(1_234, "1.23 KB", "1,23 KB")]
    public void Limit_IsWrittenInTheDecimalUnitsItIsCountedIn(long bytes, string english, string indonesian)
    {
        Assert.Equal(english, CompressText.Limit(bytes, CultureInfo.GetCultureInfo("en")));
        Assert.Equal(indonesian, CompressText.Limit(bytes, CultureInfo.GetCultureInfo("id")));
    }
}

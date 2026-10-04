// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp.Types.Units;
using Condec.Core.Architecture;
using Condec.Core.Devices;
using Condec.Core.Localization;

namespace Condec.Tests.Architecture;

/// <summary>The rules of DESIGN §6.3 that decide what the Architecture page offers, before any picture is traced.</summary>
public class ArchitecturePolicyTests
{
    private const long Gib = 1024L * 1024 * 1024;

    private static DeviceProfile Device(int ramGb, int vramGb = 0) =>
        new("Test CPU", ramGb * Gib, vramGb == 0 ? null : new GpuInfo("Test GPU", vramGb * Gib, false), null);

    private static UpscaleAdvice Advise(int unclear, DeviceProfile device, int width = 1600, int height = 1200, double settingsLimit = 4, int memoryGb = 8) =>
        ArchitectureUpscalePolicy.Advise(unclear, device, width, height, settingsLimit, memoryGb * Gib, device.DefaultEngine);

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void FewUnclearAreas_NeedNoUpscale(int unclear)
    {
        var advice = Advise(unclear, Device(32, 8));

        Assert.Equal(UpscaleAdviceKind.NotNeeded, advice.Kind);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(40)]
    public void FiveOrMoreUnclearAreas_OfferTheUpscaleWhenTheDeviceCanDoIt(int unclear)
    {
        var advice = Advise(unclear, Device(32, 8));

        Assert.Equal(UpscaleAdviceKind.Suggest, advice.Kind);
        Assert.True(advice.CanUpscale);
        Assert.Equal(2, advice.DefaultScale);
    }

    [Fact]
    public void TheMenuHasTwoFourAndEightTimes()
    {
        var advice = Advise(9, Device(32, 8));

        Assert.Equal([2, 4, 8], advice.Choices.Select(c => c.Scale));
    }

    [Fact]
    public void ScalesBeyondTheLimitAreLockedButStayInTheMenu()
    {
        // 32 GB RAM and an 8 GB card: 5× by the card; Settings caps at 4.
        var advice = Advise(9, Device(32, 8), settingsLimit: 4);

        Assert.Equal(4, advice.Limit.Effective);
        Assert.Equal([true, true, false], advice.Choices.Select(c => c.IsAllowed));
    }

    [Fact]
    public void ALowerSettingsLimitLocksTheScalesAboveIt()
    {
        var advice = Advise(9, Device(32, 8), settingsLimit: 2);

        Assert.Equal([true, false, false], advice.Choices.Select(c => c.IsAllowed));
        Assert.Equal(2, advice.DefaultScale);
    }

    [Fact]
    public void ADeviceWithTooLittleRam_IsNotEligible_AndTheRamIsTheReason()
    {
        // 4000 × 3000 at 2× is 48 MP, which wants 64 GB; this device has 8.
        var advice = Advise(12, Device(8), 4000, 3000, settingsLimit: 2);

        Assert.Equal(UpscaleAdviceKind.NotEligible, advice.Kind);
        Assert.False(advice.CanUpscale);
        Assert.Null(advice.DefaultScale);
        Assert.True(advice.IsRamShort);
        Assert.Equal(64, advice.RequiredRamGb);
        Assert.Equal(8, advice.InstalledRamGb);
    }

    [Fact]
    public void ARoomyDeviceWithATightMemoryLimit_IsNotEligible_ButTheRamIsNotShort()
    {
        // The device could do it, but Settings keeps the memory so low that even the smallest tiles don't fit.
        var advice = ArchitectureUpscalePolicy.Advise(12, Device(64, 16), 6000, 4000, 4, 256L * 1024 * 1024, RenderEngine.Gpu);

        Assert.Equal(UpscaleAdviceKind.NotEligible, advice.Kind);
        Assert.False(advice.IsRamShort);
    }

    [Fact]
    public void NotEligibleOnlyMattersWhenTheAreasAreMany()
    {
        var advice = Advise(2, Device(8), settingsLimit: 2);

        Assert.Equal(UpscaleAdviceKind.NotNeeded, advice.Kind);
    }

    [Fact]
    public void AHugePictureHasNoScaleAtAll()
    {
        var advice = Advise(20, Device(64, 16), 12000, 9000);

        Assert.Equal(UpscaleAdviceKind.NotEligible, advice.Kind);
    }

    [Fact]
    public void TheRequiredRamIsThatOfTheTwoTimesResult()
    {
        // 1600 × 1200 → 3200 × 2400 = 7.68 MP, in the HD–8K row (8 GB).
        Assert.Equal(8, Advise(5, Device(32, 8)).RequiredRamGb);
        // 3000 × 2000 → 6000 × 4000 = 24 MP, still in the HD–8K row (8 GB).
        Assert.Equal(8, Advise(5, Device(32, 8), 3000, 2000).RequiredRamGb);
        // 4000 × 3000 → 8000 × 6000 = 48 MP, above 8K (64 GB).
        Assert.Equal(64, Advise(5, Device(32, 8), 4000, 3000).RequiredRamGb);
    }

    [Fact]
    public void TheThresholdIsFive() => Assert.Equal(5, ArchitectureUpscalePolicy.UnclearAreaThreshold);

    [Fact]
    public void Calibration_GivesMillimetersPerPixel()
    {
        // 3-4-5 triangle: the points are 500 px apart, and that is 2000 mm.
        var scale = ScaleCalibration.MillimetersPerPixel(100, 100, 400, 500, 2000);

        Assert.Equal(4.0, scale!.Value, 9);
    }

    [Theory]
    [InlineData(10, 10, 10, 10, 1000)] // the same point
    [InlineData(10, 10, 10.4, 10, 1000)] // under a pixel apart
    [InlineData(0, 0, 100, 0, 0)]
    [InlineData(0, 0, 100, 0, -5)]
    [InlineData(0, 0, 100, 0, double.NaN)]
    [InlineData(0, 0, double.PositiveInfinity, 0, 100)]
    public void Calibration_RefusesPointsOrLengthsThatMeanNothing(double x1, double y1, double x2, double y2, double length) =>
        Assert.Null(ScaleCalibration.MillimetersPerPixel(x1, y1, x2, y2, length));

    [Theory]
    [InlineData(CalibrationUnit.Millimeters, 25, 25)]
    [InlineData(CalibrationUnit.Centimeters, 25, 250)]
    [InlineData(CalibrationUnit.Meters, 2.5, 2500)]
    public void Calibration_ConvertsUnitsToMillimeters(CalibrationUnit unit, double value, double expected) =>
        Assert.Equal(expected, ScaleCalibration.ToMillimeters(value, unit), 9);

    [Fact]
    public void AnA3SheetAt300Dpi_IsAbout4961By3508()
    {
        var (width, height) = CadResultEstimate.Pixels(PaperSize.A3, 300, landscape: true);

        Assert.Equal(4961, width);
        Assert.Equal(3508, height);
    }

    [Fact]
    public void APortraitSheetSwapsTheSides()
    {
        var (width, height) = CadResultEstimate.Pixels(PaperSize.A4, 150, landscape: false);

        Assert.Equal(1240, width);
        Assert.Equal(1754, height);
    }

    [Fact]
    public void TheEstimate_OfA3At300Dpi_IsAbout1Point7MbAsAPng()
    {
        var (width, height) = CadResultEstimate.Pixels(PaperSize.A3, 300, landscape: true);

        var bytes = CadResultEstimate.Bytes(CadResultFormat.Png, width, height);

        Assert.InRange(bytes / 1_000_000.0, 1.6, 1.9);
    }

    [Fact]
    public void APdfEstimateDoesNotDependOnThePixels()
    {
        Assert.Equal(CadResultEstimate.PdfBytes, CadResultEstimate.Bytes(CadResultFormat.Pdf, 100, 100));
        Assert.Equal(CadResultEstimate.PdfBytes, CadResultEstimate.Bytes(CadResultFormat.Pdf, 9000, 9000));
    }

    [Fact]
    public void AJpgEstimate_IsLargerThanAPngOfTheSamePixelsInThisModel()
    {
        Assert.True(CadResultEstimate.Bytes(CadResultFormat.Jpg, 1000, 1000) > CadResultEstimate.Bytes(CadResultFormat.Png, 1000, 1000));
    }

    [Theory]
    [InlineData("WALLS", "Architecture.Kind.Walls")]
    [InlineData("OPENINGS", "Architecture.Kind.Openings")]
    [InlineData("TEXT", "Architecture.Kind.Text")]
    public void TheLayersCondecWrites_HaveNamesInTheAppsLanguage(string layer, string key) =>
        Assert.Equal(Loc.Get(key), CadNames.Layer(layer));

    [Fact]
    public void AnyOtherLayer_KeepsTheNameTheFileGives() =>
        Assert.Equal("A-WALL-FULL", CadNames.Layer("A-WALL-FULL"));

    [Theory]
    [InlineData(UnitsType.Millimeters, "Architecture.Unit.Millimeters")]
    [InlineData(UnitsType.Centimeters, "Architecture.Unit.Centimeters")]
    [InlineData(UnitsType.Meters, "Architecture.Unit.Meters")]
    [InlineData(UnitsType.Inches, "Architecture.Unit.Inches")]
    [InlineData(UnitsType.Feet, "Architecture.Unit.Feet")]
    [InlineData(UnitsType.Unitless, "Architecture.Unit.Unitless")]
    public void TheCommonUnits_AreWords(UnitsType units, string key) =>
        Assert.Equal(Loc.Get(key), CadNames.Unit(units));

    [Fact]
    public void ARareUnit_IsStillNamed() =>
        Assert.False(string.IsNullOrWhiteSpace(CadNames.Unit(UnitsType.Parsecs)));
}

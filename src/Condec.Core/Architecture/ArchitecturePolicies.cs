// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp.Types.Units;
using Condec.Core.Devices;
using Condec.Core.Localization;
using Condec.Core.Upscale;

namespace Condec.Core.Architecture;

public enum UpscaleAdviceKind
{
    /// <summary>Few enough unclear areas: the picture is converted as it is.</summary>
    NotNeeded,

    /// <summary>Many unclear areas and the device can upscale: offer it.</summary>
    Suggest,

    /// <summary>Many unclear areas but this device can't upscale this picture.</summary>
    NotEligible,
}

/// <summary>One scale of the "Upscale n× first" menu.</summary>
public sealed record ArchitectureScaleChoice(int Scale, bool IsAllowed);

/// <summary>What Architecture says about upscaling a picture before it is traced (DESIGN §6.3.1).</summary>
/// <param name="Limit">The largest scale this device allows for the picture (DESIGN §7.3).</param>
/// <param name="RequiredRamGb">The installed RAM a 2× result needs (§7.2).</param>
public sealed record UpscaleAdvice(UpscaleAdviceKind Kind, int UnclearAreas, ScaleLimit Limit, IReadOnlyList<ArchitectureScaleChoice> Choices, int RequiredRamGb, int InstalledRamGb)
{
    public bool CanUpscale => Choices.Any(c => c.IsAllowed);

    /// <summary>2× when the device can do it, otherwise the smallest scale it can; null when none.</summary>
    public int? DefaultScale => Choices.FirstOrDefault(c => c.IsAllowed)?.Scale;

    /// <summary>The installed RAM is below what a 2× result needs, which is what the Error text says; otherwise another limit rules it out.</summary>
    public bool IsRamShort => InstalledRamGb < RequiredRamGb;
}

public static class ArchitectureUpscalePolicy
{
    /// <summary>Five or more unclear areas are "many" `[ASUMSI]` (DESIGN §6.3.1, §13).</summary>
    public const int UnclearAreaThreshold = 5;

    /// <summary>The scales of the menu (§6.3.1); the default is 2×.</summary>
    public static IReadOnlyList<int> Scales { get; } = [2, 4, 8];

    /// <param name="width">The picture's size as it is, before any upscale.</param>
    /// <param name="settingsLimit">The scale limit chosen in Settings.</param>
    /// <param name="memoryLimitBytes">The memory limit chosen in Settings (§7.5).</param>
    public static UpscaleAdvice Advise(int unclearAreas, DeviceProfile device, int width, int height, double settingsLimit, long memoryLimitBytes, RenderEngine engine)
    {
        var limit = UpscalePlan.EffectiveLimit(device, width, height, settingsLimit);
        var choices = Scales
            .Select(scale => new ArchitectureScaleChoice(scale, IsAllowed(scale, limit, width, height, engine, memoryLimitBytes)))
            .ToList();
        var required = CapabilityPolicy.RequiredRam(4L * width * height).Gb;

        var kind = unclearAreas < UnclearAreaThreshold
            ? UpscaleAdviceKind.NotNeeded
            : choices.Any(c => c.IsAllowed) ? UpscaleAdviceKind.Suggest : UpscaleAdviceKind.NotEligible;
        return new UpscaleAdvice(kind, unclearAreas, limit, choices, required, device.InstalledRamGb);
    }

    private static bool IsAllowed(int scale, ScaleLimit limit, int width, int height, RenderEngine engine, long memoryLimitBytes)
    {
        if (!CapabilityPolicy.IsScaleAllowed(scale, limit))
        {
            return false;
        }

        var (outputWidth, outputHeight) = UpscaleEstimator.OutputSize(width, height, scale);
        return UpscaleMemory.Plan(width, height, outputWidth, outputHeight, engine, keepsAlpha: true, memoryLimitBytes).Fits;
    }
}

/// <summary>Turns two marked points and the distance between them into the size of one pixel (DESIGN §6.3.1 "Kalibrasi").</summary>
public static class ScaleCalibration
{
    /// <summary>Millimeters per pixel, or null when the points are the same or the distance isn't a positive number.</summary>
    public static double? MillimetersPerPixel(double x1, double y1, double x2, double y2, double lengthMillimeters)
    {
        var pixels = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        if (!double.IsFinite(pixels) || pixels < 1 || !double.IsFinite(lengthMillimeters) || lengthMillimeters <= 0)
        {
            return null;
        }

        return lengthMillimeters / pixels;
    }

    public static double ToMillimeters(double value, CalibrationUnit unit) => unit switch
    {
        CalibrationUnit.Millimeters => value,
        CalibrationUnit.Centimeters => value * 10,
        _ => value * 1000,
    };
}

public enum CalibrationUnit
{
    Millimeters,
    Centimeters,
    Meters,
}

public enum CadResultFormat
{
    Pdf,
    Png,
    Jpg,
}

/// <summary>The size of a drawing's result, before it is made (DESIGN §6.3.2). `[ASUMSI]`: a rough figure, shown with "±".</summary>
public static class CadResultEstimate
{
    public const double PngBytesPerPixel = 0.1;

    public const double JpgBytesPerPixel = 0.14;

    public const long PdfBytes = 300_000;

    /// <summary>The pixels of a sheet at a resolution: millimeters ÷ 25.4 × dpi on each side. The drawing fills the sheet landscape or portrait, whichever fits it better.</summary>
    public static (long Width, long Height) Pixels(PaperSize paper, int dpi, bool landscape)
    {
        var (w, h) = CadPdfWriter.SheetMillimeters(paper);
        var width = (long)Math.Round(w / 25.4 * dpi, MidpointRounding.AwayFromZero);
        var height = (long)Math.Round(h / 25.4 * dpi, MidpointRounding.AwayFromZero);
        return landscape ? (width, height) : (height, width);
    }

    public static long Bytes(CadResultFormat format, long width, long height) => format switch
    {
        CadResultFormat.Png => (long)(width * height * PngBytesPerPixel),
        CadResultFormat.Jpg => (long)(width * height * JpgBytesPerPixel),
        _ => PdfBytes,
    };
}

/// <summary>What a drawing's layers and units are called on screen.</summary>
public static class CadNames
{
    /// <summary>The layers Condec writes have names in the app's language; any other layer keeps the name the file gives it.</summary>
    public static string Layer(string name) => name switch
    {
        ArchitectureCadBuilder.WallsLayer => Loc.Get("Architecture.Kind.Walls"),
        ArchitectureCadBuilder.OpeningsLayer => Loc.Get("Architecture.Kind.Openings"),
        ArchitectureCadBuilder.TextLayer => Loc.Get("Architecture.Kind.Text"),
        ArchitectureCadBuilder.LogoLayer => Loc.Get("Architecture.Kind.Logo"),
        ArchitectureCadBuilder.TableLayer => Loc.Get("Architecture.Kind.Table"),
        _ => name,
    };

    /// <summary>The unit as a word: "milimeter". Units a drawing rarely uses are named in English by the enum, which is better than nothing.</summary>
    public static string Unit(UnitsType units) => units switch
    {
        UnitsType.Unitless => Loc.Get("Architecture.Unit.Unitless"),
        UnitsType.Millimeters => Loc.Get("Architecture.Unit.Millimeters"),
        UnitsType.Centimeters => Loc.Get("Architecture.Unit.Centimeters"),
        UnitsType.Meters => Loc.Get("Architecture.Unit.Meters"),
        UnitsType.Inches => Loc.Get("Architecture.Unit.Inches"),
        UnitsType.Feet => Loc.Get("Architecture.Unit.Feet"),
        _ => units.ToString().ToLowerInvariant(),
    };
}

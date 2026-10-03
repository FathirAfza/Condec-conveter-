// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;

namespace Condec.Core.Pdf;

/// <summary>Which page of a PDF source to convert, for targets that hold one page (images, CAD).</summary>
/// <param name="PageNumber">1-based.</param>
public record PdfPageOptions(int PageNumber) : PageOptions(PageNumber);

public enum CadUnit
{
    Millimeters,
    Centimeters,
    Meters,
    Inches,
}

/// <param name="ScaleDenominator">The drawing scale 1 : n. At 1 : 100, 1 mm on the page becomes 100 mm in the drawing.</param>
/// <param name="KeepText">Write PDF text as TEXT entities. Ignored for scanned pages, which have no text.</param>
/// <param name="JoinLines">Write connected straight segments as one LWPOLYLINE instead of separate LINEs.</param>
public sealed record CadOptions(
    int PageNumber,
    CadUnit Unit = CadUnit.Millimeters,
    double ScaleDenominator = 1,
    bool KeepText = true,
    bool JoinLines = true) : PdfPageOptions(PageNumber)
{
    /// <summary>Drawing units per PDF point (1/72 inch), including the scale.</summary>
    public double UnitsPerPoint => ScaleDenominator / 72.0 * UnitsPerInch;

    private double UnitsPerInch => Unit switch
    {
        CadUnit.Millimeters => 25.4,
        CadUnit.Centimeters => 2.54,
        CadUnit.Meters => 0.0254,
        CadUnit.Inches => 1.0,
        _ => throw new ArgumentOutOfRangeException(nameof(Unit)),
    };
}

public static class CadUnits
{
    /// <summary>How many of <paramref name="unit"/> one millimeter is.</summary>
    public static double PerMillimeter(CadUnit unit) => unit switch
    {
        CadUnit.Millimeters => 1.0,
        CadUnit.Centimeters => 0.1,
        CadUnit.Meters => 0.001,
        CadUnit.Inches => 1 / 25.4,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };
}

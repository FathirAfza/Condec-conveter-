// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Architecture;

namespace Condec.Tests.Architecture;

public sealed class ClarityTests
{
    private static SyntheticDrawing Plan()
    {
        // A room with a few strokes in every square, so every square has something to be sharp or soft about.
        var drawing = new SyntheticDrawing(320, 256);
        for (var y = 16; y < 256; y += 32)
        {
            drawing.Line(0, y, 319, y, 2);
        }

        for (var x = 16; x < 320; x += 32)
        {
            drawing.Line(x, 0, x, 255, 2);
        }

        return drawing;
    }

    [Fact]
    public void ASharpDrawingHasNoUnclearAreas()
    {
        Assert.Empty(ClarityAnalyzer.FindUnclearAreas(Plan().Gray));
    }

    [Fact]
    public void PaperAloneIsNotUnclear()
    {
        Assert.Empty(ClarityAnalyzer.FindUnclearAreas(new SyntheticDrawing(200, 200).Gray));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    public void AThinLineBlurredByMoreThanAPixelIsUnclear(int radius, bool expectedSharp)
    {
        var steepness = ClarityAnalyzer.Steepness(Plan().Blurred(radius).Gray, 32, 32);
        Assert.NotNull(steepness);
        Assert.Equal(expectedSharp, steepness >= ClarityAnalyzer.MinimumSteepness);
    }

    [Fact]
    public void ABlurredDrawingIsUnclearEverywhereItHasLines()
    {
        var areas = ClarityAnalyzer.FindUnclearAreas(Plan().Blurred(3).Gray);

        Assert.NotEmpty(areas);
        Assert.True(areas.Sum(a => a.Area) > 0.9 * 320 * 256);
    }

    [Fact]
    public void ADrawingThatIsSoftAllOverIsManyAreasNotOne()
    {
        // 1280 × 1024 is 40 × 32 squares: one lump of them would count as one area and never reach the threshold of five.
        var drawing = new SyntheticDrawing(1280, 1024);
        for (var y = 16; y < 1024; y += 32)
        {
            drawing.Line(0, y, 1279, y, 2);
        }

        for (var x = 16; x < 1280; x += 32)
        {
            drawing.Line(x, 0, x, 1023, 2);
        }

        var areas = ClarityAnalyzer.FindUnclearAreas(drawing.Blurred(3).Gray);

        Assert.True(areas.Count >= ArchitectureUpscalePolicy.UnclearAreaThreshold);
        Assert.All(areas, a => Assert.True(a.Width <= ClarityAnalyzer.MaxAreaCells * ClarityAnalyzer.CellSize && a.Height <= ClarityAnalyzer.MaxAreaCells * ClarityAnalyzer.CellSize));
    }

    [Fact]
    public void OnlyTheSoftPartOfADrawingIsMarked()
    {
        // Sharp on the left, blurred on the right: take the blurred half from a blurred copy.
        var sharp = Plan();
        var soft = Plan().Blurred(3);
        var mixed = new byte[320 * 256];
        var sharpPixels = sharp.Gray.Pixels;
        var softPixels = soft.Gray.Pixels;
        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                mixed[(y * 320) + x] = x < 160 ? sharpPixels[(y * 320) + x] : softPixels[(y * 320) + x];
            }
        }

        var areas = ClarityAnalyzer.FindUnclearAreas(new Condec.Core.Cad.GrayImage(320, 256, mixed));

        Assert.NotEmpty(areas);
        Assert.All(areas, a => Assert.True(a.Left >= 128, $"area at {a.Left} is in the sharp half"));
    }

    [Fact]
    public void AFaintSquareIsLeftAlone()
    {
        // Light grey on white: too little contrast to have a line in it.
        var drawing = new SyntheticDrawing(64, 64);
        drawing.Line(0, 32, 63, 32, 3, (230, 230, 230));

        Assert.Null(ClarityAnalyzer.Steepness(drawing.Gray, 0, 0));
    }
}

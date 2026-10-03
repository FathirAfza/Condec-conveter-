// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Upscale;

public enum UpscaleFormat
{
    Png,
    Jpg,
}

/// <summary>The size of the result and the guesses printed beside it (DESIGN §8). Every figure here is shown with "± ".</summary>
public static class UpscaleEstimator
{
    // Bytes per output pixel. Assumptions (DESIGN §13 #12) until measured on real results.
    private const double PngBytesPerPixel = 1.7;
    private const double JpgBytesPerPixel = 0.4;

    /// <summary><c>round(W × scale) × round(H × scale)</c>.</summary>
    public static (int Width, int Height) OutputSize(int width, int height, double scale) =>
        (Scaled(width, scale), Scaled(height, scale));

    public static long OutputPixels(int width, int height, double scale)
    {
        var (w, h) = OutputSize(width, height, scale);
        return (long)w * h;
    }

    /// <summary>The expected size of the saved file, in bytes.</summary>
    public static long EstimatedFileBytes(long outputPixels, UpscaleFormat format) =>
        (long)Math.Round(outputPixels * (format == UpscaleFormat.Png ? PngBytesPerPixel : JpgBytesPerPixel), MidpointRounding.AwayFromZero);

    /// <summary>
    /// Render time: the megapixels the network works through (whole tiles at 4 times each side of the source, whatever size is
    /// asked for; <see cref="UpscaleSupport.RenderedPixels"/>) ÷ how many megapixels per second the engine measured at the
    /// tile size the memory limit allows, ÷ the share of the time the performance mode works (DESIGN §7.6). Null when the engine
    /// hasn't been measured yet, because the app never shows an invented speed.
    /// </summary>
    public static double? EstimatedSeconds(long networkPixels, double? megapixelsPerSecond, double duty = 1)
    {
        if (megapixelsPerSecond is not > 0)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duty);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(duty, 1);
        return networkPixels / 1_000_000d / megapixelsPerSecond.Value / duty;
    }

    private static int Scaled(int value, double scale) => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
}

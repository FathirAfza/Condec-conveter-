// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Upscale;

/// <summary>
/// Lanczos (a = 3) resizing of 8-bit pictures with any number of interleaved channels, in two passes. The kernel is
/// widened when shrinking, so a smaller result is averaged rather than skipped. The pass in between is kept as bytes
/// (half a gray level of rounding), because a float copy of an 8K picture would not fit on a small PC.
/// </summary>
public static class Resampler
{
    private const int Lobes = 3;

    public static byte[] Resize(byte[] source, int width, int height, int channels, int newWidth, int newHeight, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width < 1 || height < 1 || newWidth < 1 || newHeight < 1 || channels < 1 || source.LongLength != (long)width * height * channels)
        {
            throw new ArgumentException("The pixel data doesn't match the sizes.", nameof(source));
        }

        if (width == newWidth && height == newHeight)
        {
            return (byte[])source.Clone();
        }

        var horizontal = Weights(width, newWidth);
        var vertical = Weights(height, newHeight);

        // The pass that shrinks the picture more goes first, so the one in between is as small as it can be.
        return (long)newWidth * height <= (long)width * newHeight
            ? PassVertical(PassHorizontal(source, width, height, channels, newWidth, horizontal, ct), newWidth, height, channels, newHeight, vertical, ct)
            : PassHorizontal(PassVertical(source, width, height, channels, newHeight, vertical, ct), width, newHeight, channels, newWidth, horizontal, ct);
    }

    /// <summary>For each output index, the first input index and the weights of the input pixels it is made from (summing to 1).</summary>
    private static (int First, float[] Weights)[] Weights(int inputLength, int outputLength)
    {
        var ratio = (double)inputLength / outputLength;
        var filterScale = Math.Max(1, ratio);
        var support = Lobes * filterScale;
        var table = new (int First, float[] Weights)[outputLength];

        for (var o = 0; o < outputLength; o++)
        {
            var center = ((o + 0.5) * ratio) - 0.5;
            var first = (int)Math.Floor(center - support) + 1;
            var last = (int)Math.Floor(center + support);
            var weights = new float[last - first + 1];
            double sum = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                var w = Lanczos((first + i - center) / filterScale);
                weights[i] = (float)w;
                sum += w;
            }

            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] = (float)(weights[i] / sum);
            }

            table[o] = (first, weights);
        }

        return table;
    }

    private static double Lanczos(double x)
    {
        x = Math.Abs(x);
        if (x >= Lobes)
        {
            return 0;
        }

        if (x < 1e-9)
        {
            return 1;
        }

        var px = Math.PI * x;
        return Lobes * Math.Sin(px) * Math.Sin(px / Lobes) / (px * px);
    }

    private static byte[] PassHorizontal(byte[] source, int width, int height, int channels, int newWidth, (int First, float[] Weights)[] table, CancellationToken ct)
    {
        var result = new byte[(long)newWidth * height * channels];
        Parallel.For(0, height, new ParallelOptions { CancellationToken = ct }, y =>
        {
            var row = (long)y * width * channels;
            var target = (long)y * newWidth * channels;
            var sums = new float[channels];
            for (var x = 0; x < newWidth; x++)
            {
                var (first, weights) = table[x];
                Array.Clear(sums);
                for (var i = 0; i < weights.Length; i++)
                {
                    var at = row + ((long)Math.Clamp(first + i, 0, width - 1) * channels);
                    for (var c = 0; c < channels; c++)
                    {
                        sums[c] += source[at + c] * weights[i];
                    }
                }

                for (var c = 0; c < channels; c++)
                {
                    result[target + ((long)x * channels) + c] = (byte)Math.Clamp((int)MathF.Round(sums[c]), 0, 255);
                }
            }
        });
        return result;
    }

    private static byte[] PassVertical(byte[] source, int width, int height, int channels, int newHeight, (int First, float[] Weights)[] table, CancellationToken ct)
    {
        var result = new byte[(long)width * newHeight * channels];
        var rowLength = width * channels;
        Parallel.For(0, newHeight, new ParallelOptions { CancellationToken = ct }, y =>
        {
            var (first, weights) = table[y];
            var sums = new float[rowLength];
            for (var i = 0; i < weights.Length; i++)
            {
                var row = (long)Math.Clamp(first + i, 0, height - 1) * rowLength;
                var weight = weights[i];
                for (var k = 0; k < rowLength; k++)
                {
                    sums[k] += source[row + k] * weight;
                }
            }

            var target = (long)y * rowLength;
            for (var k = 0; k < rowLength; k++)
            {
                result[target + k] = (byte)Math.Clamp((int)MathF.Round(sums[k]), 0, 255);
            }
        });
        return result;
    }
}

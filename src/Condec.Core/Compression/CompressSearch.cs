// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Compression;

/// <summary>
/// Finds a quality and resolution whose file fits a size limit (DESIGN §6.5). The quality goes down first, from 92% to 60%,
/// so the picture keeps its resolution as long as it can; when 60% at full resolution is still too big, the resolution goes
/// down at 60%. This holds for JPG and HEIC. PNG has no quality, so only its resolution goes down. Every answer was really
/// encoded and measured: the search never guesses a size.
/// </summary>
public static class CompressSearch
{
    /// <summary>Above this a JPG grows much faster than it looks better, so a limit that leaves room isn't spent on it.</summary>
    public const int HighestQuality = 92;

    /// <summary>Below this a JPG shows its blocks; a smaller resolution looks better than a lower quality from here.</summary>
    public const int LowestQuality = 60;

    /// <param name="lossy">The format has a quality (JPG, HEIC); false for PNG.</param>
    /// <param name="measure">Encodes the picture with a setting and returns the file's size in bytes.</param>
    /// <returns>The best setting that fits, with its size, or null when even 1% of the resolution is too big.</returns>
    public static async Task<CompressMeasure?> FitAsync(
        bool lossy,
        long targetBytes,
        Func<CompressSetting, CancellationToken, Task<long>> measure,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetBytes);

        async Task<CompressMeasure?> Fits(int quality, int percent)
        {
            ct.ThrowIfCancellationRequested();
            var setting = new CompressSetting(quality, percent);
            var bytes = await measure(setting, ct).ConfigureAwait(false);
            return bytes <= targetBytes ? new CompressMeasure(setting, bytes) : null;
        }

        var fullQuality = lossy ? HighestQuality : CompressOptions.MaximumQuality;
        if (await Fits(fullQuality, CompressOptions.MaximumPercent).ConfigureAwait(false) is { } best)
        {
            return best;
        }

        var shrinkQuality = fullQuality;
        if (lossy)
        {
            // The highest quality at full resolution that fits, between the lowest (fits) and the highest (doesn't).
            if (await Fits(LowestQuality, CompressOptions.MaximumPercent).ConfigureAwait(false) is { } floor)
            {
                var (low, high) = (LowestQuality, HighestQuality);
                best = floor;
                while (high - low > 1)
                {
                    var middle = (low + high) / 2;
                    if (await Fits(middle, CompressOptions.MaximumPercent).ConfigureAwait(false) is { } fits)
                    {
                        (low, best) = (middle, fits);
                    }
                    else
                    {
                        high = middle;
                    }
                }

                return best;
            }

            shrinkQuality = LowestQuality;
        }

        // The highest resolution that fits, between 0 (taken to fit) and 100 (doesn't). A file is not always smaller at a
        // smaller size, so the answer is the best one that was measured to fit, never one in between.
        var (lowPercent, highPercent) = (0, CompressOptions.MaximumPercent);
        best = null;
        while (highPercent - lowPercent > 1)
        {
            var middle = (lowPercent + highPercent) / 2;
            if (await Fits(shrinkQuality, middle).ConfigureAwait(false) is { } fits)
            {
                (lowPercent, best) = (middle, fits);
            }
            else
            {
                highPercent = middle;
            }
        }

        return best;
    }
}

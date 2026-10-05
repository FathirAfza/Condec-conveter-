// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;

namespace Condec.Core.Compression;

/// <summary>
/// How Compress Image makes one picture smaller (DESIGN §6.5): either a quality and a resolution chosen by hand, or a file
/// size limit that the quality and resolution are found for.
/// </summary>
/// <param name="Quality">JPG or HEIC quality from 10 to 100; PNG has none and ignores it.</param>
/// <param name="ResolutionPercent">The result's width and height as a percentage of the source's, 10 to 100.</param>
/// <param name="TargetBytes">The size limit. When set, <paramref name="Quality"/> and <paramref name="ResolutionPercent"/> are ignored.</param>
public sealed record CompressOptions(int Quality, int ResolutionPercent, long? TargetBytes = null) : ConversionOptions
{
    public const int MinimumQuality = 10;
    public const int MaximumQuality = 100;
    public const int DefaultQuality = 80;
    public const int MinimumPercent = 10;
    public const int MaximumPercent = 100;

    /// <summary>The result's size for a percentage of the source's: rounded, never below one pixel.</summary>
    public static (int Width, int Height) ScaledSize(int width, int height, int percent) =>
        (Scale(width, percent), Scale(height, percent));

    private static int Scale(int length, int percent) =>
        Math.Max(1, (int)Math.Round(length * (percent / 100.0), MidpointRounding.AwayFromZero));
}

/// <summary>One way to encode a picture: a quality (ignored by PNG) and a resolution.</summary>
public sealed record CompressSetting(int Quality, int ResolutionPercent);

/// <summary>A setting and the size of the file it made.</summary>
public sealed record CompressMeasure(CompressSetting Setting, long Bytes);

/// <summary>The size limit can't be met, even at the smallest resolution.</summary>
public sealed class CompressTargetTooSmallException(long targetBytes)
    : Exception($"The picture can't be made {targetBytes:N0} bytes or smaller.")
{
    public long TargetBytes { get; } = targetBytes;
}

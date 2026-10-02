// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;

namespace Condec.Core.Media;

/// <summary>The bitrate of MP3, M4A and WMA (the profiles Windows provides: 192, 128 and 96 kbps).</summary>
public enum AudioQuality
{
    High,
    Medium,
    Low,
}

/// <summary>The tallest picture a video result may have. Windows never enlarges a video, and the shape of the picture is kept.</summary>
public enum VideoSize
{
    Original,
    P1080,
    P720,
    P480,
}

/// <summary>What the user chose for an audio or video result. WAV and FLAC are lossless and take neither.</summary>
public sealed record MediaOptions(AudioQuality Audio = AudioQuality.High, VideoSize Video = VideoSize.Original) : ConversionOptions
{
    public static MediaOptions Default { get; } = new();

    /// <summary>The height in pixels, or null for the source's own size.</summary>
    public int? MaxHeight => Video switch
    {
        VideoSize.P1080 => 1080,
        VideoSize.P720 => 720,
        VideoSize.P480 => 480,
        _ => null,
    };

    /// <summary>The bitrate in kilobits per second that <see cref="Audio"/> stands for.</summary>
    public int AudioKbps => Audio switch
    {
        AudioQuality.Medium => 128,
        AudioQuality.Low => 96,
        _ => 192,
    };
}

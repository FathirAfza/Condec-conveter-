// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Windows.Graphics.Imaging;

namespace Condec.Core.Imaging;

/// <summary>The size of a picture as it is shown (EXIF rotation applied) and how many frames it holds.</summary>
public sealed record ImageHeaderInfo(long Width, long Height, uint FrameCount)
{
    public long Pixels => Width * Height;
}

public static class ImageHeader
{
    /// <summary>Reads the picture's header only; no pixels are decoded. Throws when Windows can't read the file as a picture.</summary>
    public static async Task<ImageHeaderInfo> ReadAsync(string path, CancellationToken ct = default)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
        return new ImageHeaderInfo(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight, decoder.FrameCount);
    }
}

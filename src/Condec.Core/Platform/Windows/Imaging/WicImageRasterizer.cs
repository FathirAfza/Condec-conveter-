// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Cad;
using Condec.Core.Formats;
using Windows.Graphics.Imaging;

namespace Condec.Core.Imaging;

/// <summary>Reads pictures for tracing with the Windows Imaging Component, the decoder behind <see cref="ImageConverter"/>.</summary>
public sealed class WicImageRasterizer : IImageRasterizer
{
    private readonly Lazy<HashSet<Guid>> _installedDecoders = new(() =>
        [.. BitmapDecoder.GetDecoderInformationEnumerator().Select(codec => codec.CodecId)]);

    public bool CanDecode(string extension) =>
        ImageFormats.FindDecoder(FileExtension.Normalize(extension)) is { } decoderId && _installedDecoders.Value.Contains(decoderId);

    public async Task<GrayPicture> ReadAsync(string path, CancellationToken ct)
    {
        var decoded = await ImageConverter.DecodeAsync(path, ct).ConfigureAwait(false);

        // Transparent areas count as paper: put them on white before judging how dark a pixel is.
        ImageConverter.FlattenOntoWhite(decoded.Pixels);

        var gray = new byte[decoded.Width * decoded.Height];
        for (var i = 0; i < gray.Length; i++)
        {
            var b = decoded.Pixels[i * 4];
            var g = decoded.Pixels[(i * 4) + 1];
            var r = decoded.Pixels[(i * 4) + 2];
            gray[i] = (byte)(((299 * r) + (587 * g) + (114 * b) + 500) / 1000);
        }

        return new GrayPicture(new GrayImage((int)decoded.Width, (int)decoded.Height, gray), decoded.DpiX, decoded.DpiY);
    }
}

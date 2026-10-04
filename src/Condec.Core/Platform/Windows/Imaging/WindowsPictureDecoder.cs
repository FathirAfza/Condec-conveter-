// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Architecture;
using Condec.Core.Cad;
using Windows.Graphics.Imaging;

namespace Condec.Core.Imaging;

/// <summary>Reads the pictures of a PDF (PNG or JPEG bytes) with the Windows Imaging Component, scaled down while decoding.</summary>
public sealed class WindowsPictureDecoder : IPictureDecoder
{
    public async Task<RasterPicture?> DecodeAsync(byte[] encoded, int maximumPixels, CancellationToken ct)
    {
        try
        {
            using var stream = new MemoryStream(encoded, writable: false).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
            var (width, height) = (decoder.PixelWidth, decoder.PixelHeight);
            if (width == 0 || height == 0)
            {
                return null;
            }

            // Scaled while decoding, so a large photo never takes the memory of its full size.
            var shrink = Math.Min(1, Math.Sqrt((double)maximumPixels / ((double)width * height)));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Floor(width * shrink)),
                ScaledHeight = (uint)Math.Max(1, Math.Floor(height * shrink)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.ColorManageToSRgb).AsTask(ct).ConfigureAwait(false);
            return new RasterPicture((int)transform.ScaledWidth, (int)transform.ScaledHeight, pixels.DetachPixelData(), 96, 96);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            // A picture Windows can't read is left out, with the note, like any other.
            return null;
        }
    }
}

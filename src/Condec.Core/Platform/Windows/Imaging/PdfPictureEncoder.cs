// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices.WindowsRuntime;
using Condec.Core.Compression;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Condec.Core.Imaging;

/// <summary>
/// Encodes the pictures of a PDF again with WIC (DESIGN §6.5). A PDF shows a JPEG's pixels as they are stored, so the EXIF
/// orientation and color profile are ignored when it is read. The result is always an RGB JPEG: the Windows JPEG encoder
/// refuses Gray8 (and Gray16), so a gray picture is written with three equal channels.
/// </summary>
public sealed class PdfPictureEncoder : IPdfPictureEncoder
{
    public async Task<PdfPictureOutput?> EncodeAsync(PdfPictureInput input, int quality, int resolutionPercent, CancellationToken ct)
    {
        var decoded = input.IsJpeg ? await DecodeJpegAsync(input.Data, ct).ConfigureAwait(false) : FromSamples(input);
        if (decoded is not { } picture)
        {
            return null;
        }

        var (width, height) = CompressOptions.ScaledSize(picture.Width, picture.Height, resolutionPercent);
        using var stream = new InMemoryRandomAccessStream();
        try
        {
            var options = new BitmapPropertySet { ["ImageQuality"] = new BitmapTypedValue(quality / 100f, PropertyType.Single) };
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream, options).AsTask(ct).ConfigureAwait(false);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)picture.Width, (uint)picture.Height, 96, 96, picture.Bgra);

            if (width != picture.Width || height != picture.Height)
            {
                encoder.BitmapTransform.ScaledWidth = (uint)width;
                encoder.BitmapTransform.ScaledHeight = (uint)height;
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
            }

            await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ImageConverter.IsOutOfMemory(ex))
        {
            throw new ImageTooLargeException((long)picture.Width * picture.Height, ex);
        }

        var data = new byte[stream.Size];
        using var reader = stream.GetInputStreamAt(0).AsStreamForRead();
        await reader.ReadExactlyAsync(data, ct).ConfigureAwait(false);
        return new PdfPictureOutput(data, width, height);
    }

    private sealed record Decoded(byte[] Bgra, int Width, int Height);

    private static async Task<Decoded?> DecodeJpegAsync(byte[] jpeg, CancellationToken ct)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(jpeg.AsBuffer()).AsTask(ct).ConfigureAwait(false);
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.JpegDecoderId, stream).AsTask(ct).ConfigureAwait(false);
            var pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Ignore,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
            return new Decoded(pixels.DetachPixelData(), (int)decoder.PixelWidth, (int)decoder.PixelHeight);
        }
        catch (Exception ex) when (ImageConverter.IsOutOfMemory(ex))
        {
            throw new ImageTooLargeException(0, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A JPEG the Windows decoder can't read stays in the PDF as it is.
            return null;
        }
    }

    private static Decoded FromSamples(PdfPictureInput input)
    {
        var count = input.Width * input.Height;
        var bgra = new byte[count * 4];
        var samples = input.Data;
        for (var i = 0; i < count; i++)
        {
            byte r, g, b;
            if (input.IsGray)
            {
                r = g = b = samples[i];
            }
            else
            {
                r = samples[i * 3];
                g = samples[(i * 3) + 1];
                b = samples[(i * 3) + 2];
            }

            bgra[i * 4] = b;
            bgra[(i * 4) + 1] = g;
            bgra[(i * 4) + 2] = r;
            bgra[(i * 4) + 3] = 255;
        }

        return new Decoded(bgra, input.Width, input.Height);
    }
}

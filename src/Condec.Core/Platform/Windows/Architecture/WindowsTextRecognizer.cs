// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Condec.Core.Architecture;

/// <summary>
/// Reads lines of writing with the OCR engine of Windows (Windows.Media.Ocr), which works on this device with the language
/// packs the user has installed. There is no engine when none of the user's languages has one.
/// </summary>
public sealed class WindowsTextRecognizer : ITextRecognizer
{
    /// <summary>Lines are read at about this height (pixels): the engine does better on writing of ordinary size than on a few pixels.</summary>
    private const int TargetHeight = 36;

    /// <summary>White all around the line, so letters at its edge are not cut by it.</summary>
    private const int Margin = 8;

    private readonly OcrEngine? _engine;

    public WindowsTextRecognizer()
    {
        _engine = OcrEngine.TryCreateFromUserProfileLanguages();
    }

    /// <summary>An OCR language matches one of the user's languages.</summary>
    public bool IsAvailable => _engine is not null;

    public async Task<string?> RecognizeAsync(RasterPicture line, CancellationToken ct)
    {
        if (_engine is null)
        {
            return null;
        }

        var scale = Math.Clamp((int)Math.Ceiling((double)TargetHeight / Math.Max(1, line.Height)), 1, 6);
        var width = (line.Width * scale) + (2 * Margin);
        var height = (line.Height * scale) + (2 * Margin);
        if (width > OcrEngine.MaxImageDimension || height > OcrEngine.MaxImageDimension)
        {
            return null;
        }

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            Enlarge(line, scale, Margin).AsBuffer(),
            BitmapPixelFormat.Bgra8,
            width,
            height,
            BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        return string.Join(' ', result.Lines.Select(l => l.Text));
    }

    /// <summary>The picture <paramref name="scale"/> times as large (bilinear), on a white margin.</summary>
    internal static byte[] Enlarge(RasterPicture picture, int scale, int margin)
    {
        var width = (picture.Width * scale) + (2 * margin);
        var height = (picture.Height * scale) + (2 * margin);
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)255);

        for (var y = 0; y < picture.Height * scale; y++)
        {
            // The source position of the center of this pixel.
            var sy = Math.Clamp(((y + 0.5) / scale) - 0.5, 0, picture.Height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(picture.Height - 1, y0 + 1);
            var fy = sy - y0;
            for (var x = 0; x < picture.Width * scale; x++)
            {
                var sx = Math.Clamp(((x + 0.5) / scale) - 0.5, 0, picture.Width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(picture.Width - 1, x0 + 1);
                var fx = sx - x0;
                var o = ((((y + margin) * width) + x + margin) * 4);
                for (var channel = 0; channel < 3; channel++)
                {
                    var top = (picture.Bgra[(((y0 * picture.Width) + x0) * 4) + channel] * (1 - fx)) + (picture.Bgra[(((y0 * picture.Width) + x1) * 4) + channel] * fx);
                    var bottom = (picture.Bgra[(((y1 * picture.Width) + x0) * 4) + channel] * (1 - fx)) + (picture.Bgra[(((y1 * picture.Width) + x1) * 4) + channel] * fx);
                    pixels[o + channel] = (byte)Math.Round((top * (1 - fy)) + (bottom * fy));
                }
            }
        }

        return pixels;
    }
}

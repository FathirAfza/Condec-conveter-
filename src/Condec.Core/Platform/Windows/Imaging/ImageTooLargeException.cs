// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Imaging;

/// <summary>
/// The picture has more pixels than can be held in memory for a conversion, or this computer ran out of memory
/// while converting it. A picture is converted whole (as 4 bytes per pixel), so this is a limit of the machine.
/// </summary>
public sealed class ImageTooLargeException(long pixels, Exception? innerException = null)
    : Exception($"The picture has {pixels:N0} pixels, which is more than can be converted in memory.", innerException)
{
    /// <summary>A pixel array of 4 bytes per pixel can't be longer than a .NET array (just under 2 GB).</summary>
    public const long MaximumPixels = int.MaxValue / 4;

    public long Pixels { get; } = pixels;

    public long Megapixels => (Pixels + 500_000) / 1_000_000;
}

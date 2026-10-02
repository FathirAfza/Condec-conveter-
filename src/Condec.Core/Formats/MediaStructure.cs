// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Formats;

/// <summary>
/// Reads what a WAV, AVI, WMA, WMV or FLAC file says about its own length. Windows converts a cut-off file of
/// these types without an error and simply ends the result early, so a conversion would be "verified" while the
/// source was damaged. MP4 and M4A need no check here: Windows refuses them when they are cut. MP3 has no header
/// that states its length, so a cut MP3 can't be told from a short one.
/// </summary>
public static class MediaStructure
{
    private static readonly byte[] AsfHeaderGuid = [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];
    private static readonly byte[] AsfFilePropertiesGuid = [0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11, 0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65];

    /// <summary>
    /// True when the file is as long as it declares (RIFF size for WAV and AVI, file size for ASF), false when it is
    /// shorter. Null when the type isn't one of these or the file doesn't state a length (a stream written live).
    /// </summary>
    public static bool? IsComplete(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return IsComplete(stream);
    }

    public static bool? IsComplete(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var head = new byte[16];
        stream.Position = 0;
        var read = stream.Read(head, 0, head.Length);

        if (read >= 4 && head.AsSpan(0, 4).SequenceEqual("RIFF"u8))
        {
            return Riff(stream, head, read);
        }

        if (read >= 4 && AsfHeaderGuid.AsSpan(0, Math.Min(read, 16)).SequenceEqual(head.AsSpan(0, Math.Min(read, 16))))
        {
            return read < 16 ? false : Asf(stream);
        }

        return null;
    }

    /// <summary>
    /// The length a FLAC file states in its STREAMINFO block, or null for any other file or when it states none.
    /// Windows' own estimate of a FLAC's length is not reliable (2.0 s for a 3.0 s file), so this is the one to use.
    /// </summary>
    public static TimeSpan? GetDeclaredDuration(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return GetDeclaredDuration(stream);
    }

    public static TimeSpan? GetDeclaredDuration(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var head = new byte[8 + 34];
        stream.Position = 0;
        if (stream.Read(head, 0, head.Length) != head.Length
            || !head.AsSpan(0, 4).SequenceEqual("fLaC"u8)
            || (head[4] & 0x7F) != 0
            || ((head[5] << 16) | (head[6] << 8) | head[7]) != 34)
        {
            return null;
        }

        // STREAMINFO: 20 bits sample rate, 3 bits channels - 1, 5 bits bits per sample - 1, 36 bits total samples.
        var packed = 0UL;
        for (var i = 8 + 10; i < 8 + 18; i++)
        {
            packed = (packed << 8) | head[i];
        }

        var sampleRate = (packed >> 44) & 0xFFFFF;
        var totalSamples = packed & 0xFFFFFFFFFUL;
        return sampleRate == 0 || totalSamples == 0
            ? null
            : TimeSpan.FromSeconds((double)totalSamples / sampleRate);
    }

    /// <summary>"RIFF", the size of everything after those 8 bytes, then "WAVE" or "AVI ".</summary>
    private static bool? Riff(Stream stream, byte[] head, int read)
    {
        if (read < 12)
        {
            return false;
        }

        if (!head.AsSpan(8, 4).SequenceEqual("WAVE"u8) && !head.AsSpan(8, 4).SequenceEqual("AVI "u8))
        {
            return null;
        }

        var size = (uint)(head[4] | (head[5] << 8) | (head[6] << 16) | (head[7] << 24));

        // Zero and 0xFFFFFFFF are what a writer leaves when it didn't know the length yet.
        return size is 0 or uint.MaxValue ? null : stream.Length >= (long)size + 8;
    }

    /// <summary>The ASF header holds a File Properties object with the size of the whole file.</summary>
    private static bool? Asf(Stream stream)
    {
        // Header object: GUID, size (8), object count (4), 2 reserved bytes; then the objects it holds.
        var header = new byte[30];
        stream.Position = 0;
        if (stream.Read(header, 0, header.Length) != header.Length)
        {
            return false;
        }

        var headerSize = BitConverter.ToUInt64(header, 16);
        if (headerSize > (ulong)stream.Length)
        {
            return false;
        }

        // The header holds a handful of objects and is small; a huge claim is not worth following.
        if (headerSize is < 30 or > 16 * 1024 * 1024)
        {
            return null;
        }

        var body = new byte[headerSize - 30];
        if (stream.Read(body, 0, body.Length) != body.Length)
        {
            return false;
        }

        var offset = 0;
        while (offset + 24 <= body.Length)
        {
            var objectSize = BitConverter.ToUInt64(body, offset + 16);
            if (objectSize < 24 || objectSize > (ulong)(body.Length - offset))
            {
                return null;
            }

            if (body.AsSpan(offset, 16).SequenceEqual(AsfFilePropertiesGuid) && objectSize >= 24 + 16 + 8 + 8 + 8 + 8 + 8 + 8 + 4)
            {
                var fileSize = BitConverter.ToUInt64(body, offset + 24 + 16);
                var flags = BitConverter.ToUInt32(body, offset + 24 + 16 + 8 + 8 + 8 + 8 + 8 + 8);

                // Bit 0 is the broadcast flag: the sizes in a live stream's header aren't final.
                return fileSize == 0 || (flags & 1) != 0 ? null : (ulong)stream.Length >= fileSize;
            }

            offset += (int)objectSize;
        }

        return null;
    }
}

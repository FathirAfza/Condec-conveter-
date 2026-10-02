// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Formats;

/// <summary>
/// Checks that a PNG, JPEG, GIF or WebP file is whole: that its own structure reaches the end marker before the file
/// ends. Windows decodes a cut-off file of these types without an error and fills in what is missing, so a conversion
/// would be "verified" while the picture is damaged. BMP and TIFF are not checked here: Windows already refuses them.
/// </summary>
public static class ImageStructure
{
    /// <summary>
    /// True when the file reaches its end marker (PNG IEND, JPEG EOI, GIF trailer) or its declared length (WebP).
    /// False when it ends before that. Null when the type isn't one of these, or its structure can't be followed:
    /// then nothing is claimed either way.
    /// </summary>
    public static bool? IsComplete(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var reader = new ByteReader(stream);
        var first = reader.Read();
        var second = reader.Read();

        return (first, second) switch
        {
            (0x89, 0x50) => Png(stream),
            (0xFF, 0xD8) => Jpeg(reader),
            (0x47, 0x49) => Gif(reader),
            (0x52, 0x49) => WebP(stream),
            _ => null,
        };
    }

    public static bool? IsComplete(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        return IsComplete(stream);
    }

    // ---- PNG: signature, then chunks (length, type, data, CRC) up to IEND ----

    private static bool? Png(Stream stream)
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var head = new byte[8];
        stream.Position = 0;
        var read = stream.Read(head, 0, 8);
        if (!head.AsSpan(0, read).SequenceEqual(signature.AsSpan(0, read)))
        {
            return null;
        }

        // The file ends inside the signature.
        if (read < 8)
        {
            return false;
        }

        var chunkHead = new byte[8];
        while (true)
        {
            if (stream.Read(chunkHead, 0, 8) != 8)
            {
                return false;
            }

            var length = (uint)((chunkHead[0] << 24) | (chunkHead[1] << 16) | (chunkHead[2] << 8) | chunkHead[3]);
            if (length > int.MaxValue)
            {
                return null;
            }

            if (chunkHead[4] == (byte)'I' && chunkHead[5] == (byte)'E' && chunkHead[6] == (byte)'N' && chunkHead[7] == (byte)'D')
            {
                return stream.Length - stream.Position >= length + 4;
            }

            // The data and the 4-byte CRC must lie inside the file.
            if (stream.Length - stream.Position < (long)length + 4)
            {
                return false;
            }

            stream.Seek((long)length + 4, SeekOrigin.Current);
        }
    }

    // ---- JPEG: marker segments; after a scan header comes entropy-coded data up to the next real marker ----

    private static bool? Jpeg(ByteReader reader)
    {
        var marker = NextMarker(reader);
        while (true)
        {
            if (marker < 0)
            {
                return false;
            }

            if (marker == 0xD9)
            {
                return true;
            }

            // Markers without a length: padding, TEM, RSTn and a stray SOI.
            if (marker is 0x00 or 0x01 || marker is >= 0xD0 and <= 0xD8)
            {
                marker = NextMarker(reader);
                continue;
            }

            var high = reader.Read();
            var low = reader.Read();
            if (low < 0)
            {
                return false;
            }

            var length = (high << 8) | low;
            if (length < 2)
            {
                return null;
            }

            if (!reader.Skip(length - 2))
            {
                return false;
            }

            marker = marker == 0xDA ? SkipEntropyData(reader) : NextMarker(reader);
        }
    }

    /// <summary>The code after the next 0xFF (fill bytes skipped), or -1 at the end of the file.</summary>
    private static int NextMarker(ByteReader reader)
    {
        int b;
        do
        {
            b = reader.Read();
            if (b < 0)
            {
                return -1;
            }
        }
        while (b != 0xFF);

        do
        {
            b = reader.Read();
        }
        while (b == 0xFF);

        return b;
    }

    /// <summary>Skips the scan's data (0xFF00 is a data byte, RSTn a restart) and returns the marker that ends it.</summary>
    private static int SkipEntropyData(ByteReader reader)
    {
        while (true)
        {
            var marker = NextMarker(reader);
            if (marker is not 0x00 && marker is not (>= 0xD0 and <= 0xD7))
            {
                return marker;
            }
        }
    }

    // ---- GIF: header, screen descriptor, blocks up to the trailer (0x3B) ----

    private static bool? Gif(ByteReader reader)
    {
        // "GI" is read already; "F87a" or "F89a" follows, then the 7-byte logical screen descriptor.
        var head = new int[11];
        for (var i = 0; i < head.Length; i++)
        {
            head[i] = reader.Read();
        }

        // The file ends inside the header or the screen descriptor.
        if (head[^1] < 0)
        {
            var startsLikeGif = (head[0] is < 0 or 'F') && (head[1] is < 0 or '8') && (head[3] is < 0 or 'a');
            return startsLikeGif ? false : null;
        }

        if (head[0] != 'F' || head[1] != '8' || head[3] != 'a')
        {
            return null;
        }

        // head[4..10] is the screen descriptor; its 5th byte (head[8]) holds the global color table flags.
        if (!SkipColorTable(reader, head[8]))
        {
            return false;
        }

        while (true)
        {
            var block = reader.Read();
            switch (block)
            {
                case < 0:
                    return false;
                case 0x3B:
                    return true;
                case 0x21:
                    if (reader.Read() < 0 || !SkipSubBlocks(reader))
                    {
                        return false;
                    }

                    break;
                case 0x2C:
                    // Left, top, width, height (8 bytes), then a flags byte for the local color table.
                    if (!reader.Skip(8))
                    {
                        return false;
                    }

                    var flags = reader.Read();
                    if (flags < 0 || !SkipColorTable(reader, flags) || reader.Read() < 0 || !SkipSubBlocks(reader))
                    {
                        return false;
                    }

                    break;
                default:
                    return null;
            }
        }
    }

    private static bool SkipColorTable(ByteReader reader, int flags) =>
        (flags & 0x80) == 0 || reader.Skip(3 * (1 << ((flags & 7) + 1)));

    private static bool SkipSubBlocks(ByteReader reader)
    {
        while (true)
        {
            var size = reader.Read();
            if (size < 0)
            {
                return false;
            }

            if (size == 0)
            {
                return true;
            }

            if (!reader.Skip(size))
            {
                return false;
            }
        }
    }

    // ---- WebP: "RIFF", the size of everything after those 8 bytes, "WEBP" ----

    private static bool? WebP(Stream stream)
    {
        var head = new byte[12];
        stream.Position = 0;
        var read = stream.Read(head, 0, 12);
        if (!"RIFF"u8.StartsWith(head.AsSpan(0, Math.Min(read, 4))))
        {
            return null;
        }

        // The file ends inside the header.
        if (read < 12)
        {
            return false;
        }

        if (head[8] != 'W' || head[9] != 'E' || head[10] != 'B' || head[11] != 'P')
        {
            return null;
        }

        var size = (uint)(head[4] | (head[5] << 8) | (head[6] << 16) | (head[7] << 24));
        return stream.Length >= (long)size + 8;
    }

    /// <summary>Reads a stream a byte at a time from a buffer; -1 at the end.</summary>
    private sealed class ByteReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[65536];
        private int _position;
        private int _length;

        public int Read()
        {
            if (_position == _length && !Fill())
            {
                return -1;
            }

            return _buffer[_position++];
        }

        /// <summary>False when the stream ends before <paramref name="count"/> bytes were skipped.</summary>
        public bool Skip(long count)
        {
            while (count > 0)
            {
                if (_position == _length && !Fill())
                {
                    return false;
                }

                var step = (int)Math.Min(count, _length - _position);
                _position += step;
                count -= step;
            }

            return true;
        }

        private bool Fill()
        {
            _length = stream.Read(_buffer, 0, _buffer.Length);
            _position = 0;
            return _length > 0;
        }
    }
}

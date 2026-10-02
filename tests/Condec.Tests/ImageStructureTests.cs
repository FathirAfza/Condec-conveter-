// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Formats;

namespace Condec.Tests;

/// <summary>Crafted files, so these run without Windows: whole ones are complete, every cut of them is not.</summary>
public sealed class ImageStructureTests
{
    [Fact]
    public void Png_WhenWhole_IsComplete()
    {
        Assert.True(IsComplete(Png()));
    }

    [Fact]
    public void Png_CutAnywhereBeforeTheEnd_IsIncomplete()
    {
        var file = Png();

        // The signature alone has no chunks; the cut at length - 1 lacks the last CRC byte of IEND.
        for (var length = 8; length < file.Length; length++)
        {
            Assert.False(IsComplete(file[..length]), $"cut at {length} of {file.Length}");
        }
    }

    [Fact]
    public void Jpeg_WhenWhole_IsComplete()
    {
        Assert.True(IsComplete(Jpeg()));
    }

    [Fact]
    public void Jpeg_CutAnywhereBeforeTheEnd_IsIncomplete()
    {
        var file = Jpeg();

        for (var length = 2; length < file.Length; length++)
        {
            Assert.False(IsComplete(file[..length]), $"cut at {length} of {file.Length}");
        }
    }

    [Fact]
    public void Jpeg_DataBytesThatLookLikeMarkers_AreNotTheEnd()
    {
        // 0xFF00 is a data byte and 0xFFD0..D7 a restart: neither ends the scan. Only the real EOI does.
        byte[] scan = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56, 0xFF, 0xD3, 0x78];
        var file = Jpeg(scan);

        Assert.True(IsComplete(file));
        Assert.False(IsComplete(file[..^2]));
        Assert.False(IsComplete(file[..^3]));
    }

    [Fact]
    public void Gif_WhenWhole_IsComplete()
    {
        Assert.True(IsComplete(Gif(frames: 1)));
        Assert.True(IsComplete(Gif(frames: 3)));
    }

    [Fact]
    public void Gif_CutAnywhereBeforeTheEnd_IsIncomplete()
    {
        var file = Gif(frames: 2);

        for (var length = 2; length < file.Length; length++)
        {
            Assert.False(IsComplete(file[..length]), $"cut at {length} of {file.Length}");
        }
    }

    [Fact]
    public void WebP_ComparesTheDeclaredSizeWithTheFileLength()
    {
        var file = WebP(payload: 40);

        Assert.True(IsComplete(file));
        Assert.True(IsComplete([.. file, 0, 0]));
        Assert.False(IsComplete(file[..^1]));
        Assert.False(IsComplete(file[..20]));
    }

    [Fact]
    public void OtherFormats_AreNotJudged()
    {
        Assert.Null(IsComplete([0x42, 0x4D, 0x00, 0x00, 0x00, 0x00]));
        Assert.Null(IsComplete([0x49, 0x49, 0x2A, 0x00, 0x08, 0x00]));
        Assert.Null(IsComplete([]));
        Assert.Null(IsComplete(TestData.Bytes(200)));
    }

    [Fact]
    public void AFileOnDisk_IsReadFromItsPath()
    {
        using var dir = new TempDirectory();
        var whole = dir.File("utuh.png");
        var cut = dir.File("terpotong.png");
        File.WriteAllBytes(whole, Png());
        File.WriteAllBytes(cut, Png()[..30]);

        Assert.True(ImageStructure.IsComplete(whole));
        Assert.False(ImageStructure.IsComplete(cut));
    }

    private static bool? IsComplete(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return ImageStructure.IsComplete(stream);
    }

    /// <summary>Chunk CRCs aren't checked by <see cref="ImageStructure"/>, so zeros stand in for them.</summary>
    private static byte[] Png()
    {
        List<byte> bytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Chunk(bytes, "IHDR", new byte[13]);
        Chunk(bytes, "IDAT", TestData.Bytes(300));
        Chunk(bytes, "IDAT", TestData.Bytes(17));
        Chunk(bytes, "IEND", []);
        return [.. bytes];
    }

    private static void Chunk(List<byte> bytes, string type, byte[] data)
    {
        bytes.AddRange([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length]);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
        bytes.AddRange(data);
        bytes.AddRange(new byte[4]);
    }

    private static byte[] Jpeg(byte[]? scan = null)
    {
        List<byte> bytes = [0xFF, 0xD8];
        Segment(bytes, 0xE0, TestData.Bytes(14));
        Segment(bytes, 0xDB, TestData.Bytes(65));
        Segment(bytes, 0xC0, TestData.Bytes(15));
        Segment(bytes, 0xC4, TestData.Bytes(30));
        Segment(bytes, 0xDA, TestData.Bytes(10));

        // Entropy-coded data holds no 0xFF unless it is stuffed (0xFF00) or a restart marker.
        bytes.AddRange(scan ?? [.. TestData.Bytes(120).Select(b => b == 0xFF ? (byte)0x7F : b)]);
        bytes.AddRange([0xFF, 0xD9]);
        return [.. bytes];
    }

    private static void Segment(List<byte> bytes, byte marker, byte[] data)
    {
        bytes.AddRange([0xFF, marker, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2)]);
        bytes.AddRange(data.Select(b => b == 0xFF ? (byte)0x7F : b));
    }

    /// <summary>A global color table of 4 entries, and per frame a graphic control extension and an image.</summary>
    private static byte[] Gif(int frames)
    {
        List<byte> bytes = [.. "GIF89a"u8, 4, 0, 4, 0, 0x81, 0, 0];
        bytes.AddRange(new byte[3 * 4]);
        for (var i = 0; i < frames; i++)
        {
            bytes.AddRange([0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00]);
            bytes.AddRange([0x2C, 0, 0, 0, 0, 4, 0, 4, 0, 0x00]);
            bytes.AddRange([0x02, 0x05, 0x84, 0x1D, 0x81, 0x7A, 0x50, 0x00]);
        }

        bytes.Add(0x3B);
        return [.. bytes];
    }

    private static byte[] WebP(int payload)
    {
        // The size counts everything after the first 8 bytes: "WEBP" plus the payload.
        var size = 4 + payload;
        List<byte> bytes = [.. "RIFF"u8, (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24), .. "WEBP"u8];
        bytes.AddRange(TestData.Bytes(payload));
        return [.. bytes];
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Formats;

namespace Condec.Tests;

/// <summary>Crafted files, so these run without Windows: whole ones are complete, every cut of them is not.</summary>
public sealed class MediaStructureTests
{
    [Fact]
    public void Wav_WhenWhole_IsComplete()
    {
        Assert.True(IsComplete(Riff("WAVE", payload: 400)));
    }

    [Fact]
    public void Wav_CutAnywhereBeforeTheEnd_IsIncomplete()
    {
        var file = Riff("WAVE", payload: 400);

        for (var length = 4; length < file.Length; length++)
        {
            Assert.False(IsComplete(file[..length]), $"cut at {length} of {file.Length}");
        }
    }

    [Fact]
    public void Avi_IsJudgedLikeWav()
    {
        var file = Riff("AVI ", payload: 300);

        Assert.True(IsComplete(file));
        Assert.False(IsComplete(file[..^1]));
    }

    [Fact]
    public void Riff_OfAnotherKind_IsNotJudged()
    {
        Assert.Null(IsComplete(Riff("WEBP", payload: 40)));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void Wav_WithoutAFinalSize_IsNotJudged(uint declared)
    {
        // A writer that doesn't know the length yet (a live recording) leaves 0 or all ones.
        var file = Riff("WAVE", payload: 400);
        BitConverter.GetBytes(declared).CopyTo(file, 4);

        Assert.Null(IsComplete(file[..200]));
    }

    [Fact]
    public void Asf_WhenWhole_IsComplete()
    {
        Assert.True(IsComplete(Asf(dataLength: 500)));
    }

    [Fact]
    public void Asf_CutAnywhereBeforeTheEnd_IsIncomplete()
    {
        var file = Asf(dataLength: 500);

        for (var length = 4; length < file.Length; length++)
        {
            Assert.False(IsComplete(file[..length]), $"cut at {length} of {file.Length}");
        }
    }

    [Fact]
    public void Asf_Broadcast_IsNotJudged()
    {
        // Bit 0 of the flags: the sizes in the header of a live stream aren't final.
        var file = Asf(dataLength: 500, flags: 1);

        Assert.Null(IsComplete(file[..300]));
    }

    [Fact]
    public void OtherFormats_AreNotJudged()
    {
        Assert.Null(IsComplete([0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]));
        Assert.Null(IsComplete([0x66, 0x4C, 0x61, 0x43, 0x00]));
        Assert.Null(IsComplete([]));
        Assert.Null(IsComplete(TestData.Bytes(200)));
    }

    [Fact]
    public void Flac_StatesItsLength()
    {
        var file = Flac(sampleRate: 44100, totalSamples: 132300);

        Assert.Equal(TimeSpan.FromSeconds(3), Declared(file));
    }

    [Theory]
    [InlineData(0u, 132300UL)]
    [InlineData(44100u, 0UL)]
    public void Flac_WithoutALength_StatesNone(uint sampleRate, ulong totalSamples)
    {
        Assert.Null(Declared(Flac(sampleRate, totalSamples)));
    }

    [Fact]
    public void DeclaredDuration_IsOnlyForFlac()
    {
        Assert.Null(Declared(Riff("WAVE", payload: 400)));
        Assert.Null(Declared(Flac(44100, 132300)[..20]));
        Assert.Null(Declared([]));
    }

    [Fact]
    public void AFileOnDisk_IsReadFromItsPath()
    {
        using var dir = new TempDirectory();
        var whole = dir.File("utuh.wav");
        var cut = dir.File("terpotong.wav");
        var flac = dir.File("lagu.flac");
        File.WriteAllBytes(whole, Riff("WAVE", payload: 400));
        File.WriteAllBytes(cut, Riff("WAVE", payload: 400)[..100]);
        File.WriteAllBytes(flac, Flac(48000, 144000));

        Assert.True(MediaStructure.IsComplete(whole));
        Assert.False(MediaStructure.IsComplete(cut));
        Assert.Equal(TimeSpan.FromSeconds(3), MediaStructure.GetDeclaredDuration(flac));
    }

    private static bool? IsComplete(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return MediaStructure.IsComplete(stream);
    }

    private static TimeSpan? Declared(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return MediaStructure.GetDeclaredDuration(stream);
    }

    /// <summary>"RIFF", the size of what follows, the form type, and <paramref name="payload"/> bytes of chunks.</summary>
    private static byte[] Riff(string form, int payload)
    {
        List<byte> bytes = [.. "RIFF"u8, .. BitConverter.GetBytes(4 + payload), .. System.Text.Encoding.ASCII.GetBytes(form)];
        bytes.AddRange(TestData.Bytes(payload));
        return [.. bytes];
    }

    private static readonly byte[] AsfHeaderGuid = [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11, 0xA6, 0xD9, 0x00, 0xAA, 0x00, 0x62, 0xCE, 0x6C];
    private static readonly byte[] AsfFilePropertiesGuid = [0xA1, 0xDC, 0xAB, 0x8C, 0x47, 0xA9, 0xCF, 0x11, 0x8E, 0xE4, 0x00, 0xC0, 0x0C, 0x20, 0x53, 0x65];

    /// <summary>A header with one other object and the File Properties object, then a data object.</summary>
    private static byte[] Asf(int dataLength, uint flags = 0)
    {
        List<byte> other = [.. TestData.Bytes(16), .. BitConverter.GetBytes(24UL + 30), .. TestData.Bytes(30)];
        const int FilePropertiesSize = 24 + 80;

        var headerSize = 30 + other.Count + FilePropertiesSize;
        var fileSize = (ulong)(headerSize + dataLength);

        List<byte> bytes = [.. AsfHeaderGuid, .. BitConverter.GetBytes((ulong)headerSize), .. BitConverter.GetBytes(2u), 0x01, 0x02];
        bytes.AddRange(other);
        bytes.AddRange(AsfFilePropertiesGuid);
        bytes.AddRange(BitConverter.GetBytes((ulong)FilePropertiesSize));
        bytes.AddRange(new byte[16]);
        bytes.AddRange(BitConverter.GetBytes(fileSize));
        bytes.AddRange(new byte[8 * 5]);
        bytes.AddRange(BitConverter.GetBytes(flags));
        bytes.AddRange(new byte[12]);
        bytes.AddRange(TestData.Bytes(dataLength));
        return [.. bytes];
    }

    /// <summary>"fLaC" and a STREAMINFO block: 20 bits rate, 3 bits channels - 1, 5 bits depth - 1, 36 bits samples.</summary>
    private static byte[] Flac(uint sampleRate, ulong totalSamples)
    {
        var packed = ((ulong)sampleRate << 44) | (1UL << 41) | (15UL << 36) | totalSamples;
        List<byte> bytes = [.. "fLaC"u8, 0x80, 0x00, 0x00, 34, .. new byte[10]];
        for (var shift = 56; shift >= 0; shift -= 8)
        {
            bytes.Add((byte)(packed >> shift));
        }

        bytes.AddRange(new byte[16]);
        return [.. bytes];
    }
}

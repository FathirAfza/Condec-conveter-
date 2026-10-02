// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Localization;
using Condec.Core.Media;
using Condec.Core.Pipeline;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Condec.Tests.Media;

/// <summary>Runs the real Windows media transcoder through the full pipeline, on media made here.</summary>
public sealed class MediaConverterTests : IDisposable
{
    private const int Seconds = 3;
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Staging => _dir.File("staging");

    [Fact]
    public void Targets_AreAudioOnly_ForAnAudioSource_AndHaveVideoToo_ForAVideo()
    {
        var converter = new MediaConverter(Staging);

        Assert.DoesNotContain(".mp4", converter.GetTargets(".wav"));
        Assert.DoesNotContain(".wmv", converter.GetTargets(".mp3"));
        Assert.All(converter.GetTargets(".flac"), extension => Assert.Contains(extension, new[] { ".mp3", ".m4a", ".wav", ".wma", ".flac" }));

        var fromVideo = converter.GetTargets(".mp4");
        if (fromVideo.Count > 0)
        {
            Assert.Contains(".mp3", fromVideo);
        }

        Assert.Empty(converter.GetTargets(".docx"));
        Assert.Empty(converter.GetTargets(".png"));
        Assert.Empty(converter.GetTargets(".mkv"));
    }

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".m4a")]
    [InlineData(".wma")]
    [InlineData(".flac")]
    public async Task Wav_ToAudioFormat_IsConvertedVerifiedAndHasTheSameLength(string target)
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(target), $"{target} can't be written on this machine.");
        var source = _dir.File("nada.wav");
        WriteWav(source, Seconds);
        var destination = _dir.File("hasil" + target);

        var result = await CreatePipeline().RunAsync(new ConversionJob(source, target, destination), null, Ct);

        Assert.Empty(result.Notes ?? []);
        Assert.True(File.Exists(destination));
        await AssertLengthAsync(destination, target, Seconds);
        AssertStagingEmpty();
    }

    [Fact]
    public async Task Wav_ToFlac_AndBack_KeepsEverySample()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".flac"), "FLAC can't be written on this machine.");
        var source = _dir.File("asli.wav");
        WriteWav(source, Seconds, sampleRate: 44100, channels: 2);
        var flac = _dir.File("tengah.flac");
        var back = _dir.File("kembali.wav");

        await CreatePipeline().RunAsync(new ConversionJob(source, ".flac", flac), null, Ct);
        await CreatePipeline().RunAsync(new ConversionJob(flac, ".wav", back), null, Ct);

        // The standard profiles would turn 44.1 kHz into 48 kHz (and 16 into 24 bit); lossless means the same samples.
        var original = await File.ReadAllBytesAsync(source, Ct);
        var returned = await File.ReadAllBytesAsync(back, Ct);
        Assert.Equal(PcmOf(original), PcmOf(returned));
        Assert.Equal((44100, 2), FormatOf(returned));
    }

    [Fact]
    public async Task Mono_StaysMono_ThroughFlac()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".flac"), "FLAC can't be written on this machine.");
        var source = _dir.File("mono.wav");
        WriteWav(source, Seconds, sampleRate: 22050, channels: 1);

        await CreatePipeline().RunAsync(new ConversionJob(source, ".flac", _dir.File("mono.flac")), null, Ct);

        var back = _dir.File("mono-kembali.wav");
        await CreatePipeline().RunAsync(new ConversionJob(_dir.File("mono.flac"), ".wav", back), null, Ct);
        var (sampleRate, channels) = FormatOf(await File.ReadAllBytesAsync(back, Ct));
        Assert.Equal(1, channels);

        // The FLAC encoder in Windows doesn't write below 44.1 kHz, so 22.05 kHz comes back doubled.
        Assert.Contains(sampleRate, new[] { 22050, 44100 });
    }

    [Fact]
    public async Task Video_ToAudio_TakesTheSoundOut()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".mp4").Contains(".mp3"), "MP3 can't be written on this machine.");
        var source = await MakeVideoAsync(".mp4");
        var destination = _dir.File("suara.mp3");

        var result = await CreatePipeline().RunAsync(new ConversionJob(source, ".mp3", destination), null, Ct);

        Assert.Empty(result.Notes ?? []);
        await AssertLengthAsync(destination, ".mp3", 3);
    }

    [Theory]
    [InlineData(".mp4", ".wmv")]
    [InlineData(".m4v", ".mp4")]
    [InlineData(".mov", ".mp4")]
    public async Task Video_ToVideo_IsConverted(string sourceExtension, string target)
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(sourceExtension).Contains(target), $"{target} can't be written on this machine.");
        var video = await MakeVideoAsync(".mp4");
        var source = _dir.File("sumber" + sourceExtension);
        File.Move(video, source);
        var destination = _dir.File("hasil" + target);

        await CreatePipeline().RunAsync(new ConversionJob(source, target, destination), null, Ct);

        // Windows' WMV writer ends the file a second after the picture (3 s in, 4 s out).
        await AssertLengthAsync(destination, target, target == ".wmv" ? 4 : 3);
    }

    [Fact]
    public void SameFormat_IsOfferedOnlyWhereTheQualityOrSizeCanChange()
    {
        var registry = new ConverterRegistry([new MediaConverter(Staging)], [new MediaOutputValidator()]);
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".mp4").Contains(".mp4") || !converter.GetTargets(".mp3").Contains(".mp3"), "MP3 or MP4 can't be written on this machine.");

        Assert.Contains(registry.GetTargetOptions(".mp3"), o => o.Extension == ".mp3");
        Assert.Contains(registry.GetTargetOptions(".mp4"), o => o.Extension == ".mp4");

        // Lossless formats have nothing to change; a video isn't offered as itself in another container's name.
        Assert.DoesNotContain(registry.GetTargetOptions(".wav"), o => o.Extension == ".wav");
        Assert.DoesNotContain(registry.GetTargetOptions(".flac"), o => o.Extension == ".flac");
        Assert.DoesNotContain(registry.GetTargetOptions(".m4v"), o => o.Extension == ".m4v");
        Assert.DoesNotContain(registry.GetTargetOptions(".mov"), o => o.Extension == ".mov");
    }

    [Fact]
    public async Task AFileCanBeConvertedToItsOwnFormatUnderAnotherName_ButNotOverItself()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".mp3").Contains(".mp3"), "MP3 can't be written on this machine.");
        var wav = _dir.File("asli.wav");
        WriteWav(wav, Seconds);
        var source = _dir.File("lagu.mp3");
        await CreatePipeline().RunAsync(new ConversionJob(wav, ".mp3", source), null, Ct);

        await CreatePipeline().RunAsync(new ConversionJob(source, ".mp3", _dir.File("lagu (hasil).mp3"), new MediaOptions(Audio: AudioQuality.Low)), null, Ct);
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreatePipeline().RunAsync(new ConversionJob(source, ".mp3", source), null, Ct));

        Assert.True(new FileInfo(_dir.File("lagu (hasil).mp3")).Length < new FileInfo(source).Length);
    }

    [Theory]
    [InlineData(".mp3")]
    [InlineData(".m4a")]
    [InlineData(".wma")]
    public async Task AudioQuality_SetsTheBitrate(string target)
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(target), $"{target} can't be written on this machine.");
        var source = _dir.File("nada.wav");
        WriteWav(source, Seconds);

        var sizes = new Dictionary<AudioQuality, long>();
        foreach (var (quality, kbps) in new[] { (AudioQuality.High, 192), (AudioQuality.Medium, 128), (AudioQuality.Low, 96) })
        {
            var destination = _dir.File($"hasil-{quality}{target}");
            await CreatePipeline().RunAsync(new ConversionJob(source, target, destination, new MediaOptions(Audio: quality)), null, Ct);

            Assert.Equal(kbps, (int)Math.Round((await ProfileOfAsync(destination)).Audio.Bitrate / 1000.0));
            sizes[quality] = new FileInfo(destination).Length;
        }

        Assert.True(sizes[AudioQuality.High] > sizes[AudioQuality.Medium] && sizes[AudioQuality.Medium] > sizes[AudioQuality.Low]);
    }

    [Fact]
    public async Task NoOptions_MeansHighQuality()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".mp3"), "MP3 can't be written on this machine.");
        var source = _dir.File("nada.wav");
        WriteWav(source, Seconds);

        await CreatePipeline().RunAsync(new ConversionJob(source, ".mp3", _dir.File("hasil.mp3")), null, Ct);

        Assert.Equal(192, (int)Math.Round((await ProfileOfAsync(_dir.File("hasil.mp3"))).Audio.Bitrate / 1000.0));
    }

    [Theory]
    [InlineData(".mp4", VideoSize.P480, 854, 480)]
    [InlineData(".mp4", VideoSize.P720, 1280, 720)]
    [InlineData(".mp4", VideoSize.Original, 1280, 720)]
    [InlineData(".wmv", VideoSize.P480, 854, 480)]
    public async Task VideoSize_KeepsTheShapeOfAWidePicture(string target, VideoSize size, int width, int height)
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".mp4").Contains(target) && target != ".mp4", $"{target} can't be written on this machine.");
        var source = await MakeVideoAsync(".mp4", VideoEncodingQuality.HD720p);
        var destination = _dir.File("hasil" + target);

        await CreatePipeline().RunAsync(new ConversionJob(source, target, destination, new MediaOptions(Video: size)), null, Ct);

        var video = (await ProfileOfAsync(destination)).Video;
        Assert.Equal((width, height), ((int)video.Width, (int)video.Height));
    }

    [Theory]
    [InlineData(VideoSize.P1080)]
    [InlineData(VideoSize.P720)]
    [InlineData(VideoSize.P480)]
    [InlineData(VideoSize.Original)]
    public async Task VideoSize_NeverEnlarges_AndKeepsA4By3Picture(VideoSize size)
    {
        // The source is 640 x 480. The size profiles of Windows would make it 1280 x 720; here it stays as it is.
        var source = await MakeVideoAsync(".mp4", VideoEncodingQuality.Vga);
        Assert.SkipWhen(!new MediaConverter(Staging).GetTargets(".mp4").Contains(".wmv"), "WMV can't be written on this machine.");
        var destination = _dir.File("hasil.wmv");

        await CreatePipeline().RunAsync(new ConversionJob(source, ".wmv", destination, new MediaOptions(Video: size)), null, Ct);

        var video = (await ProfileOfAsync(destination)).Video;
        Assert.Equal((640, 480), ((int)video.Width, (int)video.Height));
    }

    [Fact]
    public async Task VideoSize_ScalesA4By3PictureDownByHeight()
    {
        var source = _dir.File("sumber.mp4");
        await File.WriteAllBytesAsync(source, [], Ct);
        var composition = new MediaComposition();
        composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 20, 160, 60), TimeSpan.FromSeconds(1)));
        var file = await StorageFile.GetFileFromPathAsync(source);
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
        profile.Video.Width = 1440;
        profile.Video.Height = 1080;
        Assert.Equal(TranscodeFailureReason.None, await composition.RenderToFileAsync(file, MediaTrimmingPreference.Fast, profile));
        var destination = _dir.File("hasil.mp4");

        await CreatePipeline().RunAsync(new ConversionJob(source, ".mp4", destination, new MediaOptions(Video: VideoSize.P480)), null, Ct);

        // 1440 x 1080 is 4:3: 480 high is 640 wide.
        var video = (await ProfileOfAsync(destination)).Video;
        Assert.Equal((640, 480), ((int)video.Width, (int)video.Height));
    }

    [Fact]
    public async Task Progress_ReachesTheEnd_AndStaysInsideTheEncodeStage()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".mp3"), "MP3 can't be written on this machine.");
        var source = _dir.File("nada.wav");
        WriteWav(source, Seconds);
        var reports = new List<PipelineProgress>();

        await CreatePipeline().RunAsync(
            new ConversionJob(source, ".mp3", _dir.File("hasil.mp3")),
            new SyncProgress<PipelineProgress>(reports.Add),
            Ct);

        Assert.Contains(reports, r => r.Stage == PipelineStage.Decode && r.Detail == Loc.Get("Progress.OpeningMedia"));
        Assert.Contains(reports, r => r.Stage == PipelineStage.Encode && r.StageFraction == 1);
        for (var i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i].OverallFraction >= reports[i - 1].OverallFraction, $"Progress went back at report {i}.");
        }
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.5)]
    public async Task CutOffWav_IsConvertedWithAWarning(double keep)
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".mp3"), "MP3 can't be written on this machine.");
        var source = _dir.File("terpotong.wav");
        WriteWav(source, Seconds);
        var whole = await File.ReadAllBytesAsync(source, Ct);
        await File.WriteAllBytesAsync(source, whole[..(int)(whole.Length * keep)], Ct);

        var result = await CreatePipeline().RunAsync(new ConversionJob(source, ".mp3", _dir.File("hasil.mp3")), null, Ct);

        var note = Assert.Single(result.Notes!);
        Assert.Equal(NoteSeverity.Warning, note.Severity);
        Assert.Equal(Loc.Get("Note.MediaIncomplete"), note.Message);
    }

    [Theory]
    [InlineData(0.9)]
    [InlineData(0.4)]
    public async Task CutOffFlac_IsConvertedWithAWarning_BecauseTheResultIsShorterThanItSays(double keep)
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".flac"), "FLAC can't be written on this machine.");
        var wav = _dir.File("asli.wav");
        WriteWav(wav, Seconds);
        var source = _dir.File("terpotong.flac");
        await CreatePipeline().RunAsync(new ConversionJob(wav, ".flac", source), null, Ct);
        var whole = await File.ReadAllBytesAsync(source, Ct);
        await File.WriteAllBytesAsync(source, whole[..(int)(whole.Length * keep)], Ct);

        var result = await CreatePipeline().RunAsync(new ConversionJob(source, ".wav", _dir.File("hasil.wav")), null, Ct);

        var note = Assert.Single(result.Notes!);
        Assert.Equal(NoteSeverity.Warning, note.Severity);
    }

    [Theory]
    [InlineData(".wma", 1.0, false)]
    [InlineData(".wma", 0.5, true)]
    [InlineData(".wma", 0.9, true)]
    [InlineData(".wmv", 1.0, false)]
    [InlineData(".wmv", 0.5, true)]
    [InlineData(".wmv", 0.9, true)]
    public async Task AsfWrittenByWindows_IsJudgedByItsOwnFileSize(string extension, double keep, bool warns)
    {
        // The warning rests on the file size in the ASF header: a whole file written by Windows must not trip it.
        var converter = new MediaConverter(Staging);
        var (sourceExtension, via) = extension == ".wma" ? (".wav", ".wma") : (".mp4", ".wmv");
        var made = extension == ".wma" ? _dir.File("asli.wav") : await MakeVideoAsync(".mp4");
        Assert.SkipWhen(!converter.GetTargets(sourceExtension).Contains(via), $"{via} can't be written on this machine.");
        if (extension == ".wma")
        {
            WriteWav(made, Seconds);
        }

        var source = _dir.File("asf" + extension);
        await CreatePipeline().RunAsync(new ConversionJob(made, via, source), null, Ct);
        var whole = await File.ReadAllBytesAsync(source, Ct);
        await File.WriteAllBytesAsync(source, whole[..(int)(whole.Length * keep)], Ct);

        var result = await CreatePipeline().RunAsync(new ConversionJob(source, ".mp3", _dir.File("hasil.mp3")), null, Ct);

        Assert.Equal(warns ? 1 : 0, (result.Notes ?? []).Count);
    }

    [Fact]
    public async Task WholeFlac_HasNoWarning()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".flac"), "FLAC can't be written on this machine.");
        var wav = _dir.File("asli.wav");
        WriteWav(wav, Seconds);
        var flac = _dir.File("utuh.flac");
        await CreatePipeline().RunAsync(new ConversionJob(wav, ".flac", flac), null, Ct);

        var result = await CreatePipeline().RunAsync(new ConversionJob(flac, ".wav", _dir.File("hasil.wav")), null, Ct);

        Assert.Empty(result.Notes ?? []);
    }

    [Fact]
    public async Task CutOffM4a_IsRefusedByWindows_AndNothingIsLeftBehind()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".m4a"), "M4A can't be written on this machine.");
        var wav = _dir.File("asli.wav");
        WriteWav(wav, Seconds);
        var source = _dir.File("terpotong.m4a");
        await CreatePipeline().RunAsync(new ConversionJob(wav, ".m4a", source), null, Ct);
        var whole = await File.ReadAllBytesAsync(source, Ct);
        await File.WriteAllBytesAsync(source, whole[..(whole.Length / 2)], Ct);
        var destination = _dir.File("hasil.wav");

        await Assert.ThrowsAsync<MediaConversionException>(
            () => CreatePipeline().RunAsync(new ConversionJob(source, ".wav", destination), null, Ct));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.condec-tmp"));
        AssertStagingEmpty();
    }

    [Theory]
    [InlineData("kosong.mp3", 0)]
    [InlineData("acak.mp3", 5000)]
    [InlineData("acak.mp4", 5000)]
    public async Task FilesThatAreNoMedia_AreRefused(string name, int length)
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(Path.GetExtension(name)).Contains(".wav"), "WAV isn't offered here.");
        var source = _dir.File(name);
        await File.WriteAllBytesAsync(source, TestData.Bytes(length), Ct);

        await Assert.ThrowsAsync<MediaConversionException>(
            () => CreatePipeline().RunAsync(new ConversionJob(source, ".wav", _dir.File("hasil.wav")), null, Ct));

        Assert.False(File.Exists(_dir.File("hasil.wav")));
    }

    [Fact]
    public async Task Cancelling_StopsTheConversion_AndRemovesTheStagedResult()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".mp3"), "MP3 can't be written on this machine.");
        var source = _dir.File("nada.wav");
        WriteWav(source, Seconds);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var destination = _dir.File("hasil.mp3");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreatePipeline().RunAsync(new ConversionJob(source, ".mp3", destination), null, cancelled.Token));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.condec-tmp"));
        AssertStagingEmpty();
    }

    [Fact]
    public async Task Validator_RejectsAudioOfAnotherFormat()
    {
        var converter = new MediaConverter(Staging);
        Assert.SkipWhen(!converter.GetTargets(".wav").Contains(".mp3"), "MP3 can't be written on this machine.");
        var wav = _dir.File("asli.wav");
        WriteWav(wav, Seconds);
        var mp3 = _dir.File("sebenarnya-mp3.condec-tmp");
        await CreatePipeline().RunAsync(new ConversionJob(wav, ".mp3", mp3), null, Ct);

        // The name says nothing: the file is read as what it is.
        await new MediaOutputValidator().ValidateAsync(mp3, ".mp3", Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaOutputValidator().ValidateAsync(mp3, ".wav", Ct));
        await Assert.ThrowsAsync<InvalidDataException>(() => new MediaOutputValidator().ValidateAsync(mp3, ".mp4", Ct));
    }

    [Fact]
    public async Task Validator_RejectsBytesThatAreNoMedia()
    {
        var path = _dir.File("rusak.mp3");
        await File.WriteAllBytesAsync(path, TestData.Bytes(500), Ct);

        await Assert.ThrowsAnyAsync<Exception>(() => new MediaOutputValidator().ValidateAsync(path, ".mp3", Ct));
    }

    [Fact]
    public void Validator_OnlyAcceptsTheFormatsItWrites()
    {
        var validator = new MediaOutputValidator();

        Assert.True(validator.CanValidate(".wav"));
        Assert.False(validator.CanValidate(".png"));
        Assert.False(validator.CanValidate(".avi"));
    }

    private ConversionPipeline CreatePipeline() => new(
        new ConverterRegistry([new MediaConverter(Staging)], [new MediaOutputValidator()]),
        new TempFileJournal(_dir.File("journal")));

    private void AssertStagingEmpty()
    {
        if (Directory.Exists(Staging))
        {
            Assert.Empty(Directory.GetFileSystemEntries(Staging));
        }
    }

    private static async Task AssertLengthAsync(string path, string extension, double seconds)
    {
        var target = MediaFormats.FindTarget(extension)!;
        var length = await MediaFormats.ReadDurationAsync(path, target, TestContext.Current.CancellationToken);
        Assert.InRange(length.TotalSeconds, seconds - 0.3, seconds + 0.3);
    }

    /// <summary>Sample rate and channel count from the "fmt " chunk, wherever it is.</summary>
    private static (int SampleRate, int Channels) FormatOf(byte[] wav)
    {
        var offset = 12;
        while (offset + 8 <= wav.Length)
        {
            var size = BitConverter.ToInt32(wav, offset + 4);
            if (wav.AsSpan(offset, 4).SequenceEqual("fmt "u8))
            {
                return (BitConverter.ToInt32(wav, offset + 12), BitConverter.ToInt16(wav, offset + 10));
            }

            offset += 8 + size + (size & 1);
        }

        throw new InvalidDataException("No fmt chunk.");
    }

    /// <summary>The samples, without the header around them.</summary>
    private static byte[] PcmOf(byte[] wav)
    {
        var offset = 12;
        while (offset + 8 <= wav.Length)
        {
            var size = BitConverter.ToInt32(wav, offset + 4);
            if (wav.AsSpan(offset, 4).SequenceEqual("data"u8))
            {
                return wav[(offset + 8)..Math.Min(wav.Length, offset + 8 + size)];
            }

            offset += 8 + size + (size & 1);
        }

        throw new InvalidDataException("No data chunk.");
    }

    private static async Task<MediaEncodingProfile> ProfileOfAsync(string path)
    {
        using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        return await MediaEncodingProfile.CreateFromStreamAsync(stream);
    }

    private async Task<string> MakeVideoAsync(string extension, VideoEncodingQuality quality = VideoEncodingQuality.Vga)
    {
        var path = _dir.File("video" + extension);
        await File.WriteAllBytesAsync(path, [], Ct);
        var composition = new MediaComposition();
        composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 200, 30, 30), TimeSpan.FromSeconds(2)));
        composition.Clips.Add(MediaClip.CreateFromColor(Windows.UI.Color.FromArgb(255, 30, 30, 200), TimeSpan.FromSeconds(1)));
        var file = await StorageFile.GetFileFromPathAsync(path);
        var status = await composition.RenderToFileAsync(file, MediaTrimmingPreference.Fast, MediaEncodingProfile.CreateMp4(quality));
        Assert.Equal(TranscodeFailureReason.None, status);
        return path;
    }

    /// <summary>16-bit PCM: a 440 Hz tone with a quieter overtone.</summary>
    private static void WriteWav(string path, int seconds, int sampleRate = 44100, int channels = 2)
    {
        var samples = sampleRate * seconds;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples * channels * 2));
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * channels * 2);
        for (var i = 0; i < samples; i++)
        {
            var sample = (short)((12000 * Math.Sin(2 * Math.PI * 440 * i / sampleRate)) + (3000 * Math.Sin(2 * Math.PI * 1320 * i / sampleRate)));
            for (var channel = 0; channel < channels; channel++)
            {
                writer.Write(sample);
            }
        }
    }
}

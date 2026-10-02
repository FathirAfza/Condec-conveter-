// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Condec.Core.Media;

/// <summary>The audio and video formats Condec reads and writes through <c>Windows.Media.Transcoding</c>.</summary>
internal static class MediaFormats
{
    /// <param name="IsVideo">The result has a video track (MP4, WMV); otherwise it is audio only.</param>
    /// <param name="ContentType">Tells Windows what the stream is when a file is read without its name.</param>
    /// <param name="AudioCodec">The <see cref="CodecSubtypes"/> value an encoder must exist for; null when none is needed (WAV).</param>
    /// <param name="VideoCodec">The same for the video track.</param>
    /// <param name="AudioSubtype">What <see cref="MediaEncodingProfile.Audio"/> says about a correct result.</param>
    /// <param name="VideoSubtype">What <see cref="MediaEncodingProfile.Video"/> says about a correct result.</param>
    internal sealed record Target(
        string Extension,
        bool IsVideo,
        string ContentType,
        string? AudioCodec,
        string? VideoCodec,
        string AudioSubtype,
        string? VideoSubtype);

    // Video first, so a video offers its own format before the audio it could be reduced to.
    private static readonly Target[] AllTargets =
    [
        new(".mp4", true, "video/mp4", CodecSubtypes.AudioFormatAac, CodecSubtypes.VideoFormatH264, MediaEncodingSubtypes.Aac, MediaEncodingSubtypes.H264),
        new(".wmv", true, "video/x-ms-wmv", CodecSubtypes.AudioFormatWMAudioV9, CodecSubtypes.VideoFormatWvc1, MediaEncodingSubtypes.Wma9, MediaEncodingSubtypes.Wvc1),
        new(".mp3", false, "audio/mpeg", CodecSubtypes.AudioFormatMP3, null, MediaEncodingSubtypes.Mp3, null),
        new(".m4a", false, "audio/mp4", CodecSubtypes.AudioFormatAac, null, MediaEncodingSubtypes.Aac, null),
        new(".wav", false, "audio/wav", null, null, MediaEncodingSubtypes.Pcm, null),
        new(".wma", false, "audio/x-ms-wma", CodecSubtypes.AudioFormatWMAudioV9, null, MediaEncodingSubtypes.Wma9, null),
        new(".flac", false, "audio/flac", CodecSubtypes.AudioFormatFlac, null, MediaEncodingSubtypes.Flac, null),
    ];

    /// <summary>
    /// Source extension to whether it carries video. Windows recognizes a file by its content, not its name, so this list
    /// is Condec's own: only types that were tried on real files are offered.
    /// </summary>
    private static readonly Dictionary<string, bool> Sources = new(StringComparer.Ordinal)
    {
        [".mp3"] = false,
        [".m4a"] = false,
        [".wav"] = false,
        [".wma"] = false,
        [".flac"] = false,
        [".mp4"] = true,
        [".m4v"] = true,
        [".mov"] = true,
        [".wmv"] = true,
        [".avi"] = true,
    };

    private static readonly Lazy<IReadOnlyList<Target>> AvailableTargets = new(() => Task.Run(FindAvailableTargetsAsync).GetAwaiter().GetResult());

    /// <summary>The targets this machine has encoders for. Windows N editions without the Media Feature Pack have none.</summary>
    public static IReadOnlyList<Target> Targets => AvailableTargets.Value;

    public static Target? FindTarget(string extension) => Targets.FirstOrDefault(target => target.Extension == extension);

    /// <param name="hasVideo">Whether this kind of file can hold video, so that audio can be taken out of it and video can be written.</param>
    public static bool TryGetSource(string extension, out bool hasVideo) => Sources.TryGetValue(extension, out hasVideo);

    /// <summary>
    /// Profiles to try, in order. The profiles Windows provides fix the audio at 48 kHz stereo (and 24 bit for FLAC), which
    /// would change a lossless result, so for WAV and FLAC the first profile keeps the source's sample rate, channels and bit
    /// depth. Windows refuses some combinations, so the standard profile always follows.
    /// </summary>
    public static IEnumerable<MediaEncodingProfile> CreateProfiles(Target target, MediaEncodingProfile? source, MediaOptions options)
    {
        if (source?.Audio is { SampleRate: > 0, ChannelCount: > 0 } sourceAudio && target.Extension is ".wav" or ".flac")
        {
            yield return CreateMatchedProfile(target, sourceAudio);
        }

        var audioQuality = options.Audio switch
        {
            AudioQuality.Medium => AudioEncodingQuality.Medium,
            AudioQuality.Low => AudioEncodingQuality.Low,
            _ => AudioEncodingQuality.High,
        };

        yield return target.Extension switch
        {
            ".mp4" or ".wmv" => CreateVideoProfile(target, source?.Video, options),
            ".mp3" => MediaEncodingProfile.CreateMp3(audioQuality),
            ".m4a" => MediaEncodingProfile.CreateM4a(audioQuality),
            ".wav" => MediaEncodingProfile.CreateWav(AudioEncodingQuality.High),
            ".wma" => MediaEncodingProfile.CreateWma(audioQuality),
            ".flac" => MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }

    /// <summary>
    /// The source's own size unless a smaller one was chosen. The size profiles Windows provides have a fixed shape (a 4:3
    /// video becomes 1280 x 720), so the chosen height is applied to the source's shape instead, and a video that is
    /// already that small is left alone.
    /// </summary>
    private static MediaEncodingProfile CreateVideoProfile(Target target, VideoEncodingProperties? source, MediaOptions options)
    {
        MediaEncodingProfile Create(VideoEncodingQuality quality) => target.Extension == ".mp4"
            ? MediaEncodingProfile.CreateMp4(quality)
            : MediaEncodingProfile.CreateWmv(quality);

        if (options.MaxHeight is not { } height || source is not { Width: > 0, Height: > 0 } || source.Height <= height)
        {
            return Create(VideoEncodingQuality.Auto);
        }

        // The profile for the nearest standard size supplies the bitrate (18, 9 and 4.5 Mbps).
        var profile = Create(height switch
        {
            1080 => VideoEncodingQuality.HD1080p,
            720 => VideoEncodingQuality.HD720p,
            _ => VideoEncodingQuality.Wvga,
        });

        // Video wants even dimensions.
        profile.Video.Width = (uint)(Math.Round(source.Width * (double)height / source.Height / 2) * 2);
        profile.Video.Height = (uint)height;
        return profile;
    }

    private static MediaEncodingProfile CreateMatchedProfile(Target target, AudioEncodingProperties source)
    {
        var profile = target.Extension == ".wav"
            ? MediaEncodingProfile.CreateWav(AudioEncodingQuality.High)
            : MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High);

        var bits = source.BitsPerSample > 16 ? 24u : 16u;
        profile.Audio.SampleRate = source.SampleRate;
        profile.Audio.ChannelCount = source.ChannelCount;
        profile.Audio.BitsPerSample = bits;

        // A WAV is refused unless its bitrate agrees with the other three.
        profile.Audio.Bitrate = source.SampleRate * source.ChannelCount * bits;
        return profile;
    }

    /// <summary>
    /// The length of a finished file. FLAC states it in its own header, which is right where Windows' reading is not
    /// (2.0 s for a 3.0 s file); for every other format Windows is asked, by stream, because the name of the file is
    /// still the temporary one and Windows' shell reads nothing from it.
    /// </summary>
    public static async Task<TimeSpan> ReadDurationAsync(string path, Target target, CancellationToken ct)
    {
        if (target.Extension == ".flac" && Formats.MediaStructure.GetDeclaredDuration(path) is { } declared)
        {
            return declared;
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        using var stream = file.AsRandomAccessStream();
        using var source = MediaSource.CreateFromStream(stream, target.ContentType);
        await source.OpenAsync().AsTask(ct).ConfigureAwait(false);
        return source.Duration ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Windows lists encoders that don't work (the HEIF one without its extension), so the question is asked per codec:
    /// a format is offered when an encoder exists for each track it needs.
    /// </summary>
    private static async Task<IReadOnlyList<Target>> FindAvailableTargetsAsync()
    {
        var available = new List<Target>();
        var query = new CodecQuery();
        foreach (var target in AllTargets)
        {
            try
            {
                if (await HasEncoderAsync(query, CodecKind.Audio, target.AudioCodec).ConfigureAwait(false)
                    && await HasEncoderAsync(query, CodecKind.Video, target.VideoCodec).ConfigureAwait(false))
                {
                    available.Add(target);
                }
            }
            catch (Exception)
            {
                // A failed question means the codec can't be used here; the format simply isn't offered.
            }
        }

        return available;
    }

    private static async Task<bool> HasEncoderAsync(CodecQuery query, CodecKind kind, string? subtype)
    {
        if (subtype is null)
        {
            return true;
        }

        var encoders = await query.FindAllAsync(kind, CodecCategory.Encoder, subtype);
        return encoders.Count > 0;
    }
}

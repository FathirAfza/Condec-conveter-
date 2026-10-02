// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.Formats;
using Windows.Media.MediaProperties;

namespace Condec.Core.Media;

/// <summary>
/// Reopens a converted audio or video file through Windows and checks that its tracks are the ones the format should
/// have and that it is not empty. This reads the file's format and length, not every sample.
/// </summary>
public sealed class MediaOutputValidator : IOutputValidator
{
    public bool CanValidate(string targetExtension) =>
        MediaFormats.FindTarget(FileExtension.Normalize(targetExtension)) is not null;

    public async Task ValidateAsync(string path, string targetExtension, CancellationToken ct)
    {
        var expected = MediaFormats.FindTarget(FileExtension.Normalize(targetExtension))
            ?? throw new NotSupportedException($"'{targetExtension}' is not an audio or video target.");

        MediaEncodingProfile profile;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous))
        using (var stream = file.AsRandomAccessStream())
        {
            profile = await MediaEncodingProfile.CreateFromStreamAsync(stream).AsTask(ct).ConfigureAwait(false);
        }

        if (profile?.Audio is not { } audio || !SameSubtype(audio.Subtype, expected.AudioSubtype))
        {
            throw new InvalidDataException($"Expected {expected.AudioSubtype} audio, but Windows reads {profile?.Audio?.Subtype ?? "no audio"}.");
        }

        if (expected.IsVideo)
        {
            if (profile.Video is not { } video || !SameSubtype(video.Subtype, expected.VideoSubtype!))
            {
                throw new InvalidDataException($"Expected {expected.VideoSubtype} video, but Windows reads {profile.Video?.Subtype ?? "no video"}.");
            }
        }
        else if (profile.Video is not null)
        {
            throw new InvalidDataException("The audio file has a video track.");
        }

        if (await MediaFormats.ReadDurationAsync(path, expected, ct).ConfigureAwait(false) <= TimeSpan.Zero)
        {
            throw new InvalidDataException("The file has no sound or picture to play.");
        }
    }

    private static bool SameSubtype(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
}

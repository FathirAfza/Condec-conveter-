// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;

namespace Condec.Core.Media;

/// <summary>
/// Audio and video through <c>Windows.Media.Transcoding.MediaTranscoder</c>, with the profiles Windows provides.
/// Everything stays on this computer. A video can also be reduced to its audio.
/// </summary>
public sealed class MediaConverter : IConverter
{
    /// <summary>Share of the Encode stage that is the transcode; the rest is copying the result to the pipeline.</summary>
    private const double TranscodeShare = 0.9;

    private readonly string _stagingRoot;

    public MediaConverter()
        : this(Path.Combine(Path.GetTempPath(), "Condec"))
    {
    }

    /// <param name="stagingRoot">Where each conversion gets a folder for its result until it is copied to the pipeline.</param>
    internal MediaConverter(string stagingRoot)
    {
        _stagingRoot = stagingRoot;
    }

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        MediaFormats.TryGetSource(FileExtension.Normalize(sourceExtension), out var hasVideo)
            ? [.. MediaFormats.Targets.Where(target => hasVideo || !target.IsVideo).Select(target => target.Extension)]
            : [];

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var target = MediaFormats.FindTarget(FileExtension.Normalize(request.TargetExtension))
            ?? throw new NotSupportedException($"'{request.TargetExtension}' is not an audio or video target on this machine.");

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0, Loc.Get("Progress.OpeningMedia")));

        // A transcoder needs to seek in its output (MP4 and FLAC rewrite their headers), so the result is made in a
        // folder of its own and copied to the pipeline's forward-only output at the end.
        var staging = Path.Combine(_stagingRoot, "media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var staged = Path.Combine(staging, "result" + target.Extension);
            var incomplete = MediaStructure.IsComplete(request.SourcePath) == false;
            var sourceAudio = await ReadSourceAudioAsync(request.SourcePath, ct).ConfigureAwait(false);

            await TranscodeAsync(request.SourcePath, staged, MediaFormats.CreateProfiles(target, sourceAudio), progress, ct).ConfigureAwait(false);

            // A FLAC states how long it should be. If the result is shorter, the file was cut off.
            if (!incomplete && MediaStructure.GetDeclaredDuration(request.SourcePath) is { } declared)
            {
                var actual = await MediaFormats.ReadDurationAsync(staged, target, ct).ConfigureAwait(false);
                incomplete = declared - actual > TimeSpan.FromSeconds(Math.Max(0.25, declared.TotalSeconds * 0.015));
            }

            if (incomplete)
            {
                request.Notes.Add(NoteSeverity.Warning, Loc.Get("Note.MediaIncomplete"));
            }

            await CopyAsync(staged, request.Output, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>What the source's sound is like, so a lossless result can keep it. Null when Windows can't say.</summary>
    private static async Task<AudioEncodingProperties?> ReadSourceAudioAsync(string path, CancellationToken ct)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            using var stream = file.AsRandomAccessStream();
            var profile = await MediaEncodingProfile.CreateFromStreamAsync(stream).AsTask(ct).ConfigureAwait(false);
            return profile?.Audio;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // The transcode below says what is wrong with the file; this is only a hint for the result's format.
            return null;
        }
    }

    private static async Task TranscodeAsync(
        string sourcePath,
        string stagedPath,
        IEnumerable<MediaEncodingProfile> profiles,
        IProgress<ConversionProgress> progress,
        CancellationToken ct)
    {
        var reason = TranscodeFailureReason.Unknown;
        Exception? failure = null;
        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
                using var destination = new FileStream(stagedPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous);
                using var sourceStream = source.AsRandomAccessStream();
                using var destinationStream = destination.AsRandomAccessStream();

                var prepared = await new MediaTranscoder()
                    .PrepareStreamTranscodeAsync(sourceStream, destinationStream, profile)
                    .AsTask(ct).ConfigureAwait(false);
                if (!prepared.CanTranscode)
                {
                    reason = prepared.FailureReason;
                    continue;
                }

                progress.Report(new ConversionProgress(ConversionStage.Decode, 1));
                await prepared.TranscodeAsync()
                    .AsTask(ct, new InlineProgress<double>(percent =>
                        progress.Report(new ConversionProgress(ConversionStage.Encode, Math.Clamp(percent / 100, 0, 1) * TranscodeShare))))
                    .ConfigureAwait(false);
                return;
            }
            catch (COMException ex)
            {
                failure = ex;
            }
        }

        throw new MediaConversionException($"Windows could not convert the file ({reason}).", failure);
    }

    private static async Task CopyAsync(string stagedPath, Stream output, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        await using var staged = new FileStream(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var length = Math.Max(1, staged.Length);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await staged.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            copied += read;
            progress.Report(new ConversionProgress(ConversionStage.Encode, TranscodeShare + ((1 - TranscodeShare) * copied / length)));
        }

        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in the temp folder; Windows clears it.
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

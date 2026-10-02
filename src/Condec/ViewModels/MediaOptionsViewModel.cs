// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CommunityToolkit.Mvvm.ComponentModel;
using Condec.Core.Localization;
using Condec.Core.Media;

namespace Condec.ViewModels;

/// <summary>One entry in a choice list. ToString is what the list shows and what screen readers announce.</summary>
public sealed record MediaChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Audio quality and video size for MP3, M4A, WMA, MP4 and WMV (DESIGN §6.1.2). The choices last until the app closes.</summary>
public sealed partial class MediaOptionsViewModel : ObservableObject
{
    public MediaOptionsViewModel()
    {
        AudioChoices =
        [
            new(AudioQuality.High, Loc.Get("MediaQuality.High")),
            new(AudioQuality.Medium, Loc.Get("MediaQuality.Medium")),
            new(AudioQuality.Low, Loc.Get("MediaQuality.Low")),
        ];
        VideoChoices =
        [
            new(VideoSize.Original, Loc.Get("MediaSize.Original")),
            new(VideoSize.P1080, Loc.Get("MediaSize.P1080")),
            new(VideoSize.P720, Loc.Get("MediaSize.P720")),
            new(VideoSize.P480, Loc.Get("MediaSize.P480")),
        ];
        SelectedAudio = AudioChoices[0];
        SelectedVideo = VideoChoices[0];
    }

    public IReadOnlyList<MediaChoice<AudioQuality>> AudioChoices { get; }

    public IReadOnlyList<MediaChoice<VideoSize>> VideoChoices { get; }

    [ObservableProperty]
    public partial MediaChoice<AudioQuality>? SelectedAudio { get; set; }

    [ObservableProperty]
    public partial MediaChoice<VideoSize>? SelectedVideo { get; set; }

    public MediaOptions BuildOptions() => new(
        SelectedAudio?.Value ?? AudioQuality.High,
        SelectedVideo?.Value ?? VideoSize.Original);
}

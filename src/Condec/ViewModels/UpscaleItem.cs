// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CommunityToolkit.Mvvm.ComponentModel;
using Condec.Core.Formats;
using Condec.Core.Localization;

namespace Condec.ViewModels;

/// <summary>Why a picture in the Upscale queue can't be upscaled as it is chosen.</summary>
public enum UpscaleBlock
{
    None,

    /// <summary>This device can't make it even 1.5× larger (DESIGN §6.2 "Upscale tidak tersedia").</summary>
    TooBig,

    /// <summary>The chosen size needs more RAM than is installed, or more than the memory limit in Settings.</summary>
    Memory,
}

/// <summary>
/// One picture in the Upscale Image queue, with its own size of the result (DESIGN §6.2). The format of the result is the
/// queue's, not the picture's.
/// </summary>
public sealed partial class UpscaleItem : ObservableObject
{
    public UpscaleItem(SourceFile file, int width, int height)
    {
        File = file;
        Width = width;
        Height = height;
    }

    public SourceFile File { get; }

    public string Name => File.Name;

    public int Width { get; }

    public int Height { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    public partial int Number { get; set; }

    /// <summary>Shown at least once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Label))]
    public partial bool Viewed { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Label))]
    public partial UpscaleBlock Block { get; set; }

    /// <summary>The chosen scale; 1 while the picture can't be upscaled at all.</summary>
    public double Scale { get; private set; } = 1;

    public int OutputWidth { get; private set; }

    public int OutputHeight { get; private set; }

    /// <summary>Remembers the size chosen for this picture, so it is the same when the picture is shown again.</summary>
    public void SetSize(double scale, int outputWidth, int outputHeight)
    {
        if (Scale == scale && OutputWidth == outputWidth && OutputHeight == outputHeight)
        {
            return;
        }

        (Scale, OutputWidth, OutputHeight) = (scale, outputWidth, outputHeight);
        OnPropertyChanged(nameof(Label));
    }

    public string StatusText => Loc.Get(Block != UpscaleBlock.None ? "Upscale.Queue.Status.Blocked"
        : Viewed ? "Queue.Status.Viewed"
        : "Queue.Status.NotViewed");

    /// <summary>"2. pantai.jpg · 2× · Belum ditinjau": the entry in the queue list, read aloud as it is.</summary>
    public string Label => Block == UpscaleBlock.TooBig
        ? Loc.Format("Queue.ItemLabel", Number, Name, StatusText)
        : Loc.Format("Upscale.Queue.ItemLabel", Number, Name, ScaleText.Format(Scale), StatusText);

    public override string ToString() => Label;
}

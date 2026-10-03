// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CommunityToolkit.Mvvm.ComponentModel;
using Condec.Core.Architecture;
using Condec.Core.Localization;
using Condec.Core.Upscale;

namespace Condec.ViewModels;

public enum ToCadItemStatus
{
    /// <summary>Not read yet; the queue reads its items one after another.</summary>
    Waiting,
    Reading,
    Ready,

    /// <summary>It could not be read: the queue says why, and it must be removed before converting.</summary>
    Failed,
}

/// <summary>
/// One picture, DXF, or PDF page in the Architecture queue, with its own review: what was found, which kinds become CAD,
/// its upscale and its calibration (DESIGN §6.3.4, owner decision 2026-10-03 "tinjau satu per satu").
/// </summary>
public sealed partial class ToCadItem : ObservableObject
{
    public ToCadItem(SourceFile file, int? page)
    {
        File = file;
        Page = page;
    }

    public SourceFile File { get; }

    /// <summary>The PDF page, 1 for the first; null for a picture or a DXF.</summary>
    public int? Page { get; }

    /// <summary>"denah.png", or "gambar.pdf, halaman 2" for one page of a PDF that has more than one.</summary>
    public string Name => Page is { } page && File.PageCount > 1 ? Loc.Format("Batch.PageName", File.Name, page) : File.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    public partial int Number { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Label), nameof(IsSettled))]
    public partial ToCadItemStatus Status { get; set; }

    /// <summary>Shown at least once after it was read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Label))]
    public partial bool Viewed { get; set; }

    /// <summary>Read or failed: nothing more happens to it until the user acts.</summary>
    public bool IsSettled => Status is ToCadItemStatus.Ready or ToCadItemStatus.Failed;

    /// <summary>Why it could not be read.</summary>
    public string? FailMessage { get; set; }

    /// <summary>Progress of reading it, 0 to 100.</summary>
    public double ReadPercent { get; set; }

    public CadSourceKind Kind { get; set; }

    public DrawingAnalysis? Analysis { get; set; }

    /// <summary>The picture the analysis worked on, or the drawn page of a vector PDF or DXF.</summary>
    public PreviewImage? Preview { get; set; }

    public UpscaleAdvice? Advice { get; set; }

    public int OriginalWidth { get; set; }

    public int OriginalHeight { get; set; }

    public int PictureWidth { get; set; }

    public int PictureHeight { get; set; }

    public double OriginalDpiX { get; set; }

    public double OriginalDpiY { get; set; }

    public int UpscaleFactor { get; set; } = 1;

    /// <summary>Millimeters per pixel of the picture as it was chosen, once calibrated.</summary>
    public double? Calibration { get; set; }

    /// <summary>The kinds that become CAD; filled with the defaults when the picture has been read.</summary>
    public HashSet<DrawingObjectKind> Included { get; } = [];

    public ToCadBanner Banner { get; set; }

    /// <summary>A read picture in which nothing would become CAD: nothing was found, or nothing is ticked.</summary>
    public bool HasNothingToConvert =>
        Status == ToCadItemStatus.Ready
        && Kind == CadSourceKind.Raster
        && (Analysis is not { ObjectCount: > 0 } || Included.Count == 0);

    public string StatusText => Loc.Get(Status switch
    {
        ToCadItemStatus.Waiting => "Queue.Status.Waiting",
        ToCadItemStatus.Reading => "Queue.Status.Reading",
        ToCadItemStatus.Failed => "Queue.Status.Failed",
        _ when HasNothingToConvert => "Queue.Status.Nothing",
        _ when Viewed => "Queue.Status.Viewed",
        _ => "Queue.Status.NotViewed",
    });

    /// <summary>"2. gambar.pdf, halaman 2 · Belum ditinjau": the entry in the queue list, read aloud as it is.</summary>
    public string Label => Loc.Format("Queue.ItemLabel", Number, Name, StatusText);

    /// <summary>The ticked kinds changed: the status may say so.</summary>
    public void RaiseStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Label));
    }

    public override string ToString() => Label;
}

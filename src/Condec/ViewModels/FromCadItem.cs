// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CommunityToolkit.Mvvm.ComponentModel;
using Condec.Core.Architecture;
using Condec.Core.Localization;

namespace Condec.ViewModels;

/// <summary>
/// One DWG or DXF file in the "DWG, DXF → Gambar, PDF" queue, with its own layers and preview (DESIGN §6.3.4). The options of
/// the result (format, paper, resolution, background) are the queue's, not the item's.
/// </summary>
public sealed partial class FromCadItem : ObservableObject
{
    public FromCadItem(SourceFile file)
    {
        File = file;
    }

    public SourceFile File { get; }

    public string Name => File.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    public partial int Number { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Label), nameof(IsSettled))]
    public partial QueueItemStatus Status { get; set; }

    /// <summary>Shown at least once after it was read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Label))]
    public partial bool Viewed { get; set; }

    /// <summary>Read or failed: nothing more happens to it until the user acts.</summary>
    public bool IsSettled => Status is QueueItemStatus.Ready or QueueItemStatus.Failed;

    /// <summary>Why it could not be read.</summary>
    public string? FailMessage { get; set; }

    public CadScene? Scene { get; set; }

    /// <summary>The layers that have something to draw, in the file's order, and whether the file has each one on.</summary>
    public IReadOnlyList<(string Name, bool IsOn)> LayerNames { get; set; } = [];

    /// <summary>The layers left out of the result.</summary>
    public HashSet<string> Hidden { get; } = [];

    public PreviewImage? Preview { get; set; }

    /// <summary>The paper <see cref="Preview"/> was drawn on; a preview of another paper is drawn again when shown.</summary>
    public PaperSize? PreviewPaper { get; set; }

    /// <summary>"Pratinjau tidak bisa digambar…", when drawing it failed.</summary>
    public string? PreviewMessage { get; set; }

    /// <summary>A read drawing whose layers are all hidden: nothing would be drawn.</summary>
    public bool HasNothingShown => Status == QueueItemStatus.Ready && LayerNames.All(l => Hidden.Contains(l.Name));

    public string StatusText => Loc.Get(Status switch
    {
        QueueItemStatus.Waiting => "Queue.Status.Waiting",
        QueueItemStatus.Reading => "Queue.Status.Reading",
        QueueItemStatus.Failed => "Queue.Status.Failed",
        _ when HasNothingShown => "Queue.Status.Nothing",
        _ when Viewed => "Queue.Status.Viewed",
        _ => "Queue.Status.NotViewed",
    });

    /// <summary>"2. denah.dwg · Belum ditinjau": the entry in the queue list, read aloud as it is.</summary>
    public string Label => Loc.Format("Queue.ItemLabel", Number, Name, StatusText);

    /// <summary>The shown layers changed: the status may say so.</summary>
    public void RaiseStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Label));
    }

    public override string ToString() => Label;
}

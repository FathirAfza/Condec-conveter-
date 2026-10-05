// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.History;
using Condec.Core.Localization;

namespace Condec.ViewModels;

/// <summary>Segoe Fluent Icons glyphs for a file, by extension.</summary>
internal static class FileGlyphs
{
    public const string Photo = "";
    public const string Document = "";

    private static readonly HashSet<string> ImageExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".heif", ".webp"];

    /// <summary>Segoe Fluent Icons "Audio" and "Video".</summary>
    public const string Audio = "\uE8D6";
    public const string Video = "\uE714";

    private static readonly HashSet<string> AudioExtensions = [".mp3", ".m4a", ".wav", ".wma", ".flac"];

    private static readonly HashSet<string> VideoExtensions = [".mp4", ".m4v", ".mov", ".wmv", ".avi"];

    public static bool IsImage(string extension) => ImageExtensions.Contains(extension);

    public static string For(string extension) =>
        IsImage(extension) ? Photo
        : AudioExtensions.Contains(extension) ? Audio
        : VideoExtensions.Contains(extension) ? Video
        : Document;
}

/// <summary>The file chosen for conversion.</summary>
/// <param name="PageCount">For a PDF, its number of pages.</param>
public sealed record SourceFile(string Path, string Extension, long Size, int? PageCount = null)
{
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>"PNG image · 2.4 MB", or "PDF document · 3 pages · 1.1 MB".</summary>
    public string Description => PageCount switch
    {
        1 => Loc.Format("Source.WithOnePage", FormatCatalog.GetKindName(Extension), DisplayFormat.FormatFileSize(Size)),
        { } pages => Loc.Format("Source.WithPages", FormatCatalog.GetKindName(Extension), pages, DisplayFormat.FormatFileSize(Size)),
        _ => Loc.Format("Source.Plain", FormatCatalog.GetKindName(Extension), DisplayFormat.FormatFileSize(Size)),
    };

    public string Glyph => FileGlyphs.For(Extension);
}

/// <summary>One entry in the format list. ToString is what screen readers announce for the item.</summary>
public sealed record FormatOption(TargetOption Option)
{
    public string DisplayName => Option.DisplayName;

    public string Extension => Option.Extension;

    public bool IsEnabled => Option.IsEnabled;

    /// <summary>The extension, or "Unavailable" so a disabled format isn't told apart by color alone.</summary>
    public string Caption => Option.IsEnabled ? Extension : Loc.Get("Format.Unavailable");

    public override string ToString() => Option.IsEnabled ? $"{DisplayName} ({Extension})" : Loc.Format("Format.UnavailableSpoken", DisplayName);
}

public enum StepState
{
    Waiting,
    Active,
    Done,
}

/// <summary>One row of the four-stage list in the progress view.</summary>
public sealed partial class ConversionStepViewModel : ObservableObject
{
    public ConversionStepViewModel(string label)
    {
        Label = label;
        Detail = Loc.Get("Step.Waiting");
    }

    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting), nameof(IsActive), nameof(IsDone), nameof(AutomationName))]
    public partial StepState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial string Detail { get; set; }

    public bool IsWaiting => State == StepState.Waiting;

    public bool IsActive => State == StepState.Active;

    public bool IsDone => State == StepState.Done;

    public string AutomationName => $"{Label}, {Detail}";
}

/// <summary>One row of the history list.</summary>
public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(HistoryEntry entry, DateTime nowLocal, Action<HistoryItemViewModel> showInFolder)
    {
        Entry = entry;
        var when = DisplayFormat.FormatTimestamp(entry.CompletedAt.LocalDateTime, nowLocal);
        Summary = entry.UpscaleScale is { } scale
            ? Loc.Format("History.UpscaleSummary", FileExtension.ToCode(entry.SourceExtension), FileExtension.ToCode(entry.TargetExtension), ScaleText.Format(scale), when)
            : entry.Compressed
            ? Loc.Format("History.CompressSummary", FileExtension.ToCode(entry.SourceExtension), FileExtension.ToCode(entry.TargetExtension), when)
            : string.Concat(
                FileExtension.ToCode(entry.SourceExtension),
                " → ",
                FileExtension.ToCode(entry.TargetExtension),
                " · ",
                when);
        ShowInFolderCommand = new RelayCommand(() => showInFolder(this));
    }

    public HistoryEntry Entry { get; }

    public string Name => Entry.SourceFileName;

    /// <summary>
    /// "PNG → JPG · Yesterday, 7:40 PM", "PNG → PNG · Upscale 2× · Yesterday, 7:40 PM" for an upscale, or
    /// "HEIC → JPG · Compress · Yesterday, 7:40 PM" for Compress Image.
    /// </summary>
    public string Summary { get; }

    public bool IsVerified => Entry.Verification == VerificationStatus.Verified;

    public string Glyph => FileGlyphs.For(Entry.SourceExtension);

    public string AutomationName => Loc.Format(IsVerified ? "History.VerifiedSpoken" : "History.PlainSpoken", Name, Summary);

    /// <summary>Screen readers need the file name: every row has the same folder button.</summary>
    public string ShowInFolderName => Loc.Format("History.ShowInFolderFor", Name);

    public IRelayCommand ShowInFolderCommand { get; }
}

/// <summary>One thing the converter wants the user to know about a saved result, shown as an InfoBar.</summary>
public sealed record ResultNoteItem(string Message, Microsoft.UI.Xaml.Controls.InfoBarSeverity Severity);

/// <summary>One file of a multi-file selection in Convert File, numbered in the order its results are made.</summary>
public sealed partial class SourceItem : ObservableObject
{
    public SourceItem(SourceFile file, Action<SourceItem> remove)
    {
        File = file;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public SourceFile File { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberText), nameof(AutomationName))]
    public partial int Number { get; set; }

    public string NumberText => Number.ToString(System.Globalization.CultureInfo.CurrentCulture) + ".";

    public string Name => File.Name;

    public string Description => File.Description;

    public string Glyph => File.Glyph;

    /// <summary>What this file becomes: "→ foto.jpg", "→ 3 files", or that none of the chosen pages are in it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial string Output { get; set; } = string.Empty;

    public string AutomationName => Output.Length == 0
        ? Loc.Format("Sources.ItemSpokenNoOutput", Number, Name, Description)
        : Loc.Format("Sources.ItemSpoken", Number, Name, Description, Output);

    public string RemoveName => Loc.Format("Sources.RemoveSpoken", Name);

    public IRelayCommand RemoveCommand { get; }
}

public enum BatchRowState
{
    Waiting,
    Done,
    Failed,
    Cancelled,
}

/// <summary>One row of a batch's result list: result n of file n, saved or not, and why.</summary>
public sealed partial class BatchResultItem : ObservableObject
{
    private readonly Action<string> _showInFolder;

    public BatchResultItem(int number, string sourceName, string plannedName, Action<string> showInFolder)
    {
        Number = number;
        SourceName = sourceName;
        OutputName = plannedName;
        _showInFolder = showInFolder;
        ShowInFolderCommand = new RelayCommand(() =>
        {
            if (OutputPath is { } path)
            {
                _showInFolder(path);
            }
        });
    }

    public int Number { get; }

    public string NumberText => Number.ToString(System.Globalization.CultureInfo.CurrentCulture) + ".";

    /// <summary>The file it comes from, with the page when it is one page of it ("laporan.pdf, page 3").</summary>
    public string SourceName { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial string OutputName { get; set; }

    [ObservableProperty]
    public partial string? OutputPath { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDone), nameof(IsFailed), nameof(IsNotDone), nameof(IsWaitingOrCancelled), nameof(StatusText), nameof(StatusGlyph), nameof(AutomationName))]
    public partial BatchRowState State { get; set; }

    public bool IsWaitingOrCancelled => State is BatchRowState.Waiting or BatchRowState.Cancelled;

    /// <summary>Why it failed, or the converter's remark about a saved file (a cut-off source).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetail), nameof(AutomationName))]
    public partial string? Detail { get; set; }

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public bool IsDone => State == BatchRowState.Done;

    public bool IsFailed => State == BatchRowState.Failed;

    /// <summary>Failed or cancelled: "Coba lagi yang gagal" makes it again.</summary>
    public bool IsNotDone => State is BatchRowState.Failed or BatchRowState.Cancelled;

    public string Caption => Loc.Format("Batch.FromSource", SourceName);

    /// <summary>Said in words as well as by the icon and its color.</summary>
    public string StatusText => Loc.Get(State switch
    {
        BatchRowState.Done => "History.Verified",
        BatchRowState.Failed => "Batch.RowFailed",
        BatchRowState.Cancelled => "Batch.RowCancelled",
        _ => "Batch.RowWaiting",
    });

    /// <summary>Segoe Fluent Icons: check, error badge, stop, clock.</summary>
    public string StatusGlyph => State switch
    {
        BatchRowState.Done => "\uE73E",
        BatchRowState.Failed => "\uEA39",
        BatchRowState.Cancelled => "\uE71A",
        _ => "\uE823",
    };

    public string AutomationName => HasDetail
        ? Loc.Format("Batch.RowSpokenDetail", Number, OutputName, StatusText, Detail!)
        : Loc.Format("Batch.RowSpoken", Number, OutputName, StatusText);

    public string ShowInFolderName => Loc.Format("History.ShowInFolderFor", OutputName);

    public IRelayCommand ShowInFolderCommand { get; }
}

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
        Summary = string.Concat(
            FileExtension.ToCode(entry.SourceExtension),
            " → ",
            FileExtension.ToCode(entry.TargetExtension),
            " · ",
            DisplayFormat.FormatTimestamp(entry.CompletedAt.LocalDateTime, nowLocal));
        ShowInFolderCommand = new RelayCommand(() => showInFolder(this));
    }

    public HistoryEntry Entry { get; }

    public string Name => Entry.SourceFileName;

    /// <summary>"PNG → JPG · Yesterday, 7:40 PM".</summary>
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

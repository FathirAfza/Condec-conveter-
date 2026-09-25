using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.History;

namespace Condec.ViewModels;

/// <summary>Segoe Fluent Icons glyphs for a file, by extension.</summary>
internal static class FileGlyphs
{
    public const string Photo = "";
    public const string Document = "";

    private static readonly HashSet<string> ImageExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".heic", ".heif", ".webp"];

    public static string For(string extension) => ImageExtensions.Contains(extension) ? Photo : Document;
}

/// <summary>The file chosen for conversion.</summary>
/// <param name="PageCount">For a PDF, its number of pages.</param>
public sealed record SourceFile(string Path, string Extension, long Size, int? PageCount = null)
{
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>"Gambar PNG · 2,4 MB", or "Dokumen PDF · 3 halaman · 1,1 MB".</summary>
    public string Description => PageCount is { } pages
        ? $"{FormatCatalog.GetKindName(Extension)} · {pages} halaman · {DisplayFormat.FormatFileSize(Size)}"
        : $"{FormatCatalog.GetKindName(Extension)} · {DisplayFormat.FormatFileSize(Size)}";

    public string Glyph => FileGlyphs.For(Extension);
}

/// <summary>One entry in the format list. ToString is what screen readers announce for the item.</summary>
public sealed record FormatOption(TargetOption Option)
{
    public string DisplayName => Option.DisplayName;

    public string Extension => Option.Extension;

    public bool IsEnabled => Option.IsEnabled;

    /// <summary>The extension, or "Tidak tersedia" so a disabled format isn't told apart by color alone.</summary>
    public string Caption => Option.IsEnabled ? Extension : "Tidak tersedia";

    public override string ToString() => Option.IsEnabled ? $"{DisplayName} ({Extension})" : $"{DisplayName}, tidak tersedia";
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
        Detail = "Menunggu";
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

    /// <summary>"PNG → JPG · Kemarin, 19.40".</summary>
    public string Summary { get; }

    public bool IsVerified => Entry.Verification == VerificationStatus.Verified;

    public string Glyph => FileGlyphs.For(Entry.SourceExtension);

    public string AutomationName => IsVerified ? $"{Name}, {Summary}, terverifikasi" : $"{Name}, {Summary}";

    /// <summary>Screen readers need the file name: every row has the same folder button.</summary>
    public string ShowInFolderName => $"Tampilkan {Name} di folder";

    public IRelayCommand ShowInFolderCommand { get; }
}

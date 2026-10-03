// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Batch;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.History;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

public enum ConverterState
{
    Input,
    Processing,
    Done,
    Failed,
}

/// <summary>
/// Convert File (DESIGN §6.1): the converter card (input, processing, done, failed) and the history card. It offers everything
/// that neither starts nor ends as DXF or DWG; those are Architecture's (DESIGN §6.3).
/// </summary>
/// <remarks>
/// One file that becomes one result is saved through the save dialog, as before. Several files of one kind, or several pages
/// of a PDF or TIFF, are a batch (owner decision 2026-10-03): one folder is chosen, the results are named after their files
/// ("foto.jpg", "laporan-03.png") without ever overwriting anything, and result n always belongs to file n.
/// </remarks>
public sealed partial class ConverterViewModel : ObservableObject
{
    /// <summary>Formats whose result holds one page or picture, so a source with pages gives one result per page.</summary>
    private static readonly HashSet<string> OnePageTargets = [".png", ".jpg", ".jpeg", ".heic", ".bmp", ".gif", ".tif", ".tiff"];

    /// <summary>Picture formats with real pages. An animated GIF's frames are not pages and still convert as the first frame.</summary>
    private static readonly HashSet<string> PagedPictures = [".tif", ".tiff"];

    private readonly ConverterRegistry _registry;
    private readonly ConversionPipeline _pipeline;
    private readonly HistoryStore _history;
    private readonly IDesktopServices _desktop;
    private readonly ActivityLog _log;
    private CancellationTokenSource? _cancellation;
    private ConversionJob? _lastJob;
    private PipelineStage _lastStage;
    private int _chunkCount;
    private bool _loadingHistory;
    private bool _selecting;
    private string _currentName = string.Empty;

    /// <summary>The kind every chosen file shares (DESIGN §6.1): a <see cref="SourceKind"/> name, or the extension for a kind of its own.</summary>
    private string? _kind;

    /// <summary>The batch as it last ran, for "Coba lagi file yang gagal": job n of the list fills result row n.</summary>
    private List<BatchJob> _batchJobs = [];
    private string _batchTargetLabel = string.Empty;

    public ConverterViewModel(ConverterRegistry registry, ConversionPipeline pipeline, HistoryStore history, IDesktopServices desktop, ActivityLog log)
    {
        _registry = registry;
        _pipeline = pipeline;
        _history = history;
        _desktop = desktop;
        _log = log;

        // Only shows the default: saving it here would write an empty history over the file before it is loaded.
        _loadingHistory = true;
        IsHistoryEnabled = true;
        _loadingHistory = false;

        Pages.PropertyChanged += (_, _) => RefreshResults();
    }

    /// <summary>Raised by the Architecture link ("Butuh DXF atau DWG? Buka Architecture"); the window switches pages.</summary>
    public event EventHandler? ArchitectureRequested;

    [RelayCommand]
    private void OpenArchitecture() => ArchitectureRequested?.Invoke(this, EventArgs.Empty);

    private static bool IsCad(string extension) => extension is ".dxf" or ".dwg";

    private static bool InScope(string source, string target) => !(IsCad(source) || IsCad(target));

    private List<TargetOption> TargetOptionsFor(string extension) =>
        [.. _registry.GetTargetOptions(extension).Where(o => InScope(extension, o.Extension))];

    private List<string> SourceExtensions() =>
        [.. _registry.GetSourceExtensions().Where(e => TargetOptionsFor(e).Count > 0)];

    // ---- State ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInput), nameof(IsProcessing), nameof(IsDone), nameof(IsFailed), nameof(IsBatchDone), nameof(IsSingleDone))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial ConverterState State { get; set; }

    public bool IsInput => State == ConverterState.Input;

    public bool IsProcessing => State == ConverterState.Processing;

    public bool IsDone => State == ConverterState.Done;

    public bool IsFailed => State == ConverterState.Failed;

    /// <summary>The last run was a batch: processing and the finished card show the batch's views.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBatchDone), nameof(IsSingleDone))]
    public partial bool IsBatch { get; set; }

    public bool IsBatchDone => IsDone && IsBatch;

    public bool IsSingleDone => IsDone && !IsBatch;

    // ---- Input ----

    /// <summary>The chosen files, numbered in the order their results are made.</summary>
    public ObservableCollection<SourceItem> Sources { get; } = [];

    /// <summary>The one chosen file, for the single-file panel; null when there are none or several.</summary>
    public SourceFile? Source => Sources.Count == 1 ? Sources[0].File : null;

    public bool HasSource => Sources.Count > 0;

    public bool HasNoSource => Sources.Count == 0;

    public bool HasOneSource => Sources.Count == 1;

    public bool HasManySources => Sources.Count > 1;

    /// <summary>The kind's icon (every file is one kind): picture, document, audio or video.</summary>
    public string ManySourcesGlyph => Sources.FirstOrDefault()?.Glyph ?? string.Empty;

    /// <summary>"3 gambar", "2 file PDF".</summary>
    public string ManySourcesTitle => Loc.Format(
        (Enum.TryParse<SourceKind>(_kind, out var kind) ? kind : SourceKind.Other) switch
        {
            SourceKind.Image => "Sources.ManyImages",
            SourceKind.Pdf => "Sources.ManyPdfs",
            SourceKind.Document => "Sources.ManyDocuments",
            SourceKind.Spreadsheet => "Sources.ManySpreadsheets",
            SourceKind.Presentation => "Sources.ManyPresentations",
            SourceKind.Audio => "Sources.ManyAudio",
            SourceKind.Video => "Sources.ManyVideos",
            _ => "Sources.ManyFiles",
        },
        Sources.Count);

    /// <summary>"Total 12,3 MB", or "Total 24 halaman · 12,3 MB" when the files have pages.</summary>
    public string ManySourcesCaption
    {
        get
        {
            var size = DisplayFormat.FormatFileSize(Sources.Sum(s => s.File.Size));
            // Pages are counted only when every file has them (PDFs); a TIFF among photos would make "3 pages" mean nothing.
            var pages = Sources.All(s => s.File.PageCount is not null) ? Sources.Sum(s => s.File.PageCount ?? 0) : 0;
            return pages > 0 ? Loc.Format("Sources.TotalPages", pages, size) : Loc.Format("Sources.Total", size);
        }
    }

    public ObservableCollection<FormatOption> TargetOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConversionFormat), nameof(ShowsPageChoice), nameof(ShowsAudioQuality), nameof(ShowsVideoSize), nameof(ShowsMediaOptions))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial FormatOption? SelectedTarget { get; set; }

    /// <summary>Which pages of a PDF or a multi-page TIFF become results.</summary>
    public PageChoiceViewModel Pages { get; } = new();

    /// <summary>Audio quality and video size for audio and video targets (DESIGN §6.1.2).</summary>
    public MediaOptionsViewModel Media { get; } = new();

    /// <summary>MP3, M4A and WMA have a bitrate to choose; WAV and FLAC are lossless.</summary>
    public bool ShowsAudioQuality => SelectedTarget?.Extension is ".mp3" or ".m4a" or ".wma";

    public bool ShowsVideoSize => SelectedTarget?.Extension is ".mp4" or ".wmv";

    public bool ShowsMediaOptions => ShowsAudioQuality || ShowsVideoSize;

    /// <summary>A file with more than one page going to a one-page format (a picture): the pages are chosen.</summary>
    public bool ShowsPageChoice => SelectedTarget is { } target && OnePageTargets.Contains(target.Extension) && Sources.Any(s => s.File.PageCount > 1);

    public string FormatPlaceholder => Loc.Get(HasSource ? "Format.Placeholder" : "Format.PlaceholderNoFile");

    public string FormatHelpText
    {
        get
        {
            if (!HasSource)
            {
                return Loc.Get("Format.HelpNone");
            }

            var count = TargetOptions.Count(o => o.IsEnabled);
            var extensions = string.Join(", ", Sources.Select(s => s.File.Extension).Distinct());
            return count == 1 ? Loc.Format("Format.HelpOne", extensions) : Loc.Format("Format.HelpCount", count, extensions);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInputMessage))]
    public partial string? InputMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity InputMessageSeverity { get; set; }

    public bool HasInputMessage => InputMessage is not null;

    /// <summary>How many files the conversion will save.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConvertLabel), nameof(ResultCountText), nameof(HasResults), nameof(HasNoResults))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial int ResultCount { get; set; }

    public string ConvertLabel => ResultCount > 1 ? Loc.Format("Convert.LabelMany", ResultCount) : Loc.Get("Convert.Label");

    /// <summary>"12 file akan disimpan", next to the page choice.</summary>
    public string ResultCountText => ResultCount == 1 ? Loc.Get("Pages.ResultOne") : Loc.Format("Pages.Results", ResultCount);

    /// <summary>The count is only said when there is something to save; otherwise the error or the warning says why.</summary>
    public bool HasResults => ResultCount > 0;

    /// <summary>Files are chosen, but none of the chosen pages are in them.</summary>
    public bool HasNoResults => HasSource && SelectedTarget is not null && ResultCount == 0 && Pages.IsValid;

    // ---- Processing ----

    [ObservableProperty]
    public partial string ProcessingTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProcessingCaption { get; set; } = string.Empty;

    /// <summary>"File 2 dari 5: foto.png" while a batch runs.</summary>
    [ObservableProperty]
    public partial string BatchCurrentText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double ProgressPercent { get; set; }

    public string ProgressText => $"{ProgressPercent:0}%";

    public ObservableCollection<ConversionStepViewModel> Steps { get; } = [];

    // ---- Done and failed ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultIntegrity))]
    public partial ConversionResult? Result { get; set; }

    /// <summary>"PNG → JPG", or "PNG, HEIC → JPG" for a batch of mixed pictures.</summary>
    public string ConversionFormat => !HasSource || SelectedTarget is null
        ? string.Empty
        : $"{string.Join(", ", Sources.Where(s => !IsAlreadyTarget(s)).Select(s => FileExtension.ToCode(s.File.Extension)).Distinct())} → {SelectedTarget.DisplayName}";

    public string ResultIntegrity => Result?.ChunkCount == 1
        ? Loc.Get("Result.IntegrityOne")
        : Loc.Format("Result.Integrity", Result?.ChunkCount ?? 0);

    /// <summary>What the converter wants the user to know about this result (a cut-off source, dropped frames).</summary>
    public ObservableCollection<ResultNoteItem> ResultNotes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultMessage))]
    public partial string? ResultMessage { get; set; }

    public bool HasResultMessage => ResultMessage is not null;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>The source's name on the failed card.</summary>
    [ObservableProperty]
    public partial string FailedSourceName { get; set; } = string.Empty;

    // ---- Batch result ----

    public ObservableCollection<BatchResultItem> BatchResults { get; } = [];

    [ObservableProperty]
    public partial string BatchFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity BatchSeverity { get; set; }

    [ObservableProperty]
    public partial string BatchTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BatchMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BatchSavedText { get; set; } = string.Empty;

    /// <summary>At least one file was not saved: "Coba lagi file yang gagal" is offered.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    public partial bool BatchHasUnsaved { get; set; }

    /// <summary>"Coba lagi file yang gagal" when something failed; "Konversi sisanya" when the rest was only stopped.</summary>
    [ObservableProperty]
    public partial string BatchRetryLabel { get; set; } = string.Empty;

    /// <summary>Nothing was saved: "Ubah pilihan" is offered instead of opening an empty folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchHasSaved))]
    public partial bool BatchSavedNothing { get; set; }

    public bool BatchHasSaved => !BatchSavedNothing;

    // ---- History ----

    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];

    public bool HasHistory => HistoryItems.Count > 0;

    public bool HasNoHistory => HistoryItems.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryEmptyText))]
    public partial bool IsHistoryEnabled { get; set; }

    public string HistoryEmptyText => Loc.Get(IsHistoryEnabled ? "History.Empty" : "History.EmptyDisabled");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryMessage))]
    public partial string? HistoryMessage { get; set; }

    public bool HasHistoryMessage => HistoryMessage is not null;

    public async Task InitializeAsync()
    {
        try
        {
            await _history.LoadAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryMessage = Loc.Get("History.LoadFailed");
        }

        RefreshHistory();
    }

    // ---- Choosing files ----

    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var paths = await _desktop.PickSourceFilesAsync(SourceExtensions());
        if (paths.Count > 0)
        {
            await SelectSourcesAsync(paths, add: false);
        }
    }

    /// <summary>"Tambah file…": more files of the same kind, after the ones already chosen.</summary>
    [RelayCommand]
    private async Task AddSourcesAsync()
    {
        // Opens where the chosen files are: more of the same kind are usually next to them.
        var paths = await _desktop.PickSourceFilesAsync(SourceExtensions(), Sources.Count > 0 ? Path.GetDirectoryName(Sources[0].File.Path) : null);
        if (paths.Count > 0)
        {
            await SelectSourcesAsync(paths, add: true);
        }
    }

    /// <summary>Drag and drop: every file is taken, in the order Windows gives them; folders are left out.</summary>
    /// <param name="filePaths">An empty path is a file with no place on disk, such as one inside a ZIP.</param>
    public async Task SelectDroppedAsync(IReadOnlyList<string> filePaths, int folderCount)
    {
        if (folderCount > 0 && filePaths.Count == 0)
        {
            ShowInputMessage(Loc.Get("Input.DropFolder"), InfoBarSeverity.Warning);
            return;
        }

        var readable = filePaths.Where(p => p.Length > 0).ToList();
        if (readable.Count == 0)
        {
            ReportUnreadableDrop();
            return;
        }

        await SelectSourcesAsync(readable, add: false);
        if (State == ConverterState.Input && InputMessage is null)
        {
            if (readable.Count < filePaths.Count)
            {
                ReportUnreadableDrop();
            }
            else if (folderCount > 0)
            {
                ShowInputMessage(Loc.Get("Input.FoldersSkipped"), InfoBarSeverity.Informational);
            }
        }
    }

    public void ReportUnreadableDrop() =>
        ShowInputMessage(Loc.Get("Input.DropUnreadable"), InfoBarSeverity.Warning);

    /// <summary>
    /// Takes the files that can be converted together. A single file that can't is explained as before; from several, the
    /// ones of another kind (or that can't be read) are left out and named, so the rest still convert.
    /// </summary>
    public async Task SelectSourcesAsync(IReadOnlyList<string> paths, bool add)
    {
        if (State != ConverterState.Input || _selecting)
        {
            return;
        }

        _selecting = true;
        try
        {
            var kept = add ? Sources.Select(s => s.File).ToList() : [];
            var kind = add && kept.Count > 0 ? _kind : null;
            var known = new HashSet<string>(kept.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            var skipped = new List<string>();
            var otherKind = false;
            string? singleProblem = null;
            var singleSeverity = InfoBarSeverity.Warning;

            foreach (var path in paths)
            {
                if (!known.Add(path))
                {
                    continue;
                }

                var name = Path.GetFileName(path);
                var (file, problem, severity, reason) = await ReadSourceAsync(path, kind);
                if (file is null)
                {
                    singleProblem = problem;
                    singleSeverity = severity;
                    otherKind |= reason == "Skip.OtherKind";
                    skipped.Add(Loc.Format("Skip.Item", name, Loc.Get(reason)));
                    continue;
                }

                kind ??= KindKey(file.Extension);
                kept.Add(file);
            }

            var added = kept.Count - (add ? Sources.Count : 0);
            if (added == 0)
            {
                // Nothing new: a single file keeps the message it always had; several get one list of what was left out.
                if (skipped.Count == 1 && paths.Count == 1 && singleProblem is not null)
                {
                    ShowInputMessage(singleProblem, singleSeverity);
                }
                else if (skipped.Count > 0)
                {
                    ShowInputMessage(Loc.Format("Input.NoneUsable", string.Join(", ", skipped)) + (otherKind ? " " + Loc.Get("Input.SameKindRule") : string.Empty), InfoBarSeverity.Warning);
                }

                return;
            }

            _kind = kind;
            SetSources(kept);
            InputMessage = null;
            if (skipped.Count > 0)
            {
                ShowInputMessage(Loc.Format("Input.SkippedSome", string.Join(", ", skipped)) + (otherKind ? " " + Loc.Get("Input.SameKindRule") : string.Empty), InfoBarSeverity.Warning);
            }
            else if (TargetOptions.FirstOrDefault(o => !o.IsEnabled)?.Option.DisabledReason is { } reason)
            {
                ShowInputMessage(reason, InfoBarSeverity.Informational);
            }
        }
        finally
        {
            _selecting = false;
        }
    }

    /// <summary>Several files are one kind when they share a <see cref="SourceKind"/>; an unlisted format is only its own kind.</summary>
    private static string KindKey(string extension)
    {
        var kind = SourceKinds.Of("x" + extension);
        return kind == SourceKind.Other ? extension : kind.ToString();
    }

    /// <summary>The file, or why it can't be taken: the message a single file shows, and the short reason in a list of several.</summary>
    private async Task<(SourceFile? File, string? Problem, InfoBarSeverity Severity, string Reason)> ReadSourceAsync(string path, string? kind)
    {
        var extension = FileExtension.FromPath(path);
        var options = extension.Length == 0 ? [] : TargetOptionsFor(extension);
        if (options.Count == 0)
        {
            // A DWG dropped on Convert File: say where it goes instead.
            var elsewhere = extension.Length > 0 && _registry.GetTargetOptions(extension).Count > 0;
            return elsewhere
                ? (null, Loc.Format("Input.UseArchitecture", extension), InfoBarSeverity.Informational, "Skip.Architecture")
                : (null, DescribeUnsupported(extension), InfoBarSeverity.Warning, "Skip.Unsupported");
        }

        if (kind is not null && KindKey(extension) != kind)
        {
            return (null, Loc.Get("Input.SameKindRule"), InfoBarSeverity.Warning, "Skip.OtherKind");
        }

        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, Loc.Get("Input.CannotOpen"), InfoBarSeverity.Warning, "Skip.Unreadable");
        }

        int? pageCount = null;
        if (extension == ".pdf")
        {
            try
            {
                pageCount = await Task.Run(() => PdfInspector.CountPages(path));
            }
            catch (LockedPdfException)
            {
                return (null, ErrorMessages.LockedPdf, InfoBarSeverity.Warning, "Skip.Locked");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // PdfPig throws its own exception types for damaged files.
                return (null, Loc.Get("Input.PdfUnreadable"), InfoBarSeverity.Warning, "Skip.Unreadable");
            }
        }
        else if (PagedPictures.Contains(extension))
        {
            try
            {
                var header = await ImageHeader.ReadAsync(path);
                pageCount = (int)header.FrameCount;
            }
            catch (Exception)
            {
                // The conversion itself reports a picture Windows can't read; here it is one page.
                pageCount = null;
            }
        }

        return (new SourceFile(path, extension, size, pageCount), null, InfoBarSeverity.Informational, string.Empty);
    }

    private void SetSources(IReadOnlyList<SourceFile> files)
    {
        Sources.Clear();
        foreach (var file in files)
        {
            Sources.Add(new SourceItem(file, RemoveSource) { Number = Sources.Count + 1 });
        }

        if (!files.Any(f => f.PageCount > 1))
        {
            Pages.Reset();
        }

        RefreshTargets();
    }

    private void RemoveSource(SourceItem item)
    {
        if (State != ConverterState.Input || !Sources.Remove(item))
        {
            return;
        }

        if (Sources.Count == 0)
        {
            ConvertAnother();
            return;
        }

        for (var i = 0; i < Sources.Count; i++)
        {
            Sources[i].Number = i + 1;
        }

        InputMessage = null;
        RefreshTargets();
    }

    /// <summary>The formats every chosen file can go to; the chosen one stays when it still fits.</summary>
    private void RefreshTargets()
    {
        var previous = SelectedTarget?.Extension;
        var options = SourceKinds.CommonTargets(Sources.Select(s => s.File.Extension), TargetOptionsFor, o => o.Extension);
        TargetOptions.Clear();
        foreach (var option in options)
        {
            TargetOptions.Add(new FormatOption(option));
        }

        SelectedTarget = TargetOptions.FirstOrDefault(o => o.Extension == previous && o.Option.IsEnabled);
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(HasNoSource));
        OnPropertyChanged(nameof(HasOneSource));
        OnPropertyChanged(nameof(HasManySources));
        OnPropertyChanged(nameof(ManySourcesGlyph));
        OnPropertyChanged(nameof(ManySourcesTitle));
        OnPropertyChanged(nameof(ManySourcesCaption));
        OnPropertyChanged(nameof(FormatPlaceholder));
        OnPropertyChanged(nameof(FormatHelpText));
        OnPropertyChanged(nameof(ConversionFormat));
        OnPropertyChanged(nameof(ShowsPageChoice));
        RefreshResults();
    }

    partial void OnSelectedTargetChanged(FormatOption? value) => RefreshResults();

    /// <summary>Counts the results and tells each file what it becomes.</summary>
    private void RefreshResults()
    {
        var results = PlannedResults();
        ResultCount = results.Count;
        foreach (var item in Sources)
        {
            var own = results.Where(r => r.Source == item).ToList();
            item.Output = SelectedTarget is null ? string.Empty
                : IsAlreadyTarget(item) ? Loc.Format("Sources.AlreadyTarget", SelectedTarget.DisplayName)
                : own.Count == 0 ? Loc.Get("Sources.NoPages")
                : own.Count == 1 ? Loc.Format("Sources.OutputOne", OutputNames.BaseName(Request(own[0]), Loc.Get("Convert.SameFormatSuffix")))
                : Loc.Format("Sources.OutputMany", own.Count);
        }

        OnPropertyChanged(nameof(HasNoResults));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What will be saved, in order: each file, or each chosen page of it.</summary>
    private List<(SourceItem Source, int? Page)> PlannedResults()
    {
        var results = new List<(SourceItem, int?)>();
        if (SelectedTarget is null)
        {
            return results;
        }

        var paged = ShowsPageChoice;
        var range = Pages.Range;
        foreach (var item in Sources)
        {
            if (IsAlreadyTarget(item))
            {
                continue;
            }

            if (!paged || item.File.PageCount is not > 1)
            {
                // One page, or a whole document (PDF to Word): one result, unless that one page wasn't chosen.
                if (paged && range is not null && range.PagesIn(1).Count == 0)
                {
                    continue;
                }

                results.Add((item, null));
                continue;
            }

            if (range is null)
            {
                continue;
            }

            foreach (var page in range.PagesIn(item.File.PageCount.Value))
            {
                results.Add((item, page));
            }
        }

        return results;
    }

    /// <summary>A JPG in a batch going to JPG: it already is the result, so it is left as it is (DESIGN §6.1).</summary>
    private bool IsAlreadyTarget(SourceItem item) =>
        SelectedTarget is { } target
        && SourceKinds.SameFormat(item.File.Extension, target.Extension)
        && !TargetOptionsFor(item.File.Extension).Any(o => o.Extension == target.Extension);

    private OutputRequest Request((SourceItem Source, int? Page) result) =>
        new(result.Source.File.Path, result.Page, result.Source.File.PageCount ?? 1, SelectedTarget!.Extension);

    [RelayCommand]
    private void DismissInputMessage() => InputMessage = null;

    private string DescribeUnsupported(string extension)
    {
        var supported = string.Join(", ", SourceExtensions().Select(extension => FormatCatalog.GetTargetLabel(extension)).Distinct());
        return extension.Length == 0
            ? Loc.Format("Input.UnsupportedNoExtension", supported)
            : Loc.Format("Input.Unsupported", extension, supported);
    }

    private void ShowInputMessage(string message, InfoBarSeverity severity)
    {
        InputMessageSeverity = severity;
        InputMessage = message;
    }

    // ---- Converting ----

    private bool CanConvert() =>
        State == ConverterState.Input
        && HasSource
        && SelectedTarget is { Option.IsEnabled: true }
        && (!ShowsPageChoice || Pages.IsValid)
        && ResultCount > 0;

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        if (SelectedTarget is null)
        {
            return;
        }

        var results = PlannedResults();
        if (results.Count == 0)
        {
            return;
        }

        if (results.Count == 1 && Sources.Count == 1)
        {
            await ConvertOneAsync(results[0]);
        }
        else
        {
            await ConvertBatchAsync(results);
        }
    }

    private async Task ConvertOneAsync((SourceItem Source, int? Page) planned)
    {
        var source = planned.Source.File;
        var target = SelectedTarget!;

        // A smaller MP4 is an MP4 again: its name is suggested with a suffix, so the source isn't the first thing offered to be replaced.
        var suggestedName = Path.GetFileNameWithoutExtension(OutputNames.BaseName(Request(planned), Loc.Get("Convert.SameFormatSuffix")));
        var destination = await _desktop.PickDestinationAsync(
            suggestedName,
            Path.GetDirectoryName(source.Path),
            target.DisplayName,
            target.Extension);
        if (destination is null)
        {
            return;
        }

        // A name typed with another extension still gets the real one, so the file is never mislabeled.
        if (FileExtension.FromPath(destination) != target.Extension)
        {
            destination += target.Extension;
        }

        await RunAsync(new ConversionJob(source.Path, target.Extension, destination, BuildOptions(source, planned.Page)));
    }

    /// <summary>The page (a PDF always has one; a TIFF only when a page was chosen) and the audio or video settings.</summary>
    private ConversionOptions? BuildOptions(SourceFile source, int? page)
    {
        var target = SelectedTarget!.Extension;
        if (source.Extension == ".pdf" && OnePageTargets.Contains(target))
        {
            return new PdfPageOptions(page ?? 1);
        }

        if (page is { } number)
        {
            return new PageOptions(number);
        }

        return ShowsMediaOptions ? Media.BuildOptions() : null;
    }

    private async Task ConvertBatchAsync(List<(SourceItem Source, int? Page)> results)
    {
        var target = SelectedTarget!;
        var folder = await _desktop.PickFolderAsync(Path.GetDirectoryName(Sources[0].File.Path));
        if (folder is null)
        {
            return;
        }

        IReadOnlyList<string> names;
        try
        {
            names = OutputNames.Plan([.. results.Select(Request)], folder, Loc.Get("Convert.SameFormatSuffix"), Exists);
        }
        catch (IOException)
        {
            ShowInputMessage(Loc.Get("Error.Io"), InfoBarSeverity.Warning);
            return;
        }

        var jobs = results
            .Select((r, i) => new BatchJob(new ConversionJob(r.Source.File.Path, target.Extension, names[i], BuildOptions(r.Source.File, r.Page)), _pipeline))
            .ToList();

        _batchJobs = jobs;
        _batchTargetLabel = target.DisplayName;
        BatchFolder = folder;
        BatchResults.Clear();
        for (var i = 0; i < jobs.Count; i++)
        {
            BatchResults.Add(new BatchResultItem(i + 1, ResultSourceName(results[i].Source.File.Name, results[i].Page), Path.GetFileName(names[i]), ShowPathInFolder));
        }

        await RunBatchAsync([.. Enumerable.Range(0, jobs.Count)]);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>"laporan.pdf, halaman 3" for one page of a file; the file name otherwise.</summary>
    private static string ResultSourceName(string name, int? page) =>
        page is { } number ? Loc.Format("Batch.PageName", name, number) : name;

    /// <summary>Runs the rows <paramref name="rows"/> of the batch (all of them, or the ones that weren't saved).</summary>
    private async Task RunBatchAsync(IReadOnlyList<int> rows)
    {
        var jobs = rows.Select(r => _batchJobs[r]).ToList();
        foreach (var row in rows)
        {
            BatchResults[row].State = BatchRowState.Waiting;
            BatchResults[row].Detail = null;
            BatchResults[row].OutputPath = null;
        }

        IsBatch = true;
        _lastStage = PipelineStage.Decode;
        _chunkCount = 0;
        InputMessage = null;
        ProcessingTitle = Loc.Format("Batch.ProcessingTitle", jobs.Count);
        ProcessingCaption = Loc.Format("Batch.ProcessingCaption", _batchTargetLabel, BatchFolder);
        BatchCurrentText = string.Empty;
        ProgressPercent = 0;
        ResetSteps(_batchTargetLabel);
        State = ConverterState.Processing;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var started = System.Diagnostics.Stopwatch.StartNew();
        var kind = $"{string.Join(",", jobs.Select(j => FileExtension.FromPath(j.Job.SourcePath)).Distinct())} -> {FileExtension.Normalize(jobs[0].Job.TargetExtension)}";
        _log.Info($"Batch started: {kind}, {jobs.Count} files");

        // Created here, on the UI thread, so progress reports come back to it.
        var progress = new Progress<BatchProgress>(p => OnBatchProgress(p, rows));
        var outcomes = await ConversionBatch.RunAsync(jobs, progress, r => OnBatchItemFinishedAsync(r, rows[r.Index], kind), Exists, cancellation.Token);
        _cancellation = null;

        var done = BatchResults.Count(r => r.IsDone);
        var failed = BatchResults.Count(r => r.IsFailed);
        var notSaved = BatchResults.Count - done;
        _log.Info(string.Create(CultureInfo.InvariantCulture, $"Batch finished: {kind}, {done} saved, {failed} failed, {notSaved - failed} not converted, {started.Elapsed.TotalSeconds:0.0} s"));

        if (done == 0 && failed == 0)
        {
            // Stopped before anything was saved: back to the choice, as a single conversion does.
            State = ConverterState.Input;
            ShowInputMessage(Loc.Get("Input.Cancelled"), InfoBarSeverity.Informational);
            return;
        }

        var cancelled = outcomes.Any(o => o.Outcome == BatchItemOutcome.Cancelled);
        (BatchSeverity, BatchTitle, BatchMessage) = (done, failed) switch
        {
            _ when done == BatchResults.Count => (InfoBarSeverity.Success, Loc.Get("Done.Title"), Loc.Format("Batch.AllDone", done)),
            (0, _) => (InfoBarSeverity.Error, Loc.Get("Failed.Title"), Loc.Get("Batch.None")),
            _ when cancelled && failed == 0 => (InfoBarSeverity.Warning, Loc.Get("Batch.StoppedTitle"), Loc.Format("Batch.Stopped", done, BatchResults.Count)),
            _ => (InfoBarSeverity.Warning, Loc.Get("Batch.SomeFailedTitle"), Loc.Format("Batch.SomeFailed", done, BatchResults.Count)),
        };
        BatchSavedText = Loc.Format("Batch.SavedCount", done, BatchResults.Count);
        BatchHasUnsaved = notSaved > 0;
        BatchRetryLabel = Loc.Get(failed > 0 ? "Batch.RetryFailed" : "Batch.RetryRest");
        BatchSavedNothing = done == 0;
        State = ConverterState.Done;
    }

    private void OnBatchProgress(BatchProgress progress, IReadOnlyList<int> rows)
    {
        if (State != ConverterState.Processing)
        {
            return;
        }

        var row = BatchResults[rows[progress.Index]];
        _currentName = row.SourceName;
        BatchCurrentText = Loc.Format("Batch.Current", progress.Index + 1, progress.Count, row.SourceName);

        // Rounded down, so 100% only shows once the last file is really saved.
        ProgressPercent = Math.Floor(progress.OverallFraction * 100);
        UpdateSteps(progress.Item);
    }

    private async Task OnBatchItemFinishedAsync(BatchItemResult result, int rowIndex, string kind)
    {
        var row = BatchResults[rowIndex];
        row.OutputName = Path.GetFileName(result.Job.DestinationPath);
        switch (result.Outcome)
        {
            case BatchItemOutcome.Done:
                row.OutputPath = result.Result!.OutputPath;
                row.Detail = result.Result.Notes?.FirstOrDefault()?.Message;
                row.State = BatchRowState.Done;
                _batchJobs[rowIndex] = _batchJobs[rowIndex] with { Job = result.Job };
                await RecordHistoryAsync(result.Job, result.Result, row.SourceName);
                break;
            case BatchItemOutcome.Failed:
                row.Detail = ErrorMessages.Describe(result.Error!, result.LastStage, _batchTargetLabel);
                row.State = BatchRowState.Failed;
                _log.Error($"Batch file failed: {kind}, at {result.LastStage}: {result.Error!.GetType().Name}: {result.Error.Message}");
                break;
            default:
                row.State = BatchRowState.Cancelled;
                break;
        }

        // Each file is a step of its own: the list starts fresh for the next one.
        _chunkCount = 0;
        _lastStage = PipelineStage.Decode;
    }

    /// <summary>"Coba lagi file yang gagal": the files that weren't saved, again, into the same folder.</summary>
    [RelayCommand(CanExecute = nameof(BatchHasUnsaved))]
    private async Task RetryFailedAsync()
    {
        var rows = BatchResults.Select((r, i) => (r, i)).Where(x => x.r.IsNotDone).Select(x => x.i).ToList();
        if (rows.Count > 0)
        {
            await RunBatchAsync(rows);
        }
    }

    [RelayCommand]
    private void OpenBatchFolder()
    {
        try
        {
            ResultMessage = null;
            _desktop.OpenFolder(BatchFolder);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ResultMessage = Loc.Get("Shell.ExplorerFailed");
        }
    }

    private void ShowPathInFolder(string path)
    {
        try
        {
            ResultMessage = null;
            _desktop.ShowInFolder(path);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ResultMessage = Loc.Get("Shell.ExplorerFailed");
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_lastJob is not null)
        {
            await RunAsync(_lastJob);
        }
    }

    [RelayCommand]
    private void BackToInput()
    {
        IsBatch = false;
        State = ConverterState.Input;
    }

    [RelayCommand]
    private void ConvertAnother()
    {
        TargetOptions.Clear();
        SelectedTarget = null;
        Sources.Clear();
        _kind = null;
        Pages.Reset();
        Result = null;
        ResultNotes.Clear();
        ResultMessage = null;
        InputMessage = null;
        BatchResults.Clear();
        IsBatch = false;
        State = ConverterState.Input;
        RefreshTargets();
    }

    private async Task RunAsync(ConversionJob job)
    {
        _lastJob = job;
        _lastStage = PipelineStage.Decode;
        _chunkCount = 0;
        _currentName = Path.GetFileName(job.SourcePath);
        IsBatch = false;
        FailedSourceName = _currentName;
        ProcessingTitle = Loc.Format("Processing.Title", _currentName);
        ProcessingCaption = Loc.Format("Processing.Caption", SelectedTarget?.DisplayName ?? string.Empty, job.DestinationPath);
        InputMessage = null;
        ResultMessage = null;
        ResultNotes.Clear();
        ProgressPercent = 0;
        ResetSteps(SelectedTarget?.DisplayName ?? string.Empty);
        State = ConverterState.Processing;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        // Created here, on the UI thread, so progress reports come back to it.
        var progress = new Progress<PipelineProgress>(OnProgress);
        var kind = $"{FileExtension.FromPath(job.SourcePath)} -> {FileExtension.Normalize(job.TargetExtension)}";
        var started = System.Diagnostics.Stopwatch.StartNew();
        _log.Info($"Conversion started: {kind}");
        try
        {
            // Hashing and verification run on a worker thread; the window stays responsive.
            var result = await Task.Run(() => _pipeline.RunAsync(job, progress, cancellation.Token), CancellationToken.None);
            ResultNotes.Clear();
            foreach (var note in result.Notes ?? [])
            {
                ResultNotes.Add(new ResultNoteItem(note.Message, note.Severity == NoteSeverity.Warning ? InfoBarSeverity.Warning : InfoBarSeverity.Informational));
                _log.Info($"Conversion note ({note.Severity}): {kind}");
            }

            Result = result;
            State = ConverterState.Done;
            _log.Info(string.Create(CultureInfo.InvariantCulture, $"Conversion verified: {kind}, {result.ChunkCount} chunks, {started.Elapsed.TotalSeconds:0.0} s"));
            await RecordHistoryAsync(job, result, Path.GetFileName(job.SourcePath));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            State = ConverterState.Input;
            ShowInputMessage(Loc.Get("Input.Cancelled"), InfoBarSeverity.Informational);
            _log.Info($"Conversion cancelled: {kind}, at {_lastStage}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ErrorMessages.Describe(ex, _lastStage, SelectedTarget?.DisplayName ?? job.TargetExtension);
            State = ConverterState.Failed;
            _log.Error($"Conversion failed: {kind}, at {_lastStage}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _cancellation = null;
        }
    }

    private void ResetSteps(string targetLabel)
    {
        Steps.Clear();
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.Decode")));
        Steps.Add(new ConversionStepViewModel(Loc.Format("Step.Encode", targetLabel)));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.VerifyChunks")));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.VerifyIntegrity")));
    }

    private void OnProgress(PipelineProgress progress)
    {
        if (State != ConverterState.Processing)
        {
            return;
        }

        // Rounded down, so 100% only shows once the file is really saved.
        ProgressPercent = Math.Floor(progress.OverallFraction * 100);
        UpdateSteps(progress);
    }

    /// <summary>The four-stage list, for the one file being made.</summary>
    private void UpdateSteps(PipelineProgress progress)
    {
        if (progress.Stage != _lastStage)
        {
            _log.Debug($"Stage {progress.Stage}");
        }

        _lastStage = progress.Stage;
        if (progress.ChunkCount > 0)
        {
            _chunkCount = progress.ChunkCount;
        }

        var current = (int)progress.Stage;
        var allDone = progress.Stage == PipelineStage.VerifyIntegrity && progress.StageFraction >= 1;
        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (i < current || (i == current && allDone))
            {
                step.State = StepState.Done;
                step.Detail = DoneDetail((PipelineStage)i);
            }
            else if (i == current)
            {
                step.State = StepState.Active;
                step.Detail = ActiveDetail(progress);
            }
            else
            {
                step.State = StepState.Waiting;
                step.Detail = Loc.Get("Step.Waiting");
            }
        }
    }

    private string DoneDetail(PipelineStage stage) => stage switch
    {
        PipelineStage.VerifyChunks => _chunkCount == 1 ? Loc.Get("Step.ChunksMatchOne") : Loc.Format("Step.ChunksMatch", _chunkCount),
        PipelineStage.VerifyIntegrity => Loc.Get("Step.HashMatches"),
        _ => Loc.Get("Step.Done"),
    };

    private string ActiveDetail(PipelineProgress progress) => progress.Stage switch
    {
        PipelineStage.Decode => progress.Detail ?? Loc.Format("Step.Reading", _currentName),
        PipelineStage.Encode => progress.Detail ?? Loc.Format("Step.Encoding", SelectedTarget?.Extension ?? string.Empty),
        PipelineStage.VerifyChunks when progress.ChunkNumber > 0 => Loc.Format("Step.ChunkOf", progress.ChunkNumber, progress.ChunkCount),
        PipelineStage.VerifyChunks => Loc.Get("Step.RereadingFromDisk"),
        _ => Loc.Get(progress.StageFraction < 0.5 ? "Step.ComputingHash" : "Step.Reopening"),
    };

    // ---- Done actions ----

    [RelayCommand]
    private void OpenResult()
    {
        if (Result is not null)
        {
            RunShellAction(() => _desktop.OpenFile(Result.OutputPath), Loc.Get("Shell.NoApp"));
        }
    }

    [RelayCommand]
    private void ShowResultInFolder()
    {
        if (Result is not null)
        {
            RunShellAction(() => _desktop.ShowInFolder(Result.OutputPath), Loc.Get("Shell.ExplorerFailed"));
        }
    }

    private void RunShellAction(Action action, string failureMessage)
    {
        try
        {
            ResultMessage = null;
            action();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ResultMessage = failureMessage;
        }
    }

    // ---- History ----

    partial void OnIsHistoryEnabledChanged(bool value)
    {
        if (!_loadingHistory)
        {
            _ = SaveHistoryEnabledAsync(value);
        }
    }

    private async Task SaveHistoryEnabledAsync(bool enabled)
    {
        try
        {
            await _history.SetEnabledAsync(enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryMessage = Loc.Get("History.SaveSettingFailed");
        }
    }

    /// <param name="sourceName">The file's name, with the page when the result is one page of it.</param>
    private async Task RecordHistoryAsync(ConversionJob job, ConversionResult result, string sourceName)
    {
        try
        {
            await _history.AddAsync(new HistoryEntry(
                sourceName,
                FileExtension.FromPath(job.SourcePath),
                FileExtension.Normalize(job.TargetExtension),
                DateTimeOffset.Now,
                result.OutputPath,
                VerificationStatus.Verified));
            RefreshHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryMessage = Loc.Get("History.RecordFailed");
        }
    }

    [RelayCommand(CanExecute = nameof(HasHistory))]
    private async Task ClearHistoryAsync()
    {
        try
        {
            await _history.ClearAsync();
            HistoryMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryMessage = Loc.Get("History.ClearFailed");
        }

        RefreshHistory();
    }

    [RelayCommand]
    private void DismissHistoryMessage() => HistoryMessage = null;

    private void ShowHistoryItemInFolder(HistoryItemViewModel item)
    {
        var path = item.Entry.OutputPath;
        var folder = Path.GetDirectoryName(path);
        try
        {
            if (File.Exists(path))
            {
                HistoryMessage = null;
                _desktop.ShowInFolder(path);
            }
            else if (folder is not null && Directory.Exists(folder))
            {
                HistoryMessage = Loc.Format("History.FileGone", Path.GetFileName(path));
                _desktop.OpenFolder(folder);
            }
            else
            {
                HistoryMessage = Loc.Format("History.FileAndFolderGone", Path.GetFileName(path));
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            HistoryMessage = Loc.Get("Shell.ExplorerFailed");
        }
    }

    /// <summary>Re-reads the list; Architecture records conversions into the same history.</summary>
    public void RefreshHistory()
    {
        _loadingHistory = true;
        IsHistoryEnabled = _history.IsEnabled;
        _loadingHistory = false;

        var now = DateTime.Now;
        HistoryItems.Clear();
        foreach (var entry in _history.Entries)
        {
            HistoryItems.Add(new HistoryItemViewModel(entry, now, ShowHistoryItemInFolder));
        }

        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasNoHistory));
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Architecture;
using Condec.Core.Batch;
using Condec.Core.Conversion;
using Condec.Core.Devices;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using Condec.Core.Settings;
using Condec.Core.Upscale;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

public enum ArchitecturePhase
{
    /// <summary>No file yet: the drop area.</summary>
    Empty,

    /// <summary>The item shown is being read and looked at.</summary>
    Reading,

    /// <summary>Preview, banner and the list of objects.</summary>
    Review,

    /// <summary>The item shown could not be read.</summary>
    Failed,
}

/// <summary>The InfoBar above the preview (DESIGN §6.3.1).</summary>
public enum ToCadBanner
{
    None,
    Unclear,
    NotEligible,
    Upscaling,
    Ready,
    Skipped,
    Vector,
    NothingFound,
}

public enum CadSourceKind
{
    /// <summary>A picture, or a scanned PDF page: it is looked at and traced.</summary>
    Raster,

    /// <summary>A DXF file: converted to DWG as it is.</summary>
    Dxf,

    /// <summary>A PDF page drawn with lines and text: its shapes are converted directly.</summary>
    VectorPdf,
}

public enum OverlayStyle
{
    Included,
    Excluded,
    Unclear,
}

/// <summary>A box on the preview, in the pixels of the picture the preview shows.</summary>
public sealed record OverlayBox(PixelRect Bounds, OverlayStyle Style);

/// <summary>One kind of object in the "Objek terdeteksi" list.</summary>
public sealed partial class ObjectRow : ObservableObject
{
    private readonly string _description;

    public ObjectRow(DrawingObjectKind kind, string name, string count, string description, bool isChecked)
    {
        Kind = kind;
        Name = name;
        Count = count;
        _description = description;
        IsChecked = isChecked;
    }

    public DrawingObjectKind Kind { get; }

    public string Name { get; }

    public string Count { get; }

    /// <summary>Whether this kind becomes CAD. Kept in step with the list's selection by the view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description), nameof(AutomationName))]
    public partial bool IsChecked { get; set; }

    public string Description => IsChecked ? _description : Loc.Get("Architecture.Desc.Excluded");

    public string AutomationName => $"{Name}, {Count}, {Description}";
}

/// <summary>One entry of the "Upscale n× first" menu; a scale beyond the limit stays in it, locked.</summary>
public sealed record UpscaleChoiceItem(int Scale, bool IsAllowed)
{
    public string Text => IsAllowed ? ScaleText.Format(Scale, Loc.Culture) : $"{ScaleText.Format(Scale, Loc.Culture)} · {Loc.Get("Upscale.ScaleLocked")}";
}

/// <summary>
/// Architecture, "Gambar, PDF, DXF → DWG" (DESIGN §6.3.1, §6.3.4): one or more files of one kind become a queue, a PDF one
/// item per chosen page. Each item is read in the background and reviewed on its own (what was found, what becomes CAD,
/// upscale, calibration); then everything is converted, one result per item, or one combined drawing per PDF.
/// Everything runs on this device.
/// </summary>
public sealed partial class ToCadViewModel : ObservableObject
{
    /// <summary>The most items the queue holds: each read picture keeps its analysis and preview in memory. `[ASUMSI]`</summary>
    public const int MaximumItems = 20;

    private const long GiB = 1024L * 1024 * 1024;

    /// <summary>Share of the progress bar the upscale takes; the rest is the second look at the larger picture.</summary>
    private const double UpscaleShare = 0.85;

    private static readonly string[] SourceExtensions = [".dxf", ".png", ".jpg", ".jpeg", ".heic", ".heif", ".pdf"];

    private static readonly DrawingObjectKind[] KindOrder =
    [
        DrawingObjectKind.Walls,
        DrawingObjectKind.Openings,
        DrawingObjectKind.Text,
        DrawingObjectKind.Logo,
        DrawingObjectKind.Table,
    ];

    private readonly AppSettings _settings;
    private readonly ConversionPipeline _pipeline;
    private readonly ConversionPipeline _architecturePipeline;
    private readonly ConversionPipeline _upscalePipeline;
    private readonly IDesktopServices _desktop;
    private readonly ActivityLog _log;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    /// <summary>Reading a picture and upscaling one both work the processor hard: one at a time.</summary>
    private readonly SemaphoreSlim _engine = new(1, 1);

    private readonly List<SourceFile> _files = [];
    private WindowsTextRecognizer? _recognizer;
    private CancellationTokenSource? _readWork;
    private CancellationTokenSource? _upscaleWork;
    private bool _stopRequested;
    private bool _pdfOptionsLoaded;
    private bool _rebuilding;
    private ToCadItem? _current;
    private int _pendingScale = 2;
    private ToCadBanner _bannerBefore;

    /// <param name="pipeline">The main pipeline: a DXF and a PDF drawn with lines are converted with the converters of Convert File.</param>
    /// <param name="architecturePipeline">Turns an analyzed picture, or several pages together, into DWG or DXF.</param>
    /// <param name="upscalePipeline">The pipeline of Upscale Image, used to enlarge a picture before it is read.</param>
    public ToCadViewModel(
        AppSettings settings,
        ConversionPipeline pipeline,
        ConversionPipeline architecturePipeline,
        ConversionPipeline upscalePipeline,
        ConversionRun run,
        IDesktopServices desktop,
        ActivityLog log)
    {
        _settings = settings;
        _pipeline = pipeline;
        _architecturePipeline = architecturePipeline;
        _upscalePipeline = upscalePipeline;
        Run = run;
        _desktop = desktop;
        _log = log;

        Rows.CollectionChanged += (_, _) => RaiseRows();
        Pdf.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PdfOptionsViewModel.HasValidScale))
            {
                RaiseQueue();
            }
        };
        Pages.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PageChoiceViewModel.ModeIndex) or nameof(PageChoiceViewModel.RangeText))
            {
                RebuildItems();
            }
        };
        Run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionRun.IsActive))
            {
                RaisePhase();
                RaiseQueue();
                PickSourceCommand.NotifyCanExecuteChanged();
                AddSourcesCommand.NotifyCanExecuteChanged();
            }
        };
        Run.Cancelled += (_, _) => ShowMessage(Loc.Get("Input.Cancelled"), InfoBarSeverity.Informational);
        Run.AnotherRequested += (_, _) => Reset();
        _settings.Changed += (_, _) => OnUi(RefreshAdvice);
    }

    /// <summary>The progress list, the finished card and the failed card, for one result or a batch.</summary>
    public ConversionRun Run { get; }

    /// <summary>Unit, scale and switches for the PDF pages drawn with lines (shared by every such page in the queue).</summary>
    public PdfOptionsViewModel Pdf { get; } = new();

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }

    // ---- Phase ----

    public ArchitecturePhase Phase => _current switch
    {
        null => ArchitecturePhase.Empty,
        { Status: QueueItemStatus.Waiting or QueueItemStatus.Reading } => ArchitecturePhase.Reading,
        { Status: QueueItemStatus.Failed } => ArchitecturePhase.Failed,
        _ => ArchitecturePhase.Review,
    };

    public bool ShowsEmpty => Phase == ArchitecturePhase.Empty && !Run.IsActive;

    public bool ShowsReading => Phase == ArchitecturePhase.Reading && !Run.IsActive;

    public bool ShowsReview => Phase == ArchitecturePhase.Review && !Run.IsActive;

    public bool ShowsFailedItem => Phase == ArchitecturePhase.Failed && !Run.IsActive;

    private void RaisePhase()
    {
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(ShowsEmpty));
        OnPropertyChanged(nameof(ShowsReading));
        OnPropertyChanged(nameof(ShowsReview));
        OnPropertyChanged(nameof(ShowsFailedItem));
        OnPropertyChanged(nameof(ShowsQueue));
        OnPropertyChanged(nameof(FailedItemMessage));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    // ---- Messages (above the drop area and the review) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; }

    public bool HasMessage => Message is not null;

    [RelayCommand]
    private void DismissMessage() => Message = null;

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        MessageSeverity = severity;
        Message = message;
    }

    public void ReportUnreadableDrop() => ShowMessage(Loc.Get("Input.DropUnreadable"), InfoBarSeverity.Warning);

    // ---- The queue ----

    public ObservableCollection<ToCadItem> Items { get; } = [];

    /// <summary>The item shown, as the queue list's selection.</summary>
    [ObservableProperty]
    public partial int CurrentIndex { get; set; } = -1;

    partial void OnCurrentIndexChanged(int value)
    {
        if (_rebuilding || value < 0 || value >= Items.Count || ReferenceEquals(Items[value], _current))
        {
            return;
        }

        Show(Items[value]);
    }

    /// <summary>The queue bar: more than one item, or a PDF whose pages can be chosen.</summary>
    public bool ShowsQueue => Phase != ArchitecturePhase.Empty && !Run.IsActive && (Items.Count > 1 || ShowsPageChoice);

    public bool HasManyItems => Items.Count > 1;

    /// <summary>Moving through the queue waits while a picture is being upscaled.</summary>
    public bool CanNavigate => Banner != ToCadBanner.Upscaling && !Run.IsActive;

    private bool CanGoPrevious() => CanNavigate && CurrentIndex > 0;

    private bool CanGoNext() => CanNavigate && CurrentIndex >= 0 && CurrentIndex < Items.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => CurrentIndex--;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => CurrentIndex++;

    private bool CanRemove() => CanNavigate && Items.Count > 1;

    /// <summary>"Hapus dari antrean": the item shown leaves the queue; for a PDF page, only that page.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveCurrent() => RemoveItem(_current);

    /// <summary>The failed card's button: also works when it is the only item.</summary>
    [RelayCommand]
    private void RemoveFailed() => RemoveItem(_current);

    private void RemoveItem(ToCadItem? item)
    {
        if (item is null || !CanNavigate)
        {
            return;
        }

        var index = Items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        if (Items.Count == 1)
        {
            Reset();
            return;
        }

        _rebuilding = true;
        Items.RemoveAt(index);
        if (!Items.Any(i => i.File == item.File))
        {
            _files.Remove(item.File);
        }

        Renumber();
        _rebuilding = false;
        Show(Items[Math.Min(index, Items.Count - 1)]);
        RaiseQueue();
    }

    private void Renumber()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Number = i + 1;
        }
    }

    /// <summary>Which pages of the PDFs become items (DESIGN §6.1.3): every page, the first, or "1-3, 5".</summary>
    public PageChoiceViewModel Pages { get; } = new();

    public bool ShowsPageChoice => _files.Any(f => f.PageCount > 1);

    /// <summary>0: one result per page; 1: one drawing per PDF with its pages side by side.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultCount), nameof(ConvertLabel), nameof(QueueStatusText))]
    public partial int OutputModeIndex { get; set; }

    private bool Combines => OutputModeIndex == 1;

    /// <summary>The choice between separate pages and one drawing: only when a PDF has more than one item.</summary>
    public bool ShowsOutputMode => _files.Any(f => f.Extension == ".pdf" && Items.Count(i => i.File == f) > 1);

    /// <summary>More items were chosen than the queue holds; only the first ones are read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueueStatusText))]
    public partial int OverLimitCount { get; set; }

    public bool IsOverLimit => OverLimitCount > 0;

    /// <summary>None of the chosen pages is in the PDFs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueueStatusText))]
    public partial bool HasNoPages { get; set; }

    /// <summary>"5 item · 3 ditinjau · 2 sedang dibaca", or why the button waits.</summary>
    public string QueueStatusText
    {
        get
        {
            if (Items.Count == 0)
            {
                return string.Empty;
            }

            if (IsOverLimit)
            {
                return Loc.Format("Queue.OverLimit", MaximumItems, OverLimitCount);
            }

            if (ShowsPageChoice && !Pages.IsValid)
            {
                return Loc.Get("Pages.Invalid");
            }

            if (HasNoPages)
            {
                return Loc.Get("Pages.NoneChosen");
            }

            if (Items.FirstOrDefault(i => i.Status == QueueItemStatus.Failed) is { } failed)
            {
                return Loc.Format("Queue.Blocked.Failed", failed.Number);
            }

            if (Items.FirstOrDefault(i => i.HasNothingToConvert) is { } empty)
            {
                return Loc.Format("Queue.Blocked.Nothing", empty.Number);
            }

            var reading = Items.Count(i => !i.IsSettled);
            var viewed = Items.Count(i => i.Viewed);
            return reading > 0
                ? Loc.Format("Queue.Summary.Reading", Items.Count, viewed, reading)
                : Loc.Format("Queue.Summary", Items.Count, viewed);
        }
    }

    private void RaiseQueue()
    {
        OnPropertyChanged(nameof(ShowsQueue));
        OnPropertyChanged(nameof(HasManyItems));
        OnPropertyChanged(nameof(CanNavigate));
        OnPropertyChanged(nameof(ShowsPageChoice));
        OnPropertyChanged(nameof(ShowsOutputMode));
        OnPropertyChanged(nameof(IsOverLimit));
        OnPropertyChanged(nameof(QueueStatusText));
        OnPropertyChanged(nameof(ResultCount));
        OnPropertyChanged(nameof(ConvertLabel));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        RemoveCurrentCommand.NotifyCanExecuteChanged();
        AddSourcesCommand.NotifyCanExecuteChanged();
        PickSourceCommand.NotifyCanExecuteChanged();
        ConvertCommand.NotifyCanExecuteChanged();
    }

    // ---- Reading ----

    public string ReadingTitle => _current is null ? string.Empty : Loc.Format("Architecture.Reading.Title", _current.Name);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingProgressText))]
    public partial double ReadingPercent { get; set; }

    public string ReadingProgressText => $"{ReadingPercent:0}%";

    /// <summary>"Hentikan": the items not read yet leave the queue; what was read stays.</summary>
    [RelayCommand]
    private void CancelReading()
    {
        _stopRequested = true;
        _readWork?.Cancel();
    }

    public string FailedItemMessage => _current?.FailMessage ?? string.Empty;

    // ---- Choosing files ----

    private bool CanPick() => !Run.IsActive && Banner != ToCadBanner.Upscaling;

    /// <summary>"Pilih file…" and "Pilih file lain": a new queue.</summary>
    [RelayCommand(CanExecute = nameof(CanPick))]
    private async Task PickSourceAsync()
    {
        var paths = await _desktop.PickSourceFilesAsync(SourceExtensions);
        if (paths.Count > 0)
        {
            await SelectSourcesAsync(paths, add: false);
        }
    }

    /// <summary>"Tambah file…": more files of the same kind at the end of the queue.</summary>
    [RelayCommand(CanExecute = nameof(CanPick))]
    private async Task AddSourcesAsync()
    {
        var folder = _files.Count > 0 ? Path.GetDirectoryName(_files[0].Path) : null;
        var paths = await _desktop.PickSourceFilesAsync(SourceExtensions, folder);
        if (paths.Count > 0)
        {
            await SelectSourcesAsync(paths, add: true);
        }
    }

    /// <summary>Drag and drop on the drop area: one or more files.</summary>
    /// <param name="filePaths">An empty path is a file with no place on disk, such as one inside a ZIP.</param>
    public async Task SelectDroppedAsync(IReadOnlyList<string> filePaths, int folderCount)
    {
        if (filePaths.Count == 0)
        {
            if (folderCount > 0)
            {
                ShowMessage(Loc.Get("Input.DropFolder"), InfoBarSeverity.Warning);
            }

            return;
        }

        var readable = filePaths.Where(p => p.Length > 0).ToList();
        if (readable.Count == 0)
        {
            ReportUnreadableDrop();
            return;
        }

        await SelectSourcesAsync(readable, add: false);
        if (folderCount > 0 && _files.Count > 0)
        {
            ShowMessage(Loc.Get("Input.FoldersSkipped"), InfoBarSeverity.Informational);
        }
    }

    /// <summary>
    /// Takes the files that can become CAD here and are the same kind as the first one (pictures, PDFs, or DXF files), and
    /// says which were left out and why.
    /// </summary>
    private async Task SelectSourcesAsync(IReadOnlyList<string> paths, bool add)
    {
        if (Run.IsActive || Banner == ToCadBanner.Upscaling)
        {
            return;
        }

        if (!add && _current is not null)
        {
            Reset();
        }

        // One unsupported file on its own keeps the message that names the formats that do work.
        if (paths.Count == 1 && !add && !SourceExtensions.Contains(FileExtension.FromPath(paths[0])))
        {
            var extension = FileExtension.FromPath(paths[0]);
            ShowMessage(
                extension.Length == 0
                    ? Loc.Get("Architecture.ToCad.UnsupportedNoExtension")
                    : Loc.Format("Architecture.ToCad.Unsupported", FileExtension.ToCode(extension)),
                InfoBarSeverity.Warning);
            return;
        }

        var kind = _files.Count > 0 ? SourceKinds.Of(_files[0].Path) : (SourceKind?)null;
        var taken = new List<SourceFile>();
        var skipped = new List<string>();
        string? onlyReason = null;
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var extension = FileExtension.FromPath(path);
            if (!SourceExtensions.Contains(extension))
            {
                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get("Skip.Unsupported")));
                continue;
            }

            if (_files.Concat(taken).Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var fileKind = SourceKinds.Of(path);
            if (kind is { } groupKind && fileKind != groupKind)
            {
                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get("Skip.OtherKind")));
                continue;
            }

            var (file, reason, skip) = await ReadFileAsync(path, extension);
            if (file is null)
            {
                onlyReason = reason;
                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get(skip!)));
                continue;
            }

            kind ??= fileKind;
            taken.Add(file);
        }

        if (taken.Count == 0)
        {
            if (skipped.Count > 0)
            {
                // A single file says exactly what is wrong with it; several are listed.
                ShowMessage(
                    paths.Count == 1 && onlyReason is not null ? onlyReason : $"{Loc.Format("Input.NoneUsable", string.Join(", ", skipped))} {Loc.Get("Input.SameKindRule")}",
                    InfoBarSeverity.Warning);
            }

            return;
        }

        Message = null;
        if (skipped.Count > 0)
        {
            ShowMessage($"{Loc.Format("Input.SkippedSome", string.Join(", ", skipped))} {Loc.Get("Input.SameKindRule")}", InfoBarSeverity.Warning);
        }

        _files.AddRange(taken);
        _log.Info($"Architecture queue: {taken.Count} files added ({string.Join(",", taken.Select(f => f.Extension).Distinct())})");
        RebuildItems();
    }

    /// <summary>The file's size, and for a PDF its number of pages; or why it can't be used.</summary>
    private static async Task<(SourceFile? File, string? Reason, string? SkipKey)> ReadFileAsync(string path, string extension)
    {
        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, Loc.Get("Input.CannotOpen"), "Skip.Unreadable");
        }

        if (extension != ".pdf")
        {
            return (new SourceFile(path, extension, size), null, null);
        }

        try
        {
            var pages = await Task.Run(() => PdfInspector.CountPages(path));
            return (new SourceFile(path, extension, size, pages), null, null);
        }
        catch (LockedPdfException)
        {
            return (null, ErrorMessages.LockedPdf, "Skip.Locked");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // PdfPig throws its own exception types for damaged files.
            return (null, Loc.Get("Input.PdfUnreadable"), "Skip.Unreadable");
        }
    }

    /// <summary>
    /// The queue from the files and the page choice: every picture or DXF, and each chosen page of a PDF. Items that stay keep
    /// their review; at most <see cref="MaximumItems"/> are kept.
    /// </summary>
    private void RebuildItems()
    {
        // The queue bar is disabled while a picture is upscaled; the queue can't change under it.
        if (_files.Count == 0 || Banner == ToCadBanner.Upscaling || (ShowsPageChoice && Pages.Range is null))
        {
            RaiseQueue();
            return;
        }

        var range = Pages.Range ?? PageRange.All;
        var planned = new List<(SourceFile File, int? Page)>();
        foreach (var file in _files)
        {
            if (file.Extension != ".pdf")
            {
                planned.Add((file, null));
            }
            else
            {
                planned.AddRange(range.PagesIn(file.PageCount ?? 1).Select(page => (file, (int?)page)));
            }
        }

        // Every PDF is shorter than the first chosen page: the queue stays as it was, and says so, until other pages are chosen.
        HasNoPages = planned.Count == 0;
        if (HasNoPages)
        {
            RaiseQueue();
            return;
        }

        OverLimitCount = Math.Max(0, planned.Count - MaximumItems);
        var keep = planned.Take(MaximumItems).ToList();
        var old = Items.ToList();

        _rebuilding = true;
        Items.Clear();
        foreach (var (file, page) in keep)
        {
            Items.Add(old.FirstOrDefault(i => i.File == file && i.Page == page) ?? new ToCadItem(file, page));
        }

        Renumber();
        _rebuilding = false;

        // Pages that left the queue are no longer read: the loop only reads what is in it.
        Show(_current is not null && Items.Contains(_current) ? _current : Items[0]);
        RaiseQueue();
        _ = ReadQueueAsync();
    }

    /// <summary>Reads the items one after another, the one shown first. Runs until nothing is left to read.</summary>
    private async Task ReadQueueAsync()
    {
        if (_readWork is { IsCancellationRequested: false })
        {
            return;
        }

        using var work = new CancellationTokenSource();
        _readWork = work;
        _stopRequested = false;
        try
        {
            while (NextToRead() is { } item)
            {
                await _engine.WaitAsync(work.Token);
                try
                {
                    await ReadItemAsync(item, work.Token);
                }
                finally
                {
                    _engine.Release();
                }
            }
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            if (_stopRequested)
            {
                OnReadingStopped();
            }
        }
        finally
        {
            if (_readWork == work)
            {
                _readWork = null;
            }
        }
    }

    private ToCadItem? NextToRead() =>
        _current is { Status: QueueItemStatus.Waiting } current && Items.Contains(current)
            ? current
            : Items.FirstOrDefault(i => i.Status == QueueItemStatus.Waiting);

    /// <summary>"Hentikan": what was read stays in the queue, the rest leaves it.</summary>
    private void OnReadingStopped()
    {
        _stopRequested = false;
        var unread = Items.Where(i => !i.IsSettled).ToList();
        _log.Info($"Reading stopped: {unread.Count} items left out");
        if (unread.Count == Items.Count)
        {
            Reset();
            ShowMessage(Loc.Get("Architecture.Reading.Cancelled"), InfoBarSeverity.Informational);
            return;
        }

        _rebuilding = true;
        foreach (var item in unread)
        {
            Items.Remove(item);
        }

        // The page choice no longer describes the queue; it stays as it was until it is changed.
        foreach (var file in _files.Where(f => !Items.Any(i => i.File == f)).ToList())
        {
            _files.Remove(file);
        }

        Renumber();
        _rebuilding = false;
        Show(Items[0]);
        RaiseQueue();
        ShowMessage(Loc.Get("Architecture.Reading.CancelledSome"), InfoBarSeverity.Informational);
    }

    private async Task ReadItemAsync(ToCadItem item, CancellationToken ct)
    {
        item.Status = QueueItemStatus.Reading;
        item.ReadPercent = 0;
        Report(item);
        try
        {
            switch (item.File.Extension)
            {
                case ".dxf":
                    await ReadDxfAsync(item, ct);
                    break;
                case ".pdf":
                    await ReadPdfPageAsync(item, ct);
                    break;
                default:
                    await ReadPictureAsync(item, ct);
                    break;
            }

            item.Status = item.FailMessage is null ? QueueItemStatus.Ready : QueueItemStatus.Failed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            item.Status = QueueItemStatus.Waiting;
            throw;
        }
        catch (Exception ex)
        {
            item.FailMessage = ErrorMessages.Describe(ex, PipelineStage.Decode, FileExtension.ToCode(item.File.Extension));
            item.Status = QueueItemStatus.Failed;
            _log.Error($"Reading a drawing failed: {ex.GetType().Name}: {ex.Message}");
        }

        if (ReferenceEquals(item, _current))
        {
            Show(item);
        }

        RaiseQueue();
    }

    /// <summary>Progress of the item being read; shown when it is the item on screen.</summary>
    private void Report(ToCadItem item)
    {
        if (ReferenceEquals(item, _current))
        {
            ReadingPercent = item.ReadPercent;
            RaisePhase();
        }
    }

    /// <summary>A DXF is converted as it is; it is read here for the preview, and so that a file that can't be read says so now.</summary>
    private async Task ReadDxfAsync(ToCadItem item, CancellationToken ct)
    {
        var scene = await ArchitectureFiles.ReadDrawingAsync(item.File.Path, ct);
        item.ReadPercent = 60;
        Report(item);
        item.Preview = await TryPreviewAsync(() => ArchitectureFiles.RenderDrawingAsync(scene, name => scene.Layers.FirstOrDefault(l => l.Name == name)?.IsOn ?? true, PaperSize.A3, ct));
        ct.ThrowIfCancellationRequested();
        item.Kind = CadSourceKind.Dxf;
        item.Banner = ToCadBanner.Vector;
    }

    private async Task ReadPdfPageAsync(ToCadItem item, CancellationToken ct)
    {
        var page = item.Page ?? 1;
        PdfPageKind kind;
        try
        {
            kind = await Task.Run(() => PdfInspector.GetPageKind(item.File.Path, page), ct);
        }
        catch (LockedPdfException)
        {
            item.FailMessage = ErrorMessages.LockedPdf;
            return;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // PdfPig throws its own exception types for damaged files.
            item.FailMessage = Loc.Get("Input.PdfUnreadable");
            return;
        }

        switch (kind)
        {
            case PdfPageKind.Empty:
                item.FailMessage = Loc.Get("Pdf.KindEmptyMessage");
                return;
            case PdfPageKind.Scan:
                await ReadPictureAsync(item, ct);
                return;
            default:
                item.ReadPercent = 40;
                Report(item);
                item.Preview = await TryPreviewAsync(() => ArchitectureFiles.RenderPdfAsync(item.File.Path, ct, page));
                ct.ThrowIfCancellationRequested();
                if (!_pdfOptionsLoaded)
                {
                    // Unit and scale start at their defaults once; after that they are the user's, for every page.
                    Pdf.Load(item.File.Path, item.File.PageCount ?? 1);
                    _pdfOptionsLoaded = true;
                }

                item.Kind = CadSourceKind.VectorPdf;
                item.Banner = ToCadBanner.Vector;
                return;
        }
    }

    /// <summary>A preview that can't be drawn doesn't stop the conversion; the page then shows a placeholder.</summary>
    private async Task<PreviewImage?> TryPreviewAsync(Func<Task<PreviewImage>> render)
    {
        try
        {
            return await render();
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            _log.Info($"No preview: {ex.GetType().Name}");
            return null;
        }
    }

    private async Task ReadPictureAsync(ToCadItem item, CancellationToken ct)
    {
        var page = item.Page ?? 1;
        var picture = await Task.Run(() => WindowsPictureReader.ReadAsync(item.File.Path, ct, page), ct);
        item.Kind = CadSourceKind.Raster;
        item.OriginalWidth = item.PictureWidth = picture.Width;
        item.OriginalHeight = item.PictureHeight = picture.Height;
        item.OriginalDpiX = picture.DpiX;
        item.OriginalDpiY = picture.DpiY;
        item.UpscaleFactor = 1;
        item.Calibration = null;
        item.ReadPercent = 5;
        Report(item);

        var (analysis, preview) = await AnalyzeAsync(picture, percent =>
        {
            item.ReadPercent = 5 + (percent * 95);
            Report(item);
        }, ct);
        item.Analysis = analysis;
        item.Preview = preview;

        // Logos and tables are the title block and its legend, which most users don't want as drawing objects.
        item.Included.Clear();
        foreach (var kind in KindOrder.Where(k => !analysis.Group(k).IsEmpty && k is not (DrawingObjectKind.Logo or DrawingObjectKind.Table)))
        {
            item.Included.Add(kind);
        }

        item.Advice = Advise(item);
        item.Banner = analysis.ObjectCount == 0 ? ToCadBanner.NothingFound : BannerFor(item.Advice);
        _log.Info($"Picture read: {analysis.ObjectCount} objects, {analysis.UnclearAreas.Count} unclear areas, text read {analysis.TextWasRead}");
    }

    /// <param name="report">Fraction 0 to 1 of the analysis.</param>
    private async Task<(DrawingAnalysis Analysis, PreviewImage Preview)> AnalyzeAsync(RasterPicture picture, Action<double> report, CancellationToken ct)
    {
        // The preview is the picture the analysis works on, so the boxes of the objects lie exactly on it.
        var reduced = picture.ReduceTo(DrawingAnalyzer.MaximumSide).Picture;
        var preview = new PreviewImage(reduced.Width, reduced.Height, reduced.Bgra);

        var progress = new Progress<double>(report);
        var analysis = await Task.Run(
            () =>
            {
                _recognizer ??= new WindowsTextRecognizer();
                return DrawingAnalyzer.AnalyzeAsync(picture, _recognizer.IsAvailable ? _recognizer : null, progress, ct);
            },
            ct);
        return (analysis, preview);
    }

    // ---- The item shown ----

    /// <summary>Puts <paramref name="item"/> on screen: its preview, its list of objects, its banner and its scale.</summary>
    private void Show(ToCadItem item)
    {
        _current = item;
        var index = Items.IndexOf(item);
        if (CurrentIndex != index)
        {
            _rebuilding = true;
            CurrentIndex = index;
            _rebuilding = false;
        }

        if (item.Status == QueueItemStatus.Ready)
        {
            item.Viewed = true;
        }

        ReadingPercent = item.ReadPercent;
        Preview = item.Preview;
        Banner = item.Banner;
        LoadRows(item);
        if (item.Kind == CadSourceKind.Raster && item.Analysis is not null && item.UpscaleFactor == 1
            && Banner is ToCadBanner.Ready or ToCadBanner.Unclear or ToCadBanner.NotEligible or ToCadBanner.Skipped)
        {
            // The limits in Settings may have changed since it was read.
            ApplyAdvice(keepSkipped: Banner == ToCadBanner.Skipped);
        }
        else
        {
            UpscaleChoices.Clear();
        }

        RaiseSource();
        RaisePhase();
        RaiseBanner();
        RaiseRows();
        OnPropertyChanged(nameof(ReadingTitle));
        OnPropertyChanged(nameof(CalibrationCaption));
        OnPropertyChanged(nameof(CalibrationButtonText));

        // It may just have become reviewed.
        RaiseQueue();
    }

    public string SourceName => _current?.Name ?? string.Empty;

    public string SourcePath => _current?.File.Path ?? string.Empty;

    private CadSourceKind CurrentKind => _current?.Kind ?? CadSourceKind.Raster;

    public bool IsRaster => _current is { Status: QueueItemStatus.Ready, Kind: CadSourceKind.Raster };

    public bool IsVector => _current is { Status: QueueItemStatus.Ready } && CurrentKind != CadSourceKind.Raster;

    /// <summary>"3200 × 2400 · 7,7 MP · setelah upscale 2×", or the kind and size of a vector file.</summary>
    public string SizeCaption
    {
        get
        {
            if (_current is not { } item)
            {
                return string.Empty;
            }

            if (item.Kind != CadSourceKind.Raster)
            {
                return item.File.Description;
            }

            var dimensions = ScaleText.Dimensions(item.PictureWidth, item.PictureHeight);
            var megapixels = ScaleText.Megapixels((long)item.PictureWidth * item.PictureHeight, Loc.Culture);
            return item.UpscaleFactor > 1
                ? Loc.Format("Architecture.Preview.SizeUpscaled", dimensions, megapixels, ScaleText.Format(item.UpscaleFactor, Loc.Culture))
                : Loc.Format("Architecture.Preview.Size", dimensions, megapixels);
        }
    }

    public string TypeCaption => Loc.Get(CurrentKind == CadSourceKind.Raster ? "Architecture.Preview.Raster" : "Architecture.Preview.Vector");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(HasNoPreview), nameof(PreviewAlt))]
    public partial PreviewImage? Preview { get; set; }

    public bool HasPreview => Preview is not null;

    public bool HasNoPreview => Preview is null;

    public string PreviewAlt => Loc.Format("Architecture.Preview.Alt", SourceName);

    private void RaiseSource()
    {
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(IsRaster));
        OnPropertyChanged(nameof(IsVector));
        OnPropertyChanged(nameof(ShowsPdfOptions));
        OnPropertyChanged(nameof(SizeCaption));
        OnPropertyChanged(nameof(TypeCaption));
        OnPropertyChanged(nameof(PreviewAlt));
        OnPropertyChanged(nameof(ShowsFormatChoice));
        OnPropertyChanged(nameof(TargetExtension));
        OnPropertyChanged(nameof(TargetCode));
        OnPropertyChanged(nameof(VectorHint));
        OnPropertyChanged(nameof(CalibrationCaption));
    }

    // ---- The analysis ----

    public ObservableCollection<ObjectRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public bool HasCheckedRows => Rows.Any(r => r.IsChecked);

    /// <summary>Nothing is ticked: say so under the list, and the button waits.</summary>
    public bool ShowsNothingTicked => IsRaster && HasRows && !HasCheckedRows;

    /// <summary>The boxes drawn on the preview: blue for what becomes CAD, dashed gray for what doesn't, yellow where the picture is unclear.</summary>
    public IReadOnlyList<OverlayBox> Overlay
    {
        get
        {
            if (_current?.Analysis is not { } analysis)
            {
                return [];
            }

            var boxes = new List<OverlayBox>();
            foreach (var row in Rows)
            {
                var style = row.IsChecked ? OverlayStyle.Included : OverlayStyle.Excluded;
                boxes.AddRange(analysis.Group(row.Kind).Items.Select(item => new OverlayBox(item.Bounds, style)));
            }

            boxes.AddRange(analysis.UnclearAreas.Select(area => new OverlayBox(area, OverlayStyle.Unclear)));
            return boxes;
        }
    }

    /// <summary>The size of the picture the boxes lie on.</summary>
    public (int Width, int Height) OverlaySize => _current?.Analysis is { } analysis ? (analysis.Width, analysis.Height) : (0, 0);

    public string VectorHint => Loc.Get(CurrentKind == CadSourceKind.Dxf ? "Architecture.Objects.VectorHint" : "Architecture.Objects.PdfHint");

    /// <summary>A PDF drawn with lines: the unit and the scale of the drawing are the user's to choose.</summary>
    public bool ShowsPdfOptions => _current is { Status: QueueItemStatus.Ready, Kind: CadSourceKind.VectorPdf };

    /// <summary>The rows of the item shown, ticked as its review left them.</summary>
    private void LoadRows(ToCadItem item)
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();
        if (item.Kind != CadSourceKind.Raster || item.Analysis is not { } analysis)
        {
            return;
        }

        foreach (var kind in KindOrder)
        {
            var group = analysis.Group(kind);
            if (group.IsEmpty)
            {
                continue;
            }

            var row = new ObjectRow(kind, KindName(kind), CountText(kind, group.Count), KindDescription(kind, analysis), item.Included.Contains(kind));
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObjectRow.IsChecked) && sender is ObjectRow row && _current is { } item)
        {
            if (row.IsChecked)
            {
                item.Included.Add(row.Kind);
            }
            else
            {
                item.Included.Remove(row.Kind);
            }

            item.RaiseStatus();
            RaiseRows();
            RaiseQueue();
        }
    }

    private void RaiseRows()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasCheckedRows));
        OnPropertyChanged(nameof(ShowsNothingTicked));
        OnPropertyChanged(nameof(Overlay));
        OnPropertyChanged(nameof(OverlaySize));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    private static string KindName(DrawingObjectKind kind) => Loc.Get(kind switch
    {
        DrawingObjectKind.Walls => "Architecture.Kind.Walls",
        DrawingObjectKind.Openings => "Architecture.Kind.Openings",
        DrawingObjectKind.Text => "Architecture.Kind.Text",
        DrawingObjectKind.Logo => "Architecture.Kind.Logo",
        _ => "Architecture.Kind.Table",
    });

    private static string CountText(DrawingObjectKind kind, int count)
    {
        var (many, one) = kind switch
        {
            DrawingObjectKind.Walls => ("Architecture.Count.Lines", "Architecture.Count.LinesOne"),
            DrawingObjectKind.Text => ("Architecture.Count.Texts", "Architecture.Count.TextsOne"),
            DrawingObjectKind.Table => ("Architecture.Count.Tables", "Architecture.Count.TablesOne"),
            _ => ("Architecture.Count.Objects", "Architecture.Count.ObjectsOne"),
        };
        return count == 1 ? Loc.Get(one) : Loc.Format(many, count);
    }

    private static string KindDescription(DrawingObjectKind kind, DrawingAnalysis analysis) => Loc.Get(kind switch
    {
        DrawingObjectKind.Walls => "Architecture.Desc.Walls",
        DrawingObjectKind.Openings => "Architecture.Desc.Openings",
        DrawingObjectKind.Text => analysis.TextWasRead ? "Architecture.Desc.Text" : "Architecture.Desc.TextLines",
        _ => "Architecture.Desc.Object",
    });

    // ---- Banner and the upscale advice ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnclear), nameof(IsNotEligible), nameof(IsUpscaling), nameof(IsReady), nameof(IsSkipped), nameof(IsVectorBanner), nameof(IsNothingFound), nameof(ConvertLabel), nameof(CanNavigate))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand), nameof(PickSourceCommand), nameof(AddSourcesCommand), nameof(PreviousCommand), nameof(NextCommand), nameof(RemoveCurrentCommand))]
    public partial ToCadBanner Banner { get; set; }

    partial void OnBannerChanged(ToCadBanner value)
    {
        if (_current is { } item && value != ToCadBanner.Upscaling)
        {
            item.Banner = value;
        }
    }

    public bool IsUnclear => Banner == ToCadBanner.Unclear;

    public bool IsNotEligible => Banner == ToCadBanner.NotEligible;

    public bool IsUpscaling => Banner == ToCadBanner.Upscaling;

    public bool IsReady => Banner == ToCadBanner.Ready;

    public bool IsSkipped => Banner == ToCadBanner.Skipped;

    public bool IsVectorBanner => Banner == ToCadBanner.Vector;

    public bool IsNothingFound => Banner == ToCadBanner.NothingFound;

    public ObservableCollection<UpscaleChoiceItem> UpscaleChoices { get; } = [];

    private RenderEngine EffectiveEngine => UpscaleSupport.EffectiveEngine(_settings.RenderMode, _settings.Device);

    private UpscaleAdvice Advise(ToCadItem item) => ArchitectureUpscalePolicy.Advise(
        item.Analysis?.UnclearAreas.Count ?? 0,
        _settings.Device,
        item.OriginalWidth,
        item.OriginalHeight,
        _settings.ScaleLimit,
        _settings.EffectiveMemoryLimitGb * GiB,
        EffectiveEngine);

    private static ToCadBanner BannerFor(UpscaleAdvice advice) => advice.Kind switch
    {
        UpscaleAdviceKind.NotNeeded => ToCadBanner.Ready,
        UpscaleAdviceKind.Suggest => ToCadBanner.Unclear,
        _ => ToCadBanner.NotEligible,
    };

    private void ApplyAdvice(bool keepSkipped)
    {
        if (_current is not { } item)
        {
            return;
        }

        var advice = Advise(item);
        item.Advice = advice;
        UpscaleChoices.Clear();
        foreach (var choice in advice.Choices)
        {
            UpscaleChoices.Add(new UpscaleChoiceItem(choice.Scale, choice.IsAllowed));
        }

        if (!keepSkipped)
        {
            Banner = BannerFor(advice);
        }

        RaiseBanner();
    }

    /// <summary>The limits in Settings changed while a picture is open: the offer follows them.</summary>
    private void RefreshAdvice()
    {
        if (Phase != ArchitecturePhase.Review || _current is not { Kind: CadSourceKind.Raster, Analysis: not null, UpscaleFactor: 1 }
            || Banner is ToCadBanner.Upscaling or ToCadBanner.Vector or ToCadBanner.NothingFound)
        {
            return;
        }

        ApplyAdvice(keepSkipped: Banner == ToCadBanner.Skipped);
    }

    private void RaiseBanner()
    {
        OnPropertyChanged(nameof(UnclearTitle));
        OnPropertyChanged(nameof(UnclearMessage));
        OnPropertyChanged(nameof(NotEligibleMessage));
        OnPropertyChanged(nameof(UpscaleButtonText));
        OnPropertyChanged(nameof(UpscaleButtonSpoken));
        OnPropertyChanged(nameof(CanUpscale));
        OnPropertyChanged(nameof(ReadyMessage));
        OnPropertyChanged(nameof(SkippedMessage));
        OnPropertyChanged(nameof(SkippedActionText));
        OnPropertyChanged(nameof(VectorTitle));
        OnPropertyChanged(nameof(VectorMessage));
        OnPropertyChanged(nameof(UpscalingTitle));
        OnPropertyChanged(nameof(ConvertLabel));
    }

    private int UnclearCount => _current?.Analysis?.UnclearAreas.Count ?? 0;

    public bool CanUpscale => _current?.Advice?.CanUpscale ?? false;

    private int DefaultScale => _current?.Advice?.DefaultScale ?? 2;

    public string UnclearTitle => Loc.Format("Architecture.Unclear.Title", UnclearCount);

    public string UnclearMessage => Loc.Format("Architecture.Unclear.Message", ScaleText.Format(_current?.Advice?.Limit.Effective ?? 2, Loc.Culture));

    public string UpscaleButtonText => Loc.Format("Architecture.Unclear.UpscaleFirst", ScaleText.Format(DefaultScale, Loc.Culture));

    public string UpscaleButtonSpoken => Loc.Format("Architecture.Unclear.UpscaleSpoken", ScaleText.Format(DefaultScale, Loc.Culture));

    public string NotEligibleMessage
    {
        get
        {
            if (_current is not { Advice: { } advice } item)
            {
                return string.Empty;
            }

            if (advice.IsRamShort)
            {
                return Loc.Format("Architecture.NotEligible.MessageRam", advice.RequiredRamGb, advice.InstalledRamGb);
            }

            return Loc.Format("Architecture.NotEligible.MessageLimit", NotEligibleReason(item, advice));
        }
    }

    /// <summary>Why a 2× upscale isn't possible although the RAM is enough: the picture, the limits, or the memory limit of Settings.</summary>
    private string NotEligibleReason(ToCadItem item, UpscaleAdvice advice)
    {
        if ((long)item.OriginalWidth * item.OriginalHeight > UpscaleSupport.MaximumSourcePixels)
        {
            return Loc.Get("Architecture.NotEligible.TooBig");
        }

        if (advice.Limit.Effective >= 2)
        {
            var (outputWidth, outputHeight) = UpscaleEstimator.OutputSize(item.OriginalWidth, item.OriginalHeight, 2);
            var peak = UpscaleMemory.Plan(
                item.OriginalWidth, item.OriginalHeight, outputWidth, outputHeight, EffectiveEngine, keepsAlpha: true, _settings.EffectiveMemoryLimitGb * GiB, _settings.Pace.MaxTileSize).PeakBytes;
            return Loc.Format("Upscale.Estimate.MemoryShort", _settings.EffectiveMemoryLimitGb, ScaleText.Gigabytes(peak, Loc.Culture));
        }

        var reasons = advice.Limit.DescribeReasons(Loc.Culture);
        return reasons.Length > 0 ? reasons : Loc.Get("Architecture.NotEligible.TooBig");
    }

    private int ClearPercent => (int)Math.Floor((_current?.Analysis?.ClearShare ?? 1) * 100);

    public string ReadyMessage => _current is { UpscaleFactor: > 1 } item
        ? Loc.Format("Architecture.Ready.MessageUpscaled", ScaleText.Format(item.UpscaleFactor, Loc.Culture), ClearPercent)
        : Loc.Format("Architecture.Ready.Message", ClearPercent);

    public string SkippedMessage => Loc.Format("Architecture.Skipped.Message", UnclearCount);

    public string SkippedActionText => Loc.Format("Architecture.Skipped.UpscaleNow", ScaleText.Format(DefaultScale, Loc.Culture));

    public string VectorTitle => Loc.Get("Architecture.Vector.Title");

    public string VectorMessage => Loc.Get(CurrentKind == CadSourceKind.Dxf ? "Architecture.Vector.Dxf" : "Architecture.Vector.Message");

    public string NothingFoundTitle => Loc.Get("Architecture.NothingFound.Title");

    public string NothingFoundMessage => Loc.Get("Architecture.NothingFound.Message");

    [RelayCommand]
    private void SkipUpscale() => Banner = ToCadBanner.Skipped;

    [RelayCommand]
    private void ContinueWithoutUpscale() => Banner = ToCadBanner.Skipped;

    // ---- Upscale ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleProgressText))]
    public partial double UpscalePercent { get; set; }

    public string UpscaleProgressText => $"{UpscalePercent:0}%";

    public string UpscalingTitle => Loc.Format("Architecture.Upscaling.Title", ScaleText.Format(_pendingScale, Loc.Culture));

    [RelayCommand]
    private Task UpscaleDefaultAsync() => StartUpscaleAsync(DefaultScale);

    [RelayCommand]
    private void CancelUpscale() => _upscaleWork?.Cancel();

    /// <summary>Enlarges the picture shown and looks at it again; the kinds that were ticked stay ticked where they still exist.</summary>
    public async Task StartUpscaleAsync(int scale)
    {
        if (Phase != ArchitecturePhase.Review || _current is not { Kind: CadSourceKind.Raster, Advice: { } advice } item
            || !advice.Choices.Any(c => c.Scale == scale && c.IsAllowed) || Banner == ToCadBanner.Upscaling)
        {
            return;
        }

        _bannerBefore = Banner;
        _pendingScale = scale;
        UpscalePercent = 0;
        Message = null;
        Banner = ToCadBanner.Upscaling;
        RaiseBanner();
        RaiseQueue();

        using var work = new CancellationTokenSource();
        _upscaleWork = work;
        var ct = work.Token;
        var input = item.File.Path;
        string? temporaryInput = null;
        var output = ArchitectureFiles.CachePath(".png");
        var started = System.Diagnostics.Stopwatch.StartNew();
        var engineTaken = false;
        _log.Info($"Architecture upscale started: {ScaleText.Format(scale, CultureInfo.InvariantCulture)}");
        try
        {
            // The queue may be reading another picture: the upscale waits for it rather than share the processor.
            await _engine.WaitAsync(ct);
            engineTaken = true;

            var width = item.OriginalWidth;
            var height = item.OriginalHeight;
            if (item.File.Extension == ".pdf")
            {
                // The page is a picture only once it is drawn: write it out, so the upscale has a file to read.
                var page = await Task.Run(() => WindowsPictureReader.ReadAsync(item.File.Path, ct, item.Page ?? 1), ct);
                temporaryInput = ArchitectureFiles.CachePath(".png");
                await ArchitectureFiles.WritePngAsync(page, temporaryInput, ct);
                input = temporaryInput;
                width = page.Width;
                height = page.Height;
            }

            var (outputWidth, outputHeight) = UpscaleEstimator.OutputSize(width, height, scale);
            var pace = _settings.Pace;
            var plan = UpscaleMemory.Plan(width, height, outputWidth, outputHeight, EffectiveEngine, keepsAlpha: true, _settings.EffectiveMemoryLimitGb * GiB, pace.MaxTileSize);
            var job = new ConversionJob(input, ".png", output, new UpscaleOptions(outputWidth, outputHeight, _settings.RenderMode, plan.TileSize, Duty: pace.Duty));
            var progress = new Progress<PipelineProgress>(p => UpscalePercent = Math.Floor(p.OverallFraction * UpscaleShare * 100));
            await Task.Run(() => _upscalePipeline.RunAsync(job, progress, ct), CancellationToken.None);
            ct.ThrowIfCancellationRequested();

            var enlarged = await Task.Run(() => WindowsPictureReader.ReadAsync(output, ct), ct);

            // The enlarged picture draws the same sheet at more pixels: its resolution grows with it, so the size in the drawing stays.
            enlarged = enlarged with
            {
                DpiX = EffectiveDpi(item.OriginalDpiX) * scale,
                DpiY = EffectiveDpi(item.OriginalDpiY) * scale,
            };
            var (analysis, preview) = await AnalyzeAsync(enlarged, percent => UpscalePercent = Math.Floor((UpscaleShare + (percent * (1 - UpscaleShare))) * 100), ct);

            item.UpscaleFactor = scale;
            item.PictureWidth = enlarged.Width;
            item.PictureHeight = enlarged.Height;
            item.Analysis = analysis;
            item.Preview = preview;
            item.Advice = null;
            item.Included.RemoveWhere(kind => analysis.Group(kind).IsEmpty);
            item.Banner = analysis.ObjectCount == 0 ? ToCadBanner.NothingFound : ToCadBanner.Ready;
            _log.Info(string.Create(CultureInfo.InvariantCulture, $"Architecture upscale done: {scale}x, {analysis.UnclearAreas.Count} unclear areas left, {started.Elapsed.TotalSeconds:0.0} s"));
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            item.Banner = _bannerBefore;
            ShowMessage(Loc.Get("Architecture.Upscale.Cancelled"), InfoBarSeverity.Informational);
            _log.Info("Architecture upscale cancelled");
        }
        catch (Exception ex)
        {
            item.Banner = ToCadBanner.Skipped;
            ShowMessage(
                ex is UpscaleModelUnavailableException ? ErrorMessages.Describe(ex, PipelineStage.Decode, "PNG") : Loc.Get("Architecture.Upscale.Failed"),
                InfoBarSeverity.Warning);
            _log.Error($"Architecture upscale failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (engineTaken)
            {
                _engine.Release();
            }

            _upscaleWork = null;
            ArchitectureFiles.Delete(output);
            if (temporaryInput is not null)
            {
                ArchitectureFiles.Delete(temporaryInput);
            }
        }

        // Banner goes back from Upscaling to the item's own banner, with its new picture and list.
        if (_current is { } shown)
        {
            Show(shown);
        }

        RaiseQueue();
        _ = ReadQueueAsync();
    }

    /// <summary>The resolution a file states, or the one assumed when it doesn't (96 pixels per inch).</summary>
    private static double EffectiveDpi(double dpi) => double.IsFinite(dpi) && dpi > 0 ? dpi : ArchitectureCadBuilder.DefaultDpi;

    // ---- Format and scale ----

    /// <summary>0 is DWG, 1 is DXF. A DXF is only ever converted to DWG.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetExtension), nameof(TargetCode), nameof(ConvertLabel))]
    public partial int FormatIndex { get; set; }

    /// <summary>DXF files (the queue holds one kind) only become DWG.</summary>
    private bool IsDxfQueue => _files.Count > 0 && _files[0].Extension == ".dxf";

    public bool ShowsFormatChoice => !IsDxfQueue;

    public string TargetExtension => !IsDxfQueue && FormatIndex == 1 ? ".dxf" : ".dwg";

    public string TargetCode => FileExtension.ToCode(TargetExtension);

    /// <summary>"Tetap konversi…" while the picture has unclear areas and nothing was decided about them; "… N file…" for a batch.</summary>
    public string ConvertLabel => ResultCount > 1
        ? Loc.Format("Convert.LabelMany", ResultCount)
        : Loc.Format(Banner is ToCadBanner.Unclear or ToCadBanner.NotEligible ? "Architecture.ConvertAnyway" : "Architecture.Convert", TargetCode);

    public string CalibrationCaption => _current?.Calibration is { } value
        ? Loc.Format("Architecture.Scale.Calibrated", value.ToString(value >= 0.1 ? "0.###" : "0.####", Loc.Culture))
        : Loc.Get("Architecture.Scale.NotCalibrated");

    public string CalibrationButtonText => Loc.Get(_current?.Calibration is null ? "Architecture.Scale.Calibrate" : "Architecture.Scale.Recalibrate");

    /// <summary>
    /// Takes two points marked on the preview and the distance between them in the real drawing. The size is kept for the
    /// picture as it was chosen, so an upscale afterwards doesn't change it.
    /// </summary>
    /// <returns>False when the points or the distance mean nothing.</returns>
    public bool ApplyCalibration(double x1, double y1, double x2, double y2, double lengthMillimeters)
    {
        if (_current is not { Analysis: { } analysis } item || ScaleCalibration.MillimetersPerPixel(x1, y1, x2, y2, lengthMillimeters) is not { } perAnalysisPixel)
        {
            return false;
        }

        // An analysis pixel stands for Reduction pixels of the picture; the picture may be an enlarged copy of the one chosen.
        item.Calibration = perAnalysisPixel / analysis.Reduction * item.UpscaleFactor;
        OnPropertyChanged(nameof(CalibrationCaption));
        OnPropertyChanged(nameof(CalibrationButtonText));
        return true;
    }

    private static double MillimetersPerAnalysisPixel(ToCadItem item, DrawingAnalysis analysis) =>
        item.Calibration is { } perPicturePixel
            ? perPicturePixel / item.UpscaleFactor * analysis.Reduction
            : ArchitectureCadBuilder.MillimetersPerPixelFromDpi(analysis);

    // ---- Converting ----

    /// <summary>One file to write: one item, or (combined) every item of one PDF in page order.</summary>
    private sealed record PlannedResult(SourceFile File, int? NamePage, IReadOnlyList<ToCadItem> Parts);

    private List<PlannedResult> PlanResults()
    {
        var results = new List<PlannedResult>();
        foreach (var file in _files)
        {
            var parts = Items.Where(i => i.File == file).ToList();
            if (parts.Count == 0)
            {
                continue;
            }

            if (Combines && file.Extension == ".pdf" && parts.Count > 1)
            {
                results.Add(new PlannedResult(file, null, parts));
            }
            else
            {
                results.AddRange(parts.Select(p => new PlannedResult(file, file.PageCount > 1 ? p.Page : null, [p])));
            }
        }

        return results;
    }

    /// <summary>How many files the conversion will save.</summary>
    public int ResultCount => PlanResults().Count;

    private static bool IsConvertible(ToCadItem item) =>
        item.Status == QueueItemStatus.Ready && item.Banner != ToCadBanner.NothingFound && !item.HasNothingToConvert;

    private bool CanConvert() =>
        Items.Count > 0
        && !Run.IsActive
        && Phase == ArchitecturePhase.Review
        && Banner is not (ToCadBanner.Upscaling or ToCadBanner.None)
        && !IsOverLimit
        && !HasNoPages
        && (!ShowsPageChoice || Pages.IsValid)
        && Items.All(IsConvertible)
        && (!Items.Any(i => i.Kind == CadSourceKind.VectorPdf) || Pdf.HasValidScale);

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        var results = PlanResults();
        if (results.Count == 0)
        {
            return;
        }

        var extension = TargetExtension;
        var format = $"{FileExtension.ToCode(_files[0].Extension)} → {TargetCode}";
        Message = null;
        if (results.Count == 1)
        {
            var only = results[0];
            var suggested = Path.GetFileNameWithoutExtension(OutputNames.BaseName(Request(only, extension), string.Empty));
            var destination = await _desktop.PickDestinationAsync(suggested, Path.GetDirectoryName(only.File.Path), TargetCode, extension);
            if (destination is null)
            {
                return;
            }

            // A name typed with another extension still gets the real one, so the file is never mislabeled.
            if (FileExtension.FromPath(destination) != extension)
            {
                destination += extension;
            }

            var (pipeline, options) = Build(only, extension);
            await Run.RunAsync(pipeline, new ConversionJob(only.File.Path, extension, destination, options), format, TargetCode, ResultName(only));
            return;
        }

        var folder = await _desktop.PickFolderAsync(Path.GetDirectoryName(_files[0].Path));
        if (folder is null)
        {
            return;
        }

        IReadOnlyList<string> names;
        try
        {
            names = OutputNames.Plan([.. results.Select(r => Request(r, extension))], folder, string.Empty, path => File.Exists(path) || Directory.Exists(path));
        }
        catch (IOException)
        {
            ShowMessage(Loc.Get("Error.Io"), InfoBarSeverity.Warning);
            return;
        }

        var entries = results
            .Select((r, i) =>
            {
                var (pipeline, options) = Build(r, extension);
                return new BatchEntry(new BatchJob(new ConversionJob(r.File.Path, extension, names[i], options), pipeline), ResultName(r));
            })
            .ToList();
        await Run.RunBatchAsync(entries, folder, format, TargetCode);
    }

    private static OutputRequest Request(PlannedResult result, string extension) =>
        new(result.File.Path, result.NamePage, result.File.PageCount ?? 1, extension);

    /// <summary>"gambar.pdf, halaman 2" for one page, the file name for a whole file or a combined drawing.</summary>
    private static string ResultName(PlannedResult result) => result.Parts.Count == 1 ? result.Parts[0].Name : result.File.Name;

    /// <summary>The pipeline and options that make one result.</summary>
    private (ConversionPipeline Pipeline, ConversionOptions? Options) Build(PlannedResult result, string extension)
    {
        if (result.Parts.Count > 1)
        {
            return (_architecturePipeline, new CombinedCadOptions([.. result.Parts.Select(p => PartOptions(p, extension))]));
        }

        var item = result.Parts[0];
        return item.Kind switch
        {
            CadSourceKind.Raster => (_architecturePipeline, PartOptions(item, extension)),
            CadSourceKind.VectorPdf => (_pipeline, PartOptions(item, extension)),
            _ => (_pipeline, null),
        };
    }

    private ConversionOptions PartOptions(ToCadItem item, string extension)
    {
        if (item.Kind == CadSourceKind.Raster && item.Analysis is { } analysis)
        {
            return new ArchitectureCadOptions(analysis, item.Included.ToHashSet(), MillimetersPerAnalysisPixel(item, analysis));
        }

        var shared = Pdf.BuildOptions(extension) as CadOptions ?? new CadOptions(1);
        return shared with { PageNumber = item.Page ?? 1 };
    }

    // ---- Starting over ----

    /// <summary>Forgets every file and goes back to the drop area.</summary>
    public void Reset()
    {
        _stopRequested = false;
        _readWork?.Cancel();
        _upscaleWork?.Cancel();
        _files.Clear();
        _rebuilding = true;
        Items.Clear();
        _rebuilding = false;
        _current = null;
        CurrentIndex = -1;
        _pdfOptionsLoaded = false;
        OverLimitCount = 0;
        HasNoPages = false;
        OutputModeIndex = 0;
        Pages.Reset();
        Run.Reset();
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();
        UpscaleChoices.Clear();
        Preview = null;
        Banner = ToCadBanner.None;
        ReadingPercent = 0;
        RaiseSource();
        RaisePhase();
        RaiseRows();
        RaiseBanner();
        RaiseQueue();
    }
}

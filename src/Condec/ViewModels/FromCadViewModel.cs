// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Architecture;
using Condec.Core.Batch;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Pipeline;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

/// <summary>One layer of the drawing in the "Layer" list.</summary>
public sealed partial class LayerRow : ObservableObject
{
    public LayerRow(string name, bool isShown)
    {
        Name = name;
        DisplayName = CadNames.Layer(name);
        IsShown = isShown;
    }

    /// <summary>The name the file gives the layer.</summary>
    public string Name { get; }

    /// <summary>The name shown: Condec's own layers are named in the app's language.</summary>
    public string DisplayName { get; }

    /// <summary>Whether the layer is drawn. Kept in step with the list's selection by the view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(AutomationName))]
    public partial bool IsShown { get; set; }

    public string Status => Loc.Get(IsShown ? "Architecture.Layer.Shown" : "Architecture.Layer.Hidden");

    public string AutomationName => Loc.Format("Architecture.Layer.Spoken", DisplayName, Status);
}

/// <summary>
/// Architecture, "DWG, DXF → Gambar, PDF" (DESIGN §6.3.2, §6.3.4): one or more drawings become a queue. Each is read in the
/// background and drawn with Condec's own renderer, so its layers can be chosen and its preview checked; paper, resolution and
/// background are shared. Everything runs on this device.
/// </summary>
public sealed partial class FromCadViewModel : ObservableObject
{
    /// <summary>The most drawings the queue holds: each read drawing stays in memory. `[ASUMSI]`</summary>
    public const int MaximumItems = ToCadViewModel.MaximumItems;

    private static readonly string[] SourceExtensions = [".dwg", ".dxf"];

    private static readonly int[] DpiValues = [150, 300, 600];

    private readonly ConversionPipeline _pipeline;
    private readonly IDesktopServices _desktop;
    private readonly ActivityLog _log;
    private CancellationTokenSource? _readWork;
    private CancellationTokenSource? _previewWork;
    private bool _stopRequested;
    private bool _rebuilding;
    private FromCadItem? _current;

    /// <param name="pipeline">The pipeline whose registry holds <see cref="CadRenderConverter"/>.</param>
    public FromCadViewModel(ConversionPipeline pipeline, ConversionRun run, IDesktopServices desktop, ActivityLog log)
    {
        _pipeline = pipeline;
        Run = run;
        _desktop = desktop;
        _log = log;

        Layers.CollectionChanged += (_, _) => RaiseLayers();
        Run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionRun.IsActive))
            {
                RaisePhase();
                RaiseQueue();
            }
        };
        Run.Cancelled += (_, _) => ShowMessage(Loc.Get("Input.Cancelled"), InfoBarSeverity.Informational);
        Run.AnotherRequested += (_, _) => Reset();
    }

    /// <summary>The progress list, the finished card and the failed card, for one result or a batch.</summary>
    public ConversionRun Run { get; }

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

    // ---- Messages ----

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

    public ObservableCollection<FromCadItem> Items { get; } = [];

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

    /// <summary>The queue bar: more than one drawing.</summary>
    public bool ShowsQueue => Items.Count > 1 && !Run.IsActive;

    private bool CanNavigate => !Run.IsActive;

    private bool CanGoPrevious() => CanNavigate && CurrentIndex > 0;

    private bool CanGoNext() => CanNavigate && CurrentIndex >= 0 && CurrentIndex < Items.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => CurrentIndex--;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => CurrentIndex++;

    private bool CanRemove() => CanNavigate && Items.Count > 1;

    /// <summary>"Hapus dari antrean": the drawing shown leaves the queue.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveCurrent() => RemoveItem(_current);

    /// <summary>The failed card's button: also works when it is the only drawing.</summary>
    [RelayCommand]
    private void RemoveFailed() => RemoveItem(_current);

    private void RemoveItem(FromCadItem? item)
    {
        var index = item is null ? -1 : Items.IndexOf(item);
        if (index < 0 || Run.IsActive)
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
        Renumber();
        _rebuilding = false;
        Show(Items[Math.Min(index, Items.Count - 1)]);
    }

    private void Renumber()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Number = i + 1;
        }
    }

    /// <summary>"3 item · 2 sudah ditinjau · 1 masih dibaca", or why the button waits.</summary>
    public string QueueStatusText
    {
        get
        {
            if (Items.Count == 0)
            {
                return string.Empty;
            }

            if (Items.FirstOrDefault(i => i.Status == QueueItemStatus.Failed) is { } failed)
            {
                return Loc.Format("Queue.Blocked.Failed", failed.Number);
            }

            if (Items.FirstOrDefault(i => i.HasNothingShown) is { } empty)
            {
                return Loc.Format("Queue.Blocked.NoLayer", empty.Number);
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
        OnPropertyChanged(nameof(QueueStatusText));
        OnPropertyChanged(nameof(ConvertLabel));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        RemoveCurrentCommand.NotifyCanExecuteChanged();
        PickSourceCommand.NotifyCanExecuteChanged();
        AddSourcesCommand.NotifyCanExecuteChanged();
        ConvertCommand.NotifyCanExecuteChanged();
    }

    // ---- Reading ----

    public string ReadingTitle => _current is null ? string.Empty : Loc.Format("Architecture.FromCad.ReadingTitle", _current.Name);

    /// <summary>"Hentikan": the drawings not read yet leave the queue; what was read stays.</summary>
    [RelayCommand]
    private void CancelReading()
    {
        _stopRequested = true;
        _readWork?.Cancel();
    }

    public string FailedItemMessage => _current?.FailMessage ?? string.Empty;

    // ---- Choosing files ----

    private bool CanPick() => !Run.IsActive;

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

    /// <summary>"Tambah file…": more drawings at the end of the queue.</summary>
    [RelayCommand(CanExecute = nameof(CanPick))]
    private async Task AddSourcesAsync()
    {
        var folder = Items.Count > 0 ? Path.GetDirectoryName(Items[0].File.Path) : null;
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
        if (folderCount > 0 && Items.Count > 0)
        {
            ShowMessage(Loc.Get("Input.FoldersSkipped"), InfoBarSeverity.Informational);
        }
    }

    /// <summary>Takes the DWG and DXF files among <paramref name="paths"/>, and says which were left out and why.</summary>
    public Task SelectSourcesAsync(IReadOnlyList<string> paths, bool add)
    {
        if (Run.IsActive)
        {
            return Task.CompletedTask;
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
                    ? Loc.Get("Architecture.FromCad.UnsupportedNoExtension")
                    : Loc.Format("Architecture.FromCad.Unsupported", FileExtension.ToCode(extension)),
                InfoBarSeverity.Warning);
            return Task.CompletedTask;
        }

        var taken = new List<SourceFile>();
        var skipped = new List<string>();
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var extension = FileExtension.FromPath(path);
            if (!SourceExtensions.Contains(extension))
            {
                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get("Skip.Unsupported")));
                continue;
            }

            if (Items.Select(i => i.File).Concat(taken).Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                taken.Add(new SourceFile(path, extension, new FileInfo(path).Length));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (paths.Count == 1)
                {
                    ShowMessage(Loc.Get("Input.CannotOpen"), InfoBarSeverity.Warning);
                    return Task.CompletedTask;
                }

                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get("Skip.Unreadable")));
            }
        }

        if (taken.Count == 0)
        {
            if (skipped.Count > 0)
            {
                ShowMessage(Loc.Format("Input.NoneUsable", string.Join(", ", skipped)), InfoBarSeverity.Warning);
            }

            return Task.CompletedTask;
        }

        Message = null;
        if (skipped.Count > 0)
        {
            ShowMessage(Loc.Format("Input.SkippedSome", string.Join(", ", skipped)), InfoBarSeverity.Warning);
        }

        // Drawings past the limit are not taken at all: unlike PDF pages, there is no page choice to shorten the queue with.
        var room = Math.Max(0, MaximumItems - Items.Count);
        _rebuilding = true;
        foreach (var file in taken.Take(room))
        {
            Items.Add(new FromCadItem(file));
        }

        Renumber();
        _rebuilding = false;
        _log.Info($"Drawing queue: {Math.Min(taken.Count, room)} files added");
        if (taken.Count > room)
        {
            ShowMessage(Loc.Format("Queue.OverLimitDropped", MaximumItems, taken.Count - room), InfoBarSeverity.Warning);
        }

        if (Items.Count == 0)
        {
            return Task.CompletedTask;
        }

        Show(_current ?? Items[0]);
        _ = ReadQueueAsync();
        return Task.CompletedTask;
    }

    /// <summary>Reads the drawings one after another, the one shown first. Runs until nothing is left to read.</summary>
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
                await ReadItemAsync(item, work.Token);
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

    private FromCadItem? NextToRead() =>
        _current is { Status: QueueItemStatus.Waiting } current && Items.Contains(current)
            ? current
            : Items.FirstOrDefault(i => i.Status == QueueItemStatus.Waiting);

    /// <summary>"Hentikan": what was read stays in the queue, the rest leaves it.</summary>
    private void OnReadingStopped()
    {
        _stopRequested = false;
        var unread = Items.Where(i => !i.IsSettled).ToList();
        _log.Info($"Reading drawings stopped: {unread.Count} left out");
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

        Renumber();
        _rebuilding = false;
        Show(Items[0]);
        ShowMessage(Loc.Get("Architecture.Reading.CancelledSome"), InfoBarSeverity.Informational);
    }

    private async Task ReadItemAsync(FromCadItem item, CancellationToken ct)
    {
        item.Status = QueueItemStatus.Reading;
        RaiseIfShown(item);
        try
        {
            var scene = await ArchitectureFiles.ReadDrawingAsync(item.File.Path, ct);
            var layers = LayersOf(scene);
            if (layers.Count == 0)
            {
                item.FailMessage = Loc.Get("Architecture.FromCad.Empty");
                item.Status = QueueItemStatus.Failed;
            }
            else
            {
                item.Scene = scene;
                item.LayerNames = layers;
                foreach (var (name, isOn) in layers.Where(l => !l.IsOn))
                {
                    item.Hidden.Add(name);
                }

                // The first picture is drawn before the drawing shows, so its card is not empty when it appears.
                await DrawPreviewAsync(item, Paper, ct);
                item.Status = QueueItemStatus.Ready;
                _log.Info($"Drawing read: {layers.Count} layers, unit {scene.Units}");
            }
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

    private void RaiseIfShown(FromCadItem item)
    {
        if (ReferenceEquals(item, _current))
        {
            RaisePhase();
        }
    }

    /// <summary>The layers that have something to draw, in the order of the file's layer table, each as the file has it.</summary>
    private static List<(string Name, bool IsOn)> LayersOf(CadScene scene)
    {
        var used = scene.Paths.Select(p => p.Layer).Concat(scene.Labels.Select(l => l.Layer)).ToHashSet();
        var rows = scene.Layers.Where(l => used.Contains(l.Name)).Select(l => (l.Name, l.IsOn)).ToList();

        // A shape on a layer the table doesn't list is still drawn by the file's own viewer.
        var listed = rows.Select(r => r.Name).ToHashSet();
        rows.AddRange(used.Where(name => !listed.Contains(name)).Order(StringComparer.Ordinal).Select(name => (name, true)));
        return rows;
    }

    // ---- The drawing shown ----

    /// <summary>Puts <paramref name="item"/> on screen: its layers and its preview, drawn again if the paper changed.</summary>
    private void Show(FromCadItem item)
    {
        _previewWork?.Cancel();
        _previewWork = null;
        IsPreviewWorking = false;
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

        LoadLayers(item);
        Preview = item.Preview;
        PreviewMessage = item.PreviewMessage;
        if (item.Status == QueueItemStatus.Ready && item.PreviewPaper != Paper)
        {
            SchedulePreview();
        }

        RaiseSource();
        RaisePhase();
        RaiseLayers();
        OnPropertyChanged(nameof(ReadingTitle));
        RaiseQueue();
    }

    /// <summary>The rows of the drawing shown, ticked as its review left them. Rows are made anew for each drawing.</summary>
    private void LoadLayers(FromCadItem item)
    {
        foreach (var row in Layers)
        {
            row.PropertyChanged -= OnLayerChanged;
        }

        Layers.Clear();
        foreach (var (name, _) in item.LayerNames)
        {
            var row = new LayerRow(name, !item.Hidden.Contains(name));
            row.PropertyChanged += OnLayerChanged;
            Layers.Add(row);
        }
    }

    public string SourceName => _current?.Name ?? string.Empty;

    public string ReadyTitle => Loc.Get(_current?.File.Extension == ".dxf" ? "Architecture.FromCad.ReadyDxf" : "Architecture.FromCad.ReadyDwg");

    public string ReadyMessage => Layers.Count == 1
        ? Loc.Format("Architecture.FromCad.ReadyMessageOne", Unit)
        : Loc.Format("Architecture.FromCad.ReadyMessage", Layers.Count, Unit);

    private string Unit => _current?.Scene is { } scene ? CadNames.Unit(scene.Units) : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(HasNoPreview), nameof(PreviewAlt))]
    public partial PreviewImage? Preview { get; set; }

    public bool HasPreview => Preview is not null;

    public bool HasNoPreview => Preview is null;

    public string PreviewAlt => Loc.Format("Architecture.Preview.Alt", SourceName);

    /// <summary>The preview is being drawn again after a change.</summary>
    [ObservableProperty]
    public partial bool IsPreviewWorking { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewMessage))]
    public partial string? PreviewMessage { get; set; }

    public bool HasPreviewMessage => PreviewMessage is not null;

    public ObservableCollection<LayerRow> Layers { get; } = [];

    public bool HasShownLayers => Layers.Any(l => l.IsShown);

    public bool ShowsNoLayerNote => Layers.Count > 0 && !HasShownLayers;

    private void OnLayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerRow.IsShown) && sender is LayerRow row && _current is { } item)
        {
            if (row.IsShown)
            {
                item.Hidden.Remove(row.Name);
            }
            else
            {
                item.Hidden.Add(row.Name);
            }

            item.RaiseStatus();
            RaiseLayers();
            RaiseQueue();
            SchedulePreview();
        }
    }

    private void RaiseLayers()
    {
        OnPropertyChanged(nameof(HasShownLayers));
        OnPropertyChanged(nameof(ShowsNoLayerNote));
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(ReadyMessage));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    private void RaiseSource()
    {
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(ReadyTitle));
        OnPropertyChanged(nameof(ReadyMessage));
        OnPropertyChanged(nameof(PreviewAlt));
        OnPropertyChanged(nameof(EstimateText));
    }

    // ---- Options (shared by every drawing in the queue) ----

    /// <summary>0 PDF, 1 PNG, 2 JPG.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(IsPdf), nameof(IsPng), nameof(DpiEnabled), nameof(TransparentEnabled), nameof(EstimateText))]
    public partial int FormatIndex { get; set; }

    /// <summary>0 A4, 1 A3.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Paper), nameof(EstimateText))]
    public partial int PaperIndex { get; set; } = 1;

    /// <summary>0 150, 1 300, 2 600 dpi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dpi), nameof(EstimateText))]
    public partial int DpiIndex { get; set; } = 1;

    /// <summary>0 white, 1 transparent.</summary>
    [ObservableProperty]
    public partial int BackgroundIndex { get; set; }

    public CadResultFormat Format => FormatIndex switch
    {
        1 => CadResultFormat.Png,
        2 => CadResultFormat.Jpg,
        _ => CadResultFormat.Pdf,
    };

    public bool IsPdf => Format == CadResultFormat.Pdf;

    public bool IsPng => Format == CadResultFormat.Png;

    /// <summary>A PDF is vector: it has no resolution.</summary>
    public bool DpiEnabled => !IsPdf;

    /// <summary>Only a PNG can be transparent.</summary>
    public bool TransparentEnabled => IsPng;

    public PaperSize Paper => PaperIndex == 0 ? PaperSize.A4 : PaperSize.A3;

    public int Dpi => DpiValues[Math.Clamp(DpiIndex, 0, DpiValues.Length - 1)];

    private string TargetExtension => Format switch
    {
        CadResultFormat.Png => ".png",
        CadResultFormat.Jpg => ".jpg",
        _ => ".pdf",
    };

    private string TargetCode => FileExtension.ToCode(TargetExtension);

    partial void OnFormatIndexChanged(int value)
    {
        // A background that this format can't have goes back to white, so what is shown is what is made.
        if (value != 1 && BackgroundIndex == 1)
        {
            BackgroundIndex = 0;
        }
    }

    partial void OnPaperIndexChanged(int value) => SchedulePreview();

    /// <summary>"Perkiraan hasil: 4961 × 3508 px · ± 1,7 MB", for the drawing shown.</summary>
    public string EstimateText
    {
        get
        {
            if (_current?.Scene is not { } scene || !HasShownLayers)
            {
                return string.Empty;
            }

            if (IsPdf)
            {
                return Loc.Format("Architecture.Estimate", Loc.Format("Architecture.Estimate.Pdf", DisplayFormat.FormatFileSize(CadResultEstimate.PdfBytes, Loc.Culture)));
            }

            var landscape = IsLandscape(scene, _current.Hidden);
            var (width, height) = CadResultEstimate.Pixels(Paper, Dpi, landscape);
            var bytes = CadResultEstimate.Bytes(Format, width, height);
            return Loc.Format("Architecture.Estimate", Loc.Format("Architecture.Estimate.Picture", width, height, DisplayFormat.FormatFileSize(bytes, Loc.Culture)));
        }
    }

    /// <summary>The sheet is used the way round the drawing fills it better, the same choice <see cref="CadPdfWriter"/> makes.</summary>
    private static bool IsLandscape(CadScene scene, IReadOnlySet<string> hidden) =>
        scene.BoundsOf(name => !hidden.Contains(name)) is not { } bounds || bounds.MaxX - bounds.MinX >= bounds.MaxY - bounds.MinY;

    // ---- The preview ----

    private void SchedulePreview()
    {
        _previewWork?.Cancel();
        if (_current is not { Status: QueueItemStatus.Ready } item)
        {
            return;
        }

        var work = new CancellationTokenSource();
        _previewWork = work;
        _ = DrawPreviewAfterPauseAsync(item, work);
    }

    /// <summary>Waits a moment so a run of clicks on the layer list draws once, then draws.</summary>
    private async Task DrawPreviewAfterPauseAsync(FromCadItem item, CancellationTokenSource work)
    {
        IsPreviewWorking = true;
        try
        {
            await Task.Delay(200, work.Token);
            await DrawPreviewAsync(item, Paper, work.Token);
            if (ReferenceEquals(item, _current))
            {
                Preview = item.Preview;
                PreviewMessage = item.PreviewMessage;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer change, or another drawing, has taken over.
        }
        finally
        {
            if (ReferenceEquals(_previewWork, work))
            {
                _previewWork = null;
                IsPreviewWorking = false;
            }
        }
    }

    /// <summary>Draws <paramref name="item"/> on <paramref name="paper"/> without its hidden layers, into the item.</summary>
    private async Task DrawPreviewAsync(FromCadItem item, PaperSize paper, CancellationToken ct)
    {
        if (item.Scene is not { } scene)
        {
            return;
        }

        var hidden = item.Hidden.ToHashSet();
        if (item.LayerNames.All(l => hidden.Contains(l.Name)))
        {
            item.Preview = null;
            item.PreviewMessage = null;
            item.PreviewPaper = paper;
            return;
        }

        try
        {
            var image = await ArchitectureFiles.RenderDrawingAsync(scene, name => !hidden.Contains(name), paper, ct);
            ct.ThrowIfCancellationRequested();
            item.Preview = image;
            item.PreviewMessage = null;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // The file can still be converted: the same drawing is made again from the file itself.
            item.Preview = null;
            item.PreviewMessage = Loc.Get("Architecture.FromCad.PreviewFailed");
            _log.Info($"No preview: {ex.GetType().Name}");
        }

        item.PreviewPaper = paper;
    }

    // ---- Converting ----

    /// <summary>"Konversi dan simpan…" for one drawing, "Konversi dan simpan N file…" for more.</summary>
    public string ConvertLabel => Items.Count > 1 ? Loc.Format("Convert.LabelMany", Items.Count) : Loc.Get("Architecture.ConvertAndSave");

    private bool CanConvert() =>
        Items.Count > 0
        && !Run.IsActive
        && Phase == ArchitecturePhase.Review
        && Items.All(i => i.Status == QueueItemStatus.Ready && !i.HasNothingShown);

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        var extension = TargetExtension;
        var format = $"{string.Join(", ", Items.Select(i => FileExtension.ToCode(i.File.Extension)).Distinct())} → {TargetCode}";
        Message = null;
        if (Items.Count == 1)
        {
            var only = Items[0];
            var destination = await _desktop.PickDestinationAsync(Path.GetFileNameWithoutExtension(only.File.Path), Path.GetDirectoryName(only.File.Path), TargetCode, extension);
            if (destination is null)
            {
                return;
            }

            // A name typed with another extension still gets the real one, so the file is never mislabeled.
            if (FileExtension.FromPath(destination) != extension)
            {
                destination += extension;
            }

            await Run.RunAsync(_pipeline, new ConversionJob(only.File.Path, extension, destination, OptionsFor(only)), format, TargetCode);
            return;
        }

        var folder = await _desktop.PickFolderAsync(Path.GetDirectoryName(Items[0].File.Path));
        if (folder is null)
        {
            return;
        }

        IReadOnlyList<string> names;
        try
        {
            names = OutputNames.Plan([.. Items.Select(i => new OutputRequest(i.File.Path, null, 1, extension))], folder, string.Empty, path => File.Exists(path) || Directory.Exists(path));
        }
        catch (IOException)
        {
            ShowMessage(Loc.Get("Error.Io"), InfoBarSeverity.Warning);
            return;
        }

        var entries = Items
            .Select((item, i) => new BatchEntry(new BatchJob(new ConversionJob(item.File.Path, extension, names[i], OptionsFor(item)), _pipeline), item.Name))
            .ToList();
        await Run.RunBatchAsync(entries, folder, format, TargetCode);
    }

    private CadRenderOptions OptionsFor(FromCadItem item) => new(Paper, Dpi, IsPng && BackgroundIndex == 1, item.Hidden.ToHashSet());

    // ---- Starting over ----

    /// <summary>Forgets every drawing and goes back to the drop area.</summary>
    public void Reset()
    {
        _stopRequested = false;
        _readWork?.Cancel();
        _previewWork?.Cancel();
        _previewWork = null;
        foreach (var row in Layers)
        {
            row.PropertyChanged -= OnLayerChanged;
        }

        _rebuilding = true;
        Items.Clear();
        _rebuilding = false;
        _current = null;
        CurrentIndex = -1;
        Run.Reset();
        Layers.Clear();
        Preview = null;
        PreviewMessage = null;
        IsPreviewWorking = false;
        RaiseSource();
        RaisePhase();
        RaiseLayers();
        RaiseQueue();
    }
}

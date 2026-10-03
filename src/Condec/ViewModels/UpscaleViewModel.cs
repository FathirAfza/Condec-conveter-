// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Batch;
using Condec.Core.Conversion;
using Condec.Core.Devices;
using Condec.Core.Formats;
using Condec.Core.History;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Pipeline;
using Condec.Core.Settings;
using Condec.Core.Upscale;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

/// <summary>One scale in the "Skala" list: 2×, 4×, 8× or 16×. A scale above the limit stays in the list, captioned "Dikunci".</summary>
public sealed record ScaleChoice(int Scale, bool IsEnabled)
{
    public string Label => ScaleText.Format(Scale);

    public string Caption => IsEnabled ? ScaleText.Percent(Scale) : Loc.Get("Upscale.ScaleLocked");

    public override string ToString() => IsEnabled ? $"{Label} ({ScaleText.Percent(Scale)})" : $"{Label}, {Loc.Get("Upscale.ScaleLocked")}";
}

/// <summary>One named resolution in the "Resolusi hasil" list.</summary>
public sealed record ResolutionChoice(ResolutionPreset Preset, int Width, int Height, bool IsEnabled)
{
    public string Name => Loc.Get(Preset.NameKey);

    public string Dimensions => ScaleText.Dimensions(Width, Height);

    public string Caption => IsEnabled ? Dimensions : Loc.Get("Format.Unavailable");

    public override string ToString() => IsEnabled ? $"{Name} · {Dimensions}" : Loc.Format("Format.UnavailableSpoken", Name);
}

/// <summary>One picture of an Upscale batch: its job, the scale it was chosen at, and its size, for the cards and the history.</summary>
public sealed record UpscaleBatchEntry(ConversionJob Job, double Scale, string SourceName, int Width, int Height);

/// <summary>
/// Upscale Image (DESIGN §6.2): the pictures, the size of each result, the estimate, and the upscale itself. Several pictures
/// form a queue reviewed one by one, then upscaled one after another into a chosen folder.
/// </summary>
public sealed partial class UpscaleViewModel : ObservableObject
{
    /// <summary>The same limit as the Architecture queue (DESIGN §13 #45).</summary>
    public const int MaximumItems = ToCadViewModel.MaximumItems;

    private const double MinimumPercent = CapabilityPolicy.MinimumScale * 100;
    private const double PercentStep = 50;

    private readonly AppSettings _settings;
    private readonly ConverterRegistry _registry;
    private readonly ConversionPipeline _pipeline;
    private readonly EngineBenchmark _benchmark;
    private readonly HistoryStore _history;
    private readonly IDesktopServices _desktop;
    private readonly ActivityLog _log;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private CancellationTokenSource? _cancellation;
    private ConversionJob? _lastJob;
    private PipelineStage _lastStage;
    private RenderEngine _lastEngine;
    private double _lastScale;
    private bool _syncing;
    private ScaleLimit? _limit;
    private UpscaleItem? _current;
    private bool _rebuilding;
    private List<UpscaleBatchEntry> _batch = [];
    private int _batchCount;
    private int _batchIndex = -1;

    public UpscaleViewModel(AppSettings settings, ConverterRegistry registry, ConversionPipeline pipeline, HistoryStore history, IDesktopServices desktop, ActivityLog log)
    {
        _settings = settings;
        _registry = registry;
        _pipeline = pipeline;
        _history = history;
        _desktop = desktop;
        _log = log;
        _benchmark = new EngineBenchmark(settings);
        _settings.Changed += (_, _) => OnUi(OnSettingsChanged);
    }

    /// <summary>Raised by "Ubah di Settings"; the window switches pages.</summary>
    public event EventHandler? SettingsRequested;

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

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

    // ---- The model ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasModelProblem), nameof(IsUnavailable), nameof(CanChoose))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string? ModelProblem { get; set; }

    public bool HasModelProblem => ModelProblem is not null;

    /// <summary>Checks once that the bundled model is there and intact; the first check reads the whole file.</summary>
    public async Task InitializeAsync()
    {
        var status = await Task.Run(UpscaleModelLocator.GetStatus);
        ModelProblem = status switch
        {
            UpscaleModelStatus.Missing => Loc.Get("Error.UpscaleModelMissing"),
            UpscaleModelStatus.Damaged => Loc.Get("Error.UpscaleModelDamaged"),
            _ => null,
        };
        if (status != UpscaleModelStatus.Ready)
        {
            _log.Error($"The upscale model is {status}");
        }
    }

    // ---- State ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInput), nameof(IsProcessing), nameof(IsDone), nameof(IsFailed), nameof(IsSingleDone), nameof(IsBatchDone))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(PreviousCommand), nameof(NextCommand), nameof(RemoveCurrentCommand))]
    public partial ConverterState State { get; set; }

    public bool IsInput => State == ConverterState.Input;

    public bool IsProcessing => State == ConverterState.Processing;

    public bool IsDone => State == ConverterState.Done;

    public bool IsFailed => State == ConverterState.Failed;

    // ---- The picture ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource), nameof(HasNoSource), nameof(SourceCaption), nameof(ProcessingTitle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial SourceFile? Source { get; set; }

    public bool HasSource => Source is not null;

    public bool HasNoSource => Source is null;

    public int SourceWidth { get; private set; }

    public int SourceHeight { get; private set; }

    /// <summary>"1280 × 720 · 0,9 MP · 245 KB".</summary>
    public string SourceCaption => Source is null
        ? string.Empty
        : $"{ScaleText.Dimensions(SourceWidth, SourceHeight)} · {ScaleText.Megapixels((long)SourceWidth * SourceHeight, Loc.Culture)} · {DisplayFormat.FormatFileSize(Source.Size)}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInputMessage))]
    public partial string? InputMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity InputMessageSeverity { get; set; }

    public bool HasInputMessage => InputMessage is not null;

    [RelayCommand]
    private void DismissInputMessage() => InputMessage = null;

    private void ShowInputMessage(string message, InfoBarSeverity severity)
    {
        InputMessageSeverity = severity;
        InputMessage = message;
    }

    /// <summary>"Pilih gambar…" and "Ganti": one or more pictures, which start a new queue.</summary>
    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var paths = await _desktop.PickSourceFilesAsync(SourceExtensions());
        if (paths.Count > 0)
        {
            await SelectSourcesAsync(paths, add: false);
        }
    }

    /// <summary>"Tambah gambar…": more pictures at the end of the queue; the dialog opens in the folder of the first one.</summary>
    [RelayCommand]
    private async Task AddSourcesAsync()
    {
        var paths = await _desktop.PickSourceFilesAsync(SourceExtensions(), Items.Count > 0 ? Path.GetDirectoryName(Items[0].File.Path) : null);
        if (paths.Count > 0)
        {
            await SelectSourcesAsync(paths, add: true);
        }
    }

    private List<string> SourceExtensions() => [.. _registry.GetSourceExtensions()];

    private bool CanUpscale(string extension) => extension.Length > 0 && _registry.GetTargetOptions(extension).Count > 0;

    /// <summary>Drag and drop: one or more pictures start a new queue, as "Pilih gambar…" does.</summary>
    /// <param name="filePaths">An empty path is a file with no place on disk, such as one inside a ZIP.</param>
    public async Task SelectDroppedAsync(IReadOnlyList<string> filePaths, int folderCount)
    {
        if (filePaths.Count == 0)
        {
            if (folderCount > 0)
            {
                ShowInputMessage(Loc.Get("Input.DropFolder"), InfoBarSeverity.Warning);
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
            ShowInputMessage(Loc.Get("Input.FoldersSkipped"), InfoBarSeverity.Informational);
        }
    }

    public void ReportUnreadableDrop() =>
        ShowInputMessage(Loc.Get("Input.DropUnreadable"), InfoBarSeverity.Warning);

    /// <summary>Takes the pictures among <paramref name="paths"/> into the queue, and says which were left out and why.</summary>
    /// <param name="add">At the end of the queue ("Tambah gambar…"), or as a new queue.</param>
    private async Task SelectSourcesAsync(IReadOnlyList<string> paths, bool add)
    {
        if (State != ConverterState.Input)
        {
            return;
        }

        // One picture on its own keeps the messages that say exactly what is wrong with it.
        if (paths.Count == 1 && !add)
        {
            await SelectOneAsync(paths[0]);
            return;
        }

        var taken = new List<UpscaleItem>();
        var skipped = new List<string>();
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var extension = FileExtension.FromPath(path);
            if (!CanUpscale(extension))
            {
                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get("Skip.Unsupported")));
                continue;
            }

            if ((add ? Items : Enumerable.Empty<UpscaleItem>()).Select(i => i.File.Path).Concat(taken.Select(i => i.File.Path)).Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var (item, reason, _) = await ReadPictureAsync(path, extension);
            if (item is null)
            {
                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get(reason!)));
                continue;
            }

            taken.Add(item);
        }

        if (taken.Count == 0)
        {
            if (skipped.Count > 0)
            {
                ShowInputMessage(Loc.Format("Input.NoneUsable", string.Join(", ", skipped)), InfoBarSeverity.Warning);
            }

            return;
        }

        if (!add)
        {
            ClearQueue();
        }

        // Pictures past the limit are not taken at all; the message says how many.
        var room = Math.Max(0, MaximumItems - Items.Count);
        var notes = new List<string>();
        if (skipped.Count > 0)
        {
            notes.Add(Loc.Format("Input.SkippedSome", string.Join(", ", skipped)));
        }

        if (taken.Count > room)
        {
            notes.Add(Loc.Format("Queue.OverLimitDropped", MaximumItems, taken.Count - room));
        }

        InputMessage = null;
        if (notes.Count > 0)
        {
            ShowInputMessage(string.Join(" ", notes), InfoBarSeverity.Warning);
        }

        AddToQueue(taken.Take(room));
        _log.Info($"Upscale queue: {Math.Min(taken.Count, room)} pictures added");
    }

    /// <summary>One picture as a new queue, with the message of DESIGN §6.2 when it can't be taken.</summary>
    private async Task SelectOneAsync(string path)
    {
        var extension = FileExtension.FromPath(path);
        if (!CanUpscale(extension))
        {
            var supported = string.Join(", ", SourceExtensions().Select(e => FormatCatalog.GetTargetLabel(e)).Distinct());
            ShowInputMessage(
                extension.Length == 0 ? Loc.Format("Upscale.UnsupportedNoExtension", supported) : Loc.Format("Upscale.Unsupported", extension, supported),
                InfoBarSeverity.Warning);
            return;
        }

        var (item, _, message) = await ReadPictureAsync(path, extension);
        if (item is null)
        {
            ShowInputMessage(message!, InfoBarSeverity.Warning);
            return;
        }

        InputMessage = null;
        ClearQueue();
        AddToQueue([item]);
    }

    /// <summary>
    /// Reads the size of a picture from its header. A picture that can't be taken comes with the short reason of the "not
    /// taken" list (a resource key) and the full message said when it was chosen alone.
    /// </summary>
    private async Task<(UpscaleItem? Item, string? SkipReason, string? Message)> ReadPictureAsync(string path, string extension)
    {
        long size;
        ImageHeaderInfo header;
        try
        {
            size = new FileInfo(path).Length;
            header = await Task.Run(() => ImageHeader.ReadAsync(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "Skip.Unreadable", Loc.Get("Input.CannotOpen"));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Windows throws its own exception types for a file that is not a picture, or one that is damaged.
            _log.Info($"Upscale source not readable: {ex.GetType().Name}");
            return (null, "Skip.Unreadable", Loc.Get("Error.Decode"));
        }

        if (header.Pixels > UpscaleSupport.MaximumSourcePixels)
        {
            return (null, "Skip.TooLarge", Loc.Format("Error.ImageTooLarge", (header.Pixels + 500_000) / 1_000_000));
        }

        return (new UpscaleItem(new SourceFile(path, extension, size), (int)header.Width, (int)header.Height), null, null);
    }

    // ---- The queue ----

    /// <summary>The pictures, in the order their results are numbered. One picture is a queue of one, shown without the queue card.</summary>
    public ObservableCollection<UpscaleItem> Items { get; } = [];

    /// <summary>The picture shown, as the queue list's selected index.</summary>
    [ObservableProperty]
    public partial int CurrentIndex { get; set; } = -1;

    partial void OnCurrentIndexChanged(int value)
    {
        if (!_rebuilding && value >= 0 && value < Items.Count && !ReferenceEquals(Items[value], _current))
        {
            Show(Items[value]);
        }
    }

    /// <summary>The queue card shows with more than one picture.</summary>
    public bool ShowsQueue => Items.Count > 1;

    private bool CanPrevious() => State == ConverterState.Input && CurrentIndex > 0;

    private bool CanNext() => State == ConverterState.Input && CurrentIndex >= 0 && CurrentIndex < Items.Count - 1;

    private bool CanRemove() => State == ConverterState.Input && Items.Count > 1;

    [RelayCommand(CanExecute = nameof(CanPrevious))]
    private void Previous() => Show(Items[CurrentIndex - 1]);

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next() => Show(Items[CurrentIndex + 1]);

    /// <summary>"Hapus dari antrean": the picture shown leaves the queue.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveCurrent()
    {
        var index = _current is null ? -1 : Items.IndexOf(_current);
        if (index < 0 || Items.Count < 2 || State != ConverterState.Input)
        {
            return;
        }

        _rebuilding = true;
        Items.RemoveAt(index);
        Renumber();
        _rebuilding = false;
        Show(Items[Math.Min(index, Items.Count - 1)]);
    }

    /// <summary>New pictures get the scale a single picture would start with (DESIGN §6.2), and the first new one is shown.</summary>
    private void AddToQueue(IEnumerable<UpscaleItem> items)
    {
        UpscaleItem? first = null;
        _rebuilding = true;
        foreach (var item in items)
        {
            FitToLimit(item);
            Items.Add(item);
            first ??= item;
        }

        Renumber();
        _rebuilding = false;
        if (_current is not null && Items.Contains(_current))
        {
            RaiseQueue();
        }
        else if (first is not null)
        {
            Show(first);
        }
    }

    private void ClearQueue()
    {
        _rebuilding = true;
        Items.Clear();
        _rebuilding = false;
        _current = null;
        CurrentIndex = -1;
    }

    private void Renumber()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Number = i + 1;
        }
    }

    /// <summary>Shows a picture: its size, its limit, and the size chosen for it.</summary>
    private void Show(UpscaleItem item)
    {
        _current = item;
        item.Viewed = true;
        _rebuilding = true;
        CurrentIndex = Items.IndexOf(item);
        _rebuilding = false;
        SourceWidth = item.Width;
        SourceHeight = item.Height;
        Source = item.File;
        OnPropertyChanged(nameof(SourceCaption));
        _limit = null;
        Reload();
    }

    /// <summary>
    /// Keeps a picture's chosen size within what this device and Settings allow: a new picture gets the default scale, a scale
    /// above a lowered limit comes down to it, and a picture that can't be upscaled keeps its own size.
    /// </summary>
    private void FitToLimit(UpscaleItem item)
    {
        var limit = UpscalePlan.EffectiveLimit(_settings.Device, item.Width, item.Height, _settings.ScaleLimit);
        if (!limit.IsAvailable)
        {
            item.SetSize(1, item.Width, item.Height);
            return;
        }

        if (item.Scale >= CapabilityPolicy.MinimumScale && item.Scale <= limit.Effective + 1e-9)
        {
            return;
        }

        var scale = item.Scale < CapabilityPolicy.MinimumScale ? CapabilityPolicy.DefaultScale(limit) : limit.Effective;
        var (width, height) = UpscaleEstimator.OutputSize(item.Width, item.Height, scale);
        item.SetSize(scale, width, height);
    }

    /// <summary>Whether a picture can be upscaled at its chosen size, with the format and the Settings of now.</summary>
    private UpscaleBlock BlockOf(UpscaleItem item)
    {
        var limit = UpscalePlan.EffectiveLimit(_settings.Device, item.Width, item.Height, _settings.ScaleLimit);
        if (!limit.IsAvailable || item.Scale < CapabilityPolicy.MinimumScale)
        {
            return UpscaleBlock.TooBig;
        }

        var ramShort = _settings.Device.InstalledRamGb < CapabilityPolicy.RequiredRam((long)item.OutputWidth * item.OutputHeight).Gb;
        return ramShort || !MemoryPlanFor(item.Width, item.Height, item.OutputWidth, item.OutputHeight).Fits ? UpscaleBlock.Memory : UpscaleBlock.None;
    }

    /// <summary>"3 item · 2 sudah ditinjau", or why the queue can't be upscaled yet.</summary>
    public string QueueStatusText
    {
        get
        {
            var blocked = Items.FirstOrDefault(i => i.Block != UpscaleBlock.None);
            return blocked is null
                ? Loc.Format("Queue.Summary", Items.Count, Items.Count(i => i.Viewed))
                : Loc.Format(blocked.Block == UpscaleBlock.TooBig ? "Upscale.Queue.BlockedTooBig" : "Upscale.Queue.BlockedMemory", blocked.Number);
        }
    }

    /// <summary>"Upscale dan simpan…", or "Upscale dan simpan 3 gambar…" for a queue.</summary>
    public string StartLabel => Items.Count > 1 ? Loc.Format("Upscale.StartMany", Items.Count) : Loc.Get("Upscale.Start");

    /// <summary>The queue's statuses and buttons, after anything that can change them.</summary>
    private void RaiseQueue()
    {
        foreach (var item in Items)
        {
            item.Block = BlockOf(item);
        }

        OnPropertyChanged(nameof(ShowsQueue));
        OnPropertyChanged(nameof(QueueStatusText));
        OnPropertyChanged(nameof(StartLabel));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        RemoveCurrentCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
    }

    // ---- Limits and choices ----

    public ObservableCollection<ScaleChoice> ScaleChoices { get; } = [];

    public ObservableCollection<ResolutionChoice> ResolutionChoices { get; } = [];

    [ObservableProperty]
    public partial ScaleChoice? SelectedScaleChoice { get; set; }

    [ObservableProperty]
    public partial ResolutionChoice? SelectedResolution { get; set; }

    [ObservableProperty]
    public partial string ScalePlaceholder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResolutionPlaceholder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double SliderMinimum { get; set; } = MinimumPercent;

    [ObservableProperty]
    public partial double SliderMaximum { get; set; } = MinimumPercent;

    [ObservableProperty]
    public partial double SliderValue { get; set; } = MinimumPercent;

    public string SliderMinimumText => ScaleText.Percent(SliderMinimum / 100);

    public string SliderMaximumText => ScaleText.Percent(SliderMaximum / 100);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLockedText))]
    public partial string? LockedText { get; set; }

    public bool HasLockedText => LockedText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnavailable), nameof(CanChoose))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsLimitUnavailable { get; set; }

    [ObservableProperty]
    public partial string? UnavailableMessage { get; set; }

    /// <summary>Upscale can't run: the device or the model rules it out (an InfoBar says why).</summary>
    public bool IsUnavailable => IsLimitUnavailable || HasModelProblem;

    /// <summary>The options of the right card can be used.</summary>
    public bool CanChoose => HasSource && !IsUnavailable;

    public double Scale { get; private set; } = 1;

    public int OutputWidth { get; private set; }

    public int OutputHeight { get; private set; }

    private void OnSettingsChanged()
    {
        // The pictures not shown are held to the new limit too, so the queue never upscales beyond it.
        foreach (var item in Items)
        {
            if (!ReferenceEquals(item, _current))
            {
                FitToLimit(item);
            }
        }

        if (Source is null)
        {
            RaiseEngine();
            return;
        }

        Reload();
    }

    /// <summary>
    /// Reads the limit again (the picture shown, or Settings, changed) and puts every choice back in step with it. The size
    /// chosen for the picture is kept when it is still allowed, otherwise brought within the limit.
    /// </summary>
    private void Reload()
    {
        if (Source is null)
        {
            RaiseEngine();
            return;
        }

        var limit = UpscalePlan.EffectiveLimit(_settings.Device, SourceWidth, SourceHeight, _settings.ScaleLimit);
        _limit = limit;
        IsLimitUnavailable = !limit.IsAvailable;
        UnavailableMessage = limit.IsAvailable ? null
            : _settings.Device.InstalledRamGb < CapabilityPolicy.MinimumRamGb
                ? Loc.Format("Upscale.UnavailableRam", _settings.Device.InstalledRamGb)
                : Loc.Format("Upscale.UnavailableTooBig", limit.DescribeReasons());

        _syncing = true;
        try
        {
            ScaleChoices.Clear();
            foreach (var preset in CapabilityPolicy.Presets)
            {
                ScaleChoices.Add(new ScaleChoice(preset, preset <= limit.Effective));
            }

            ResolutionChoices.Clear();
            foreach (var preset in UpscalePlan.Presets)
            {
                var (width, height) = UpscalePlan.SizeForLongSide(SourceWidth, SourceHeight, preset.LongSide);
                ResolutionChoices.Add(new ResolutionChoice(preset, width, height, UpscalePlan.IsPresetAllowed(preset, SourceWidth, SourceHeight, limit)));
            }

            SliderMinimum = MinimumPercent;
            SliderMaximum = Math.Max(MinimumPercent, limit.Effective * 100);
        }
        finally
        {
            _syncing = false;
        }

        var locked = CapabilityPolicy.LockedPresets(limit).Select(p => ScaleText.Format(p)).ToList();
        LockedText = limit.IsAvailable && locked.Count > 0
            ? Loc.Format("Upscale.LockedScales", string.Join(", ", locked), limit.DescribeReasons())
            : null;
        OnPropertyChanged(nameof(SliderMinimumText));
        OnPropertyChanged(nameof(SliderMaximumText));

        if (limit.IsAvailable)
        {
            var chosen = _current;
            if (chosen is not null && chosen.Scale >= CapabilityPolicy.MinimumScale && chosen.Scale <= limit.Effective + 1e-9)
            {
                // Exactly as chosen, so a size from the resolution list stays that size.
                Scale = chosen.Scale;
                (OutputWidth, OutputHeight) = (chosen.OutputWidth, chosen.OutputHeight);
                SyncChoices();
                RaiseEstimates();
            }
            else
            {
                var wanted = chosen is null || chosen.Scale < CapabilityPolicy.MinimumScale ? CapabilityPolicy.DefaultScale(limit) : chosen.Scale;
                SetScale(Math.Min(wanted, limit.Effective));
            }
        }
        else
        {
            Scale = 1;
            OutputWidth = SourceWidth;
            OutputHeight = SourceHeight;
            SyncChoices();
            RaiseEstimates();
        }

        RaiseEngine();
        OnPropertyChanged(nameof(CanChoose));
        StartCommand.NotifyCanExecuteChanged();
    }

    private void SetScale(double scale)
    {
        var limit = _limit;
        if (limit is null || !limit.IsAvailable)
        {
            return;
        }

        Scale = Math.Clamp(scale, CapabilityPolicy.MinimumScale, limit.Effective);
        (OutputWidth, OutputHeight) = UpscaleEstimator.OutputSize(SourceWidth, SourceHeight, Scale);
        SyncChoices();
        RaiseEstimates();
    }

    private void SetResolution(ResolutionChoice choice)
    {
        Scale = UpscalePlan.ScaleForLongSide(SourceWidth, SourceHeight, choice.Preset.LongSide);
        (OutputWidth, OutputHeight) = (choice.Width, choice.Height);
        SyncChoices();
        RaiseEstimates();
    }

    /// <summary>Makes the scale list, the slider and the resolution list show the chosen size.</summary>
    private void SyncChoices()
    {
        _syncing = true;
        try
        {
            SelectedScaleChoice = ScaleChoices.FirstOrDefault(c => c.IsEnabled && Math.Abs(c.Scale - Scale) < 1e-9);
            ScalePlaceholder = Loc.Format("Upscale.ScaleCustom", ScaleText.Format(Scale));
            SelectedResolution = ResolutionChoices.FirstOrDefault(c => c.Width == OutputWidth && c.Height == OutputHeight && c.IsEnabled);
            ResolutionPlaceholder = Loc.Format("Upscale.ResolutionCustom", ScaleText.Dimensions(OutputWidth, OutputHeight));
            SliderValue = Math.Clamp(Math.Round(Scale * 100 / PercentStep) * PercentStep, SliderMinimum, SliderMaximum);
        }
        finally
        {
            _syncing = false;
        }

        OnPropertyChanged(nameof(PercentText));
        OnPropertyChanged(nameof(DiagramText));
        OnPropertyChanged(nameof(Diagram));
    }

    partial void OnSelectedScaleChoiceChanged(ScaleChoice? value)
    {
        if (_syncing || value is null)
        {
            return;
        }

        if (value.IsEnabled)
        {
            SetScale(value.Scale);
        }
        else
        {
            SyncChoices();
        }
    }

    partial void OnSelectedResolutionChanged(ResolutionChoice? value)
    {
        if (_syncing || value is null)
        {
            return;
        }

        if (value.IsEnabled)
        {
            SetResolution(value);
        }
        else
        {
            SyncChoices();
        }
    }

    partial void OnSliderValueChanged(double value)
    {
        if (!_syncing)
        {
            SetScale(value / 100);
        }
    }

    /// <summary>"400%": the scale of the result, which is also right for a size picked from the resolution list.</summary>
    public string PercentText => ScaleText.Percent(Scale);

    // ---- The size diagram ----

    /// <summary>The proportions the diagram is drawn from: the picture's shape and the share of the result the original takes.</summary>
    public DiagramGeometry Diagram => new(SourceWidth, SourceHeight, Scale);

    public string DiagramText => Loc.Format(
        "Upscale.DiagramSpoken",
        ScaleText.Dimensions(SourceWidth, SourceHeight),
        ScaleText.Dimensions(OutputWidth, OutputHeight));

    public string OriginalLegend => Loc.Format("Upscale.OriginalLegend", ScaleText.Dimensions(SourceWidth, SourceHeight));

    public string ResultLegend => Loc.Format("Upscale.ResultLegend", ScaleText.Dimensions(OutputWidth, OutputHeight));

    // ---- Format ----

    /// <summary>0 for PNG, 1 for JPG (the RadioButtons' selected index).</summary>
    [ObservableProperty]
    public partial int FormatIndex { get; set; }

    private UpscaleFormat Format => FormatIndex == 1 ? UpscaleFormat.Jpg : UpscaleFormat.Png;

    private string TargetExtension => Format == UpscaleFormat.Jpg ? ".jpg" : ".png";

    partial void OnFormatIndexChanged(int value) => RaiseEstimates();

    // ---- Engine and estimate ----

    /// <summary>"GPU · AMD Radeon(TM) Graphics": the engine chosen in Settings.</summary>
    public string EngineText => DeviceText.Engine(_settings.Device, _settings.RenderMode);

    /// <summary>The engine that renders: an NPU choice renders on the GPU or CPU, which the page says.</summary>
    private RenderEngine EffectiveEngine => UpscaleSupport.EffectiveEngine(_settings.RenderMode, _settings.Device);

    public string? EngineNote => _settings.RenderMode == RenderEngine.Npu
        ? Loc.Format("Upscale.EngineNpuNote", DeviceText.ShortName(EffectiveEngine))
        : null;

    public bool HasEngineNote => EngineNote is not null;

    private void RaiseEngine()
    {
        OnPropertyChanged(nameof(EngineText));
        OnPropertyChanged(nameof(EngineNote));
        OnPropertyChanged(nameof(HasEngineNote));
        RaiseEstimates();
    }

    public string ResolutionValue => HasSource ? ScaleText.Dimensions(OutputWidth, OutputHeight) : "—";

    /// <summary>"14,7 MP · 5K", or only the megapixels for a custom size.</summary>
    public string ResolutionCaption
    {
        get
        {
            if (!HasSource)
            {
                return string.Empty;
            }

            var megapixels = ScaleText.Megapixels((long)OutputWidth * OutputHeight, Loc.Culture);
            var preset = UpscalePlan.MatchPreset(SourceWidth, SourceHeight, OutputWidth, OutputHeight);
            return preset is null ? megapixels : $"{megapixels} · {Loc.Get(preset.NameKey)}";
        }
    }

    public string FileSizeValue => HasSource
        ? ScaleText.Approximately(DisplayFormat.FormatFileSize(UpscaleEstimator.EstimatedFileBytes((long)OutputWidth * OutputHeight, Format), Loc.Culture))
        : "—";

    public string FileSizeCaption => Format == UpscaleFormat.Jpg ? "JPG" : "PNG";

    private const long GiB = 1024L * 1024 * 1024;

    /// <summary>
    /// What the memory limit in Settings allows for this picture (DESIGN §7.5): the largest tile that fits, and whether anything does.
    /// A PNG is assumed to carry transparency, because that is only known once the file is decoded.
    /// </summary>
    private MemoryPlan? Plan => HasSource && !IsLimitUnavailable && OutputWidth > 0
        ? MemoryPlanFor(SourceWidth, SourceHeight, OutputWidth, OutputHeight)
        : null;

    private MemoryPlan MemoryPlanFor(int width, int height, int outputWidth, int outputHeight) =>
        UpscaleMemory.Plan(width, height, outputWidth, outputHeight, EffectiveEngine, Format == UpscaleFormat.Png, _settings.MemoryLimitGb * GiB);

    private int TileSize => Plan?.TileSize ?? TiledUpscaler.DefaultTileSize;

    private bool MemoryFits => Plan is not { Fits: false };

    private double? EstimatedSeconds => HasSource && !IsLimitUnavailable
        ? UpscaleEstimator.EstimatedSeconds(UpscaleSupport.RenderedPixels(SourceWidth, SourceHeight, TileSize), _settings.GetThroughput(EffectiveEngine, TileSize))
        : null;

    /// <summary>"± 7 seconds", or "—" until the engine has been measured: the app never shows an invented speed.</summary>
    public string TimeValue => EstimatedSeconds is { } seconds ? ScaleText.Approximately(ScaleText.Duration(seconds, Loc.Culture)) : "—";

    public string TimeCaption => EstimatedSeconds is null
        ? Loc.Get("Upscale.Estimate.NotMeasured")
        : TileSize < TiledUpscaler.DefaultTileSize
            ? Loc.Format("Upscale.Estimate.TimeSmallTiles", DeviceText.Engine(_settings.Device, EffectiveEngine), TileSize)
            : DeviceText.Engine(_settings.Device, EffectiveEngine);

    private RamTier? RequiredRam => HasSource && !IsLimitUnavailable ? CapabilityPolicy.RequiredRam((long)OutputWidth * OutputHeight) : null;

    private bool InstalledRamIsEnough => RequiredRam is not { } tier || _settings.Device.InstalledRamGb >= tier.Gb;

    /// <summary>The installed RAM meets the tier of the result (§7.2) and the picture fits the memory limit in Settings (§7.5).</summary>
    private bool RamIsEnough => InstalledRamIsEnough && MemoryFits;

    public string RamValue => RequiredRam is { } tier ? Loc.Format("Device.Gigabytes", tier.Gb) : "—";

    public string RamCaption => RequiredRam is null
        ? string.Empty
        : !InstalledRamIsEnough ? Loc.Format("Upscale.Estimate.RamShort", _settings.Device.InstalledRamGb)
        : !MemoryFits ? Loc.Format("Upscale.Estimate.MemoryShort", _settings.MemoryLimitGb, ScaleText.Gigabytes(Plan!.PeakBytes, Loc.Culture))
        : Loc.Format("Upscale.Estimate.RamEnough", _settings.Device.InstalledRamGb);

    public bool RamEnoughVisible => RequiredRam is not null && RamIsEnough;

    public bool RamShortVisible => RequiredRam is not null && !RamIsEnough;

    private void RaiseEstimates()
    {
        // The size on the page is the shown picture's choice: the queue remembers it for that picture.
        if (_current is not null && ReferenceEquals(Source, _current.File) && OutputWidth > 0)
        {
            _current.SetSize(Scale, OutputWidth, OutputHeight);
        }

        RaiseQueue();
        foreach (var name in new[]
        {
            nameof(ResolutionValue), nameof(ResolutionCaption), nameof(FileSizeValue), nameof(FileSizeCaption), nameof(TimeValue),
            nameof(TimeCaption), nameof(RamValue), nameof(RamCaption), nameof(RamEnoughVisible), nameof(RamShortVisible),
            nameof(OriginalLegend), nameof(ResultLegend),
        })
        {
            OnPropertyChanged(name);
        }

        StartCommand.NotifyCanExecuteChanged();
    }

    // ---- Upscaling ----

    /// <summary>Every picture in the queue can be upscaled at its chosen size, the one shown included.</summary>
    private bool CanStart() =>
        State == ConverterState.Input && Source is not null && !IsUnavailable && RamIsEnough && OutputWidth > 0
        && Items.Count > 0 && Items.All(i => BlockOf(i) == UpscaleBlock.None);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (Source is null)
        {
            return;
        }

        if (Items.Count > 1)
        {
            await StartBatchAsync();
            return;
        }

        var extension = TargetExtension;
        var destination = await _desktop.PickDestinationAsync(
            Path.GetFileNameWithoutExtension(UpscalePlan.ResultName(Source.Path, OutputWidth, OutputHeight, extension)),
            Path.GetDirectoryName(Source.Path),
            FormatCatalog.GetTargetLabel(extension),
            extension);
        if (destination is null)
        {
            return;
        }

        // A name typed with another extension still gets the real one, so the file is never mislabeled.
        if (FileExtension.FromPath(destination) != extension)
        {
            destination += extension;
        }

        _lastScale = Scale;
        await RunAsync(new ConversionJob(Source.Path, extension, destination, new UpscaleOptions(OutputWidth, OutputHeight, _settings.RenderMode, TileSize)));
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
        RaiseQueue();
    }

    [RelayCommand]
    private void UpscaleAnother()
    {
        ClearQueue();
        IsBatch = false;
        BatchResults.Clear();
        _batch = [];
        Source = null;
        Result = null;
        ResultNotes.Clear();
        ResultMessage = null;
        InputMessage = null;
        _limit = null;
        IsLimitUnavailable = false;
        UnavailableMessage = null;
        LockedText = null;
        OutputWidth = OutputHeight = 0;
        ScaleChoices.Clear();
        ResolutionChoices.Clear();
        State = ConverterState.Input;
        RaiseEngine();
        OnPropertyChanged(nameof(CanChoose));
    }

    // ---- A batch ----

    /// <summary>Several pictures into one folder: the processing and finished cards show the list instead of one file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessingTitle), nameof(IsSingleDone), nameof(IsBatchDone))]
    public partial bool IsBatch { get; set; }

    public bool IsSingleDone => IsDone && !IsBatch;

    public bool IsBatchDone => IsDone && IsBatch;

    public ObservableCollection<BatchResultItem> BatchResults { get; } = [];

    [ObservableProperty]
    public partial string BatchFolder { get; set; } = string.Empty;

    /// <summary>"Gambar 2 dari 5: pantai.jpg · 2× · 2560 × 1440", above the four stages.</summary>
    [ObservableProperty]
    public partial string BatchCurrentText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity BatchSeverity { get; set; }

    [ObservableProperty]
    public partial string BatchTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BatchMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BatchSavedText { get; set; } = string.Empty;

    /// <summary>At least one picture was not saved: it can be upscaled again.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryBatchCommand))]
    public partial bool BatchHasUnsaved { get; set; }

    /// <summary>"Coba lagi gambar yang gagal" when something failed; "Upscale sisanya" when the rest was only stopped.</summary>
    [ObservableProperty]
    public partial string BatchRetryLabel { get; set; } = string.Empty;

    /// <summary>Nothing was saved: "Ubah pilihan" is offered instead of opening an empty folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchHasSaved))]
    public partial bool BatchSavedNothing { get; set; }

    public bool BatchHasSaved => !BatchSavedNothing;

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>The queue into a chosen folder: "pantai-2560x1440.png" and so on, never over a file that is there (DESIGN §6.1.3).</summary>
    private async Task StartBatchAsync()
    {
        var folder = await _desktop.PickFolderAsync(Path.GetDirectoryName(Items[0].File.Path));
        if (folder is null)
        {
            return;
        }

        var extension = TargetExtension;
        IReadOnlyList<string> names;
        try
        {
            names = OutputNames.PlanNamed(
                [.. Items.Select(i => (i.File.Path, UpscalePlan.ResultName(i.File.Path, i.OutputWidth, i.OutputHeight, extension)))],
                folder,
                Exists);
        }
        catch (IOException)
        {
            ShowInputMessage(Loc.Get("Error.Io"), InfoBarSeverity.Warning);
            return;
        }

        _batch = [.. Items.Select((item, i) => new UpscaleBatchEntry(
            new ConversionJob(
                item.File.Path,
                extension,
                names[i],
                new UpscaleOptions(item.OutputWidth, item.OutputHeight, _settings.RenderMode, MemoryPlanFor(item.Width, item.Height, item.OutputWidth, item.OutputHeight).TileSize)),
            item.Scale,
            item.Name,
            item.Width,
            item.Height))];
        BatchFolder = folder;
        BatchResults.Clear();
        for (var i = 0; i < _batch.Count; i++)
        {
            BatchResults.Add(new BatchResultItem(i + 1, _batch[i].SourceName, Path.GetFileName(_batch[i].Job.DestinationPath), ShowPathInFolder));
        }

        await RunBatchRowsAsync([.. Enumerable.Range(0, _batch.Count)]);
    }

    /// <summary>Upscales the rows <paramref name="rows"/> of the batch, one after another: all of them, or the ones not saved.</summary>
    private async Task RunBatchRowsAsync(IReadOnlyList<int> rows)
    {
        foreach (var row in rows)
        {
            BatchResults[row].State = BatchRowState.Waiting;
            BatchResults[row].Detail = null;
            BatchResults[row].OutputPath = null;
        }

        _batchCount = rows.Count;
        _batchIndex = -1;
        _lastStage = PipelineStage.Decode;
        _lastEngine = EffectiveEngine;
        IsBatch = true;
        InputMessage = null;
        ResultMessage = null;
        ResultNotes.Clear();
        ProgressPercent = 0;
        ProcessingCaption = Loc.Format("Upscale.Batch.ProcessingCaption", DeviceText.Engine(_settings.Device, _lastEngine), BatchFolder);
        BatchCurrentText = string.Empty;
        ResetSteps();
        OnPropertyChanged(nameof(ProcessingTitle));
        State = ConverterState.Processing;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var started = System.Diagnostics.Stopwatch.StartNew();
        _log.Info($"Upscale batch started: {rows.Count} pictures on {_lastEngine}");
        IReadOnlyList<BatchItemResult> outcomes = [];
        try
        {
            // Measured once per tile size before the first picture, as a single upscale does (DESIGN §8).
            foreach (var tileSize in rows.Select(r => ((UpscaleOptions)_batch[r].Job.Options!).TileSize).Distinct())
            {
                await MeasureEngineIfNeededAsync(_lastEngine, tileSize, cancellation.Token);
            }

            // Created here, on the UI thread, so progress reports come back to it.
            var progress = new Progress<BatchProgress>(p => OnBatchProgress(p, rows));
            outcomes = await ConversionBatch.RunAsync(
                [.. rows.Select(r => new BatchJob(_batch[r].Job, _pipeline))],
                progress,
                r => OnBatchItemFinishedAsync(r, rows[r.Index]),
                Exists,
                cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Stopped while the engine was being measured: nothing was made.
        }
        finally
        {
            _cancellation = null;
            IsIndeterminate = false;
        }

        // A picture never started (stopped while measuring) can be upscaled again with the rest.
        foreach (var row in rows.Where(r => BatchResults[r].State == BatchRowState.Waiting))
        {
            BatchResults[row].State = BatchRowState.Cancelled;
        }

        var done = BatchResults.Count(r => r.IsDone);
        var failed = BatchResults.Count(r => r.IsFailed);
        var notSaved = BatchResults.Count - done;
        _log.Info(string.Create(CultureInfo.InvariantCulture, $"Upscale batch finished: {done} saved, {failed} failed, {notSaved - failed} not upscaled, {started.Elapsed.TotalSeconds:0.0} s"));

        if (done == 0 && failed == 0)
        {
            // Stopped before anything was saved: back to the choices, as a single upscale does.
            IsBatch = false;
            State = ConverterState.Input;
            ShowInputMessage(Loc.Get("Upscale.Cancelled"), InfoBarSeverity.Informational);
            RaiseEstimates();
            return;
        }

        var cancelled = cancellation.IsCancellationRequested || outcomes.Any(o => o.Outcome == BatchItemOutcome.Cancelled);
        (BatchSeverity, BatchTitle, BatchMessage) = (done, failed) switch
        {
            _ when done == BatchResults.Count => (InfoBarSeverity.Success, Loc.Get("Upscale.Done.Title"), Loc.Format("Upscale.Batch.AllDone", done)),
            (0, _) => (InfoBarSeverity.Error, Loc.Get("Upscale.Failed.Title"), Loc.Get("Upscale.Batch.None")),
            _ when cancelled && failed == 0 => (InfoBarSeverity.Warning, Loc.Get("Upscale.Batch.StoppedTitle"), Loc.Format("Upscale.Batch.Stopped", done, BatchResults.Count)),
            _ => (InfoBarSeverity.Warning, Loc.Get("Upscale.Batch.SomeFailedTitle"), Loc.Format("Upscale.Batch.SomeFailed", done, BatchResults.Count)),
        };
        BatchSavedText = Loc.Format("Upscale.Batch.SavedCount", done, BatchResults.Count);
        BatchHasUnsaved = notSaved > 0;
        BatchRetryLabel = Loc.Get(failed > 0 ? "Upscale.Batch.RetryFailed" : "Upscale.Batch.RetryRest");
        BatchSavedNothing = done == 0;
        State = ConverterState.Done;
        RaiseEstimates();
    }

    private void OnBatchProgress(BatchProgress progress, IReadOnlyList<int> rows)
    {
        if (State != ConverterState.Processing)
        {
            return;
        }

        // A new picture: its four stages start from the beginning.
        if (progress.Index != _batchIndex)
        {
            _batchIndex = progress.Index;
            _lastStage = PipelineStage.Decode;
            var entry = _batch[rows[progress.Index]];
            var options = (UpscaleOptions)entry.Job.Options!;
            BatchCurrentText = Loc.Format(
                "Upscale.Batch.Current",
                progress.Index + 1,
                progress.Count,
                entry.SourceName,
                ScaleText.Format(entry.Scale),
                ScaleText.Dimensions(options.OutputWidth, options.OutputHeight));
            ResetSteps();
        }

        var itemPercent = ShowStage(progress.Item);

        // Rounded down, so 100% only shows once the last picture is really saved.
        ProgressPercent = Math.Floor((progress.Index + (itemPercent / 100)) / progress.Count * 100);
    }

    private async Task OnBatchItemFinishedAsync(BatchItemResult result, int rowIndex)
    {
        var row = BatchResults[rowIndex];
        var entry = _batch[rowIndex];
        row.OutputName = Path.GetFileName(result.Job.DestinationPath);
        switch (result.Outcome)
        {
            case BatchItemOutcome.Done:
                row.OutputPath = result.Result!.OutputPath;
                row.Detail = result.Result.Notes?.FirstOrDefault()?.Message;
                row.State = BatchRowState.Done;

                // A retry keeps the name this picture was really saved under.
                _batch[rowIndex] = entry with { Job = result.Job };
                await RecordHistoryAsync(result.Job, result.Result, entry.Scale);
                break;
            case BatchItemOutcome.Failed:
                row.Detail = ErrorMessages.Describe(result.Error!, result.LastStage, FormatCatalog.GetTargetLabel(result.Job.TargetExtension));
                row.State = BatchRowState.Failed;
                _log.Error($"Upscale batch picture failed at {result.LastStage}: {result.Error!.GetType().Name}: {result.Error.Message}");
                break;
            default:
                row.State = BatchRowState.Cancelled;
                break;
        }
    }

    /// <summary>"Coba lagi gambar yang gagal" / "Upscale sisanya": the pictures that weren't saved, again, into the same folder.</summary>
    [RelayCommand(CanExecute = nameof(BatchHasUnsaved))]
    private async Task RetryBatchAsync()
    {
        var rows = BatchResults.Select((r, i) => (r, i)).Where(x => x.r.IsNotDone).Select(x => x.i).ToList();
        if (rows.Count > 0)
        {
            await RunBatchRowsAsync(rows);
        }
    }

    [RelayCommand]
    private void OpenBatchFolder() => RunShellAction(() => _desktop.OpenFolder(BatchFolder), Loc.Get("Shell.ExplorerFailed"));

    private void ShowPathInFolder(string path) => RunShellAction(() => _desktop.ShowInFolder(path), Loc.Get("Shell.ExplorerFailed"));

    // ---- Processing ----

    public string ProcessingTitle => IsBatch
        ? Loc.Format("Upscale.Batch.ProcessingTitle", _batchCount)
        : Loc.Format("Upscale.Processing.Title", Source?.Name ?? string.Empty);

    [ObservableProperty]
    public partial string ProcessingCaption { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double ProgressPercent { get; set; }

    public string ProgressText => $"{ProgressPercent:0}%";

    /// <summary>True while the engine is being measured, which has no steps to count.</summary>
    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    public ObservableCollection<ConversionStepViewModel> Steps { get; } = [];

    private async Task RunAsync(ConversionJob job)
    {
        IsBatch = false;
        _lastJob = job;
        _lastStage = PipelineStage.Decode;
        _lastEngine = EffectiveEngine;
        InputMessage = null;
        ResultMessage = null;
        ResultNotes.Clear();
        ProgressPercent = 0;
        ProcessingCaption = $"{ScaleText.Format(_lastScale)} · {ScaleText.Dimensions(((UpscaleOptions)job.Options!).OutputWidth, ((UpscaleOptions)job.Options!).OutputHeight)} · {DeviceText.Engine(_settings.Device, _lastEngine)}";
        ResetSteps();
        State = ConverterState.Processing;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        // Created here, on the UI thread, so progress reports come back to it.
        var progress = new Progress<PipelineProgress>(OnProgress);
        var started = System.Diagnostics.Stopwatch.StartNew();
        _log.Info($"Upscale started: {ScaleText.Format(_lastScale)} on {_lastEngine}");
        try
        {
            await MeasureEngineIfNeededAsync(_lastEngine, ((UpscaleOptions)job.Options!).TileSize, cancellation.Token);

            // Hashing and verification run on a worker thread; the window stays responsive.
            var result = await Task.Run(() => _pipeline.RunAsync(job, progress, cancellation.Token), CancellationToken.None);
            ResultNotes.Clear();
            foreach (var note in result.Notes ?? [])
            {
                ResultNotes.Add(new ResultNoteItem(note.Message, note.Severity == NoteSeverity.Warning ? InfoBarSeverity.Warning : InfoBarSeverity.Informational));
                _log.Info($"Upscale note ({note.Severity})");
            }

            Result = result;
            State = ConverterState.Done;
            await RecordHistoryAsync(job, result, _lastScale);
            _log.Info(string.Create(CultureInfo.InvariantCulture, $"Upscale verified: {result.ChunkCount} chunks, {started.Elapsed.TotalSeconds:0.0} s"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            State = ConverterState.Input;
            ShowInputMessage(Loc.Get("Upscale.Cancelled"), InfoBarSeverity.Informational);
            _log.Info($"Upscale cancelled, at {_lastStage}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ErrorMessages.Describe(ex, _lastStage, FormatCatalog.GetTargetLabel(job.TargetExtension));
            State = ConverterState.Failed;
            _log.Error($"Upscale failed at {_lastStage}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _cancellation = null;
            IsIndeterminate = false;
            RaiseEstimates();
        }
    }

    /// <summary>
    /// The first upscale on an engine measures its speed (one tile to warm up, three timed; DESIGN §8), which the estimates then
    /// use. An engine that can't run the model is left unmeasured: the converter falls back to the CPU and says so.
    /// </summary>
    private async Task MeasureEngineIfNeededAsync(RenderEngine engine, int tileSize, CancellationToken ct)
    {
        if (_settings.GetThroughput(engine, tileSize) is not null || UpscaleModelLocator.GetStatus() != UpscaleModelStatus.Ready)
        {
            return;
        }

        Steps[0].Detail = Loc.Get("Upscale.Step.Measuring");
        IsIndeterminate = true;
        using var workload = new UpscaleBenchmarkWorkload();
        try
        {
            var speed = await _benchmark.MeasureAsync(engine, workload, tileSize, ct);
            _log.Info(string.Create(CultureInfo.InvariantCulture, $"Measured {engine} at tile {tileSize}: {speed:0.00} MP/s"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Info($"Could not measure {engine}: {ex.GetType().Name}");
        }
        finally
        {
            IsIndeterminate = false;
        }
    }

    private void ResetSteps()
    {
        Steps.Clear();
        Steps.Add(new ConversionStepViewModel(Loc.Get("Upscale.Step.Prepare")));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Upscale.Step.Tiles")));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Upscale.Step.Save")));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Upscale.Step.Verify")));
    }

    private void OnProgress(PipelineProgress progress)
    {
        if (State == ConverterState.Processing)
        {
            // Rounded down, so 100% only shows once the file is really saved.
            ProgressPercent = Math.Floor(ShowStage(progress));
        }
    }

    /// <summary>
    /// The page's own stages: the converter's Decode and Encode stages, and the pipeline's checks, laid over the percentages
    /// of DESIGN §6.2. Returns how far the picture is, 0 to 100.
    /// </summary>
    private double ShowStage(PipelineProgress progress)
    {
        if (progress.Stage != _lastStage)
        {
            _log.Debug($"Stage {progress.Stage}");
        }

        _lastStage = progress.Stage;

        // The step now running, and how far along the bar is (rounded down, so 100% only shows once the file is really saved).
        int current;
        double percent;
        string? detail = null;
        var allDone = false;
        switch (progress.Stage)
        {
            case PipelineStage.Decode:
                current = 0;
                percent = 10 * progress.StageFraction;
                detail = Loc.Get("Upscale.Step.Loading");
                break;
            case PipelineStage.Encode when progress.StageFraction < UpscaleSupport.TilesEnd:
                current = 1;
                percent = 10 + (80 * progress.StageFraction / UpscaleSupport.TilesEnd);
                detail = UpscaleSupport.TryParseTiles(progress.Detail, out var done, out var total)
                    ? Loc.Format("Upscale.Step.TileOf", done, total)
                    : null;
                break;
            case PipelineStage.Encode:
                current = 2;
                percent = 90 + (6 * (progress.StageFraction - UpscaleSupport.TilesEnd) / (1 - UpscaleSupport.TilesEnd));
                detail = Loc.Format("Upscale.Step.Writing", TargetExtension);
                break;
            default:
                current = 3;
                percent = 96 + (4 * Math.Clamp((progress.OverallFraction - 0.70) / 0.30, 0, 1));
                allDone = progress.Stage == PipelineStage.VerifyIntegrity && progress.StageFraction >= 1;
                detail = progress.Stage == PipelineStage.VerifyChunks
                    ? Loc.Get("Step.RereadingFromDisk")
                    : Loc.Get(progress.StageFraction < 0.5 ? "Step.ComputingHash" : "Step.Reopening");
                break;
        }

        IsIndeterminate = false;
        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (i < current || (i == current && allDone))
            {
                step.State = StepState.Done;
                step.Detail = i == 3 ? Loc.Get("Step.HashMatches") : Loc.Get("Step.Done");
            }
            else if (i == current)
            {
                step.State = StepState.Active;
                step.Detail = detail ?? step.Detail;
            }
            else
            {
                step.State = StepState.Waiting;
                step.Detail = Loc.Get("Step.Waiting");
            }
        }

        return percent;
    }

    // ---- Done and failed ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultIntegrity), nameof(ResultResolution), nameof(ResultEngine))]
    public partial ConversionResult? Result { get; set; }

    public ObservableCollection<ResultNoteItem> ResultNotes { get; } = [];

    /// <summary>"1280 × 720 → 5120 × 2880 (4×)".</summary>
    public string ResultResolution => _lastJob?.Options is UpscaleOptions options
        ? $"{ScaleText.Dimensions(SourceWidth, SourceHeight)} → {ScaleText.Dimensions(options.OutputWidth, options.OutputHeight)} ({ScaleText.Format(_lastScale)})"
        : string.Empty;

    public string ResultEngine => DeviceText.Engine(_settings.Device, _lastEngine);

    public string ResultIntegrity => Result?.ChunkCount == 1
        ? Loc.Get("Result.IntegrityOne")
        : Loc.Format("Result.Integrity", Result?.ChunkCount ?? 0);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultMessage))]
    public partial string? ResultMessage { get; set; }

    public bool HasResultMessage => ResultMessage is not null;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>An upscale goes into the same history as a conversion, marked with its scale (DESIGN §6.2).</summary>
    private async Task RecordHistoryAsync(ConversionJob job, ConversionResult result, double scale)
    {
        try
        {
            await _history.AddAsync(new HistoryEntry(
                Path.GetFileName(job.SourcePath),
                FileExtension.FromPath(job.SourcePath),
                FileExtension.Normalize(job.TargetExtension),
                DateTimeOffset.Now,
                result.OutputPath,
                VerificationStatus.Verified,
                scale));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ResultMessage = Loc.Get("History.RecordFailed");
        }
    }

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
}

/// <summary>The shape of the size diagram: the picture's proportions and how much of the result's width the original takes.</summary>
public readonly record struct DiagramGeometry(int SourceWidth, int SourceHeight, double Scale)
{
    public double Aspect => SourceHeight > 0 ? (double)SourceWidth / SourceHeight : 1;

    /// <summary>The original's width and height as a share of the result's: 100 / scale percent.</summary>
    public double OriginalShare => Scale >= 1 ? 1 / Scale : 1;
}

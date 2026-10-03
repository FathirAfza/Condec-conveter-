// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>Upscale Image (DESIGN §6.2): the picture, the size of the result, the estimate, and the upscale itself.</summary>
public sealed partial class UpscaleViewModel : ObservableObject
{
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
    [NotifyPropertyChangedFor(nameof(IsInput), nameof(IsProcessing), nameof(IsDone), nameof(IsFailed))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
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

    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var path = await _desktop.PickSourceFileAsync(SourceExtensions());
        if (path is not null)
        {
            await SelectSourceAsync(path);
        }
    }

    private List<string> SourceExtensions() => [.. _registry.GetSourceExtensions()];

    /// <summary>Drag and drop: exactly one file is accepted.</summary>
    /// <param name="filePaths">An empty path is a file with no place on disk, such as one inside a ZIP.</param>
    public async Task SelectDroppedAsync(IReadOnlyList<string> filePaths, int folderCount)
    {
        if (folderCount > 0 && filePaths.Count == 0)
        {
            ShowInputMessage(Loc.Get("Input.DropFolder"), InfoBarSeverity.Warning);
        }
        else if (filePaths.Count + folderCount > 1)
        {
            ShowInputMessage(Loc.Get("Upscale.DropMany"), InfoBarSeverity.Warning);
        }
        else if (filePaths.Count == 1 && filePaths[0].Length == 0)
        {
            ReportUnreadableDrop();
        }
        else if (filePaths.Count == 1)
        {
            await SelectSourceAsync(filePaths[0]);
        }
    }

    public void ReportUnreadableDrop() =>
        ShowInputMessage(Loc.Get("Input.DropUnreadable"), InfoBarSeverity.Warning);

    public async Task SelectSourceAsync(string path)
    {
        if (State != ConverterState.Input)
        {
            return;
        }

        var extension = FileExtension.FromPath(path);
        if (extension.Length == 0 || _registry.GetTargetOptions(extension).Count == 0)
        {
            var supported = string.Join(", ", SourceExtensions().Select(e => FormatCatalog.GetTargetLabel(e)).Distinct());
            ShowInputMessage(
                extension.Length == 0 ? Loc.Format("Upscale.UnsupportedNoExtension", supported) : Loc.Format("Upscale.Unsupported", extension, supported),
                InfoBarSeverity.Warning);
            return;
        }

        long size;
        ImageHeaderInfo header;
        try
        {
            size = new FileInfo(path).Length;
            header = await Task.Run(() => ImageHeader.ReadAsync(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInputMessage(Loc.Get("Input.CannotOpen"), InfoBarSeverity.Warning);
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Windows throws its own exception types for a file that is not a picture, or one that is damaged.
            _log.Info($"Upscale source not readable: {ex.GetType().Name}");
            ShowInputMessage(Loc.Get("Error.Decode"), InfoBarSeverity.Warning);
            return;
        }

        if (header.Pixels > UpscaleSupport.MaximumSourcePixels)
        {
            ShowInputMessage(Loc.Format("Error.ImageTooLarge", (header.Pixels + 500_000) / 1_000_000), InfoBarSeverity.Warning);
            return;
        }

        InputMessage = null;
        SourceWidth = (int)header.Width;
        SourceHeight = (int)header.Height;
        Source = new SourceFile(path, extension, size);
        OnPropertyChanged(nameof(SourceCaption));
        _limit = null;
        Reload(resetScale: true);
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
        if (Source is null)
        {
            RaiseEngine();
            return;
        }

        Reload(resetScale: false);
    }

    /// <summary>Reads the limit again (the picture, or Settings, changed) and puts every choice back in step with it.</summary>
    private void Reload(bool resetScale)
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
            var wanted = resetScale || Scale < CapabilityPolicy.MinimumScale ? CapabilityPolicy.DefaultScale(limit) : Scale;
            SetScale(Math.Min(wanted, limit.Effective));
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
        ? UpscaleMemory.Plan(SourceWidth, SourceHeight, OutputWidth, OutputHeight, EffectiveEngine, Format == UpscaleFormat.Png, _settings.MemoryLimitGb * GiB)
        : null;

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

    private bool CanStart() => State == ConverterState.Input && Source is not null && !IsUnavailable && RamIsEnough && OutputWidth > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (Source is null)
        {
            return;
        }

        var extension = TargetExtension;
        var destination = await _desktop.PickDestinationAsync(
            $"{Path.GetFileNameWithoutExtension(Source.Path)}-{OutputWidth}x{OutputHeight}",
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
    private void BackToInput() => State = ConverterState.Input;

    [RelayCommand]
    private void UpscaleAnother()
    {
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

    // ---- Processing ----

    public string ProcessingTitle => Loc.Format("Upscale.Processing.Title", Source?.Name ?? string.Empty);

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
            await RecordHistoryAsync(job, result);
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

    /// <summary>The page's own stages: the converter's Decode and Encode stages, and the pipeline's checks, laid over the percentages of DESIGN §6.2.</summary>
    private void OnProgress(PipelineProgress progress)
    {
        if (State != ConverterState.Processing)
        {
            return;
        }

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

        ProgressPercent = Math.Floor(percent);
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
    private async Task RecordHistoryAsync(ConversionJob job, ConversionResult result)
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
                _lastScale));
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

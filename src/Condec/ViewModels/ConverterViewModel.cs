// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.History;
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
public sealed partial class ConverterViewModel : ObservableObject
{
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
    [NotifyPropertyChangedFor(nameof(IsInput), nameof(IsProcessing), nameof(IsDone), nameof(IsFailed))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial ConverterState State { get; set; }

    public bool IsInput => State == ConverterState.Input;

    public bool IsProcessing => State == ConverterState.Processing;

    public bool IsDone => State == ConverterState.Done;

    public bool IsFailed => State == ConverterState.Failed;

    // ---- Input ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource), nameof(HasNoSource), nameof(FormatPlaceholder), nameof(FormatHelpText), nameof(ProcessingTitle), nameof(ShowsPageChoice))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial SourceFile? Source { get; set; }

    public bool HasSource => Source is not null;

    public bool HasNoSource => Source is null;

    public ObservableCollection<FormatOption> TargetOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConversionFormat), nameof(ShowsPageChoice), nameof(ShowsAudioQuality), nameof(ShowsVideoSize), nameof(ShowsMediaOptions))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial FormatOption? SelectedTarget { get; set; }

    /// <summary>The page of a PDF source.</summary>
    public PdfOptionsViewModel Pdf { get; } = new();

    /// <summary>Audio quality and video size for audio and video targets (DESIGN §6.1.2).</summary>
    public MediaOptionsViewModel Media { get; } = new();

    /// <summary>MP3, M4A and WMA have a bitrate to choose; WAV and FLAC are lossless.</summary>
    public bool ShowsAudioQuality => SelectedTarget?.Extension is ".mp3" or ".m4a" or ".wma";

    public bool ShowsVideoSize => SelectedTarget?.Extension is ".mp4" or ".wmv";

    public bool ShowsMediaOptions => ShowsAudioQuality || ShowsVideoSize;

    private bool IsPdfSource => Source?.Extension == ".pdf";

    /// <summary>A PDF going to a one-page target (an image) needs a page.</summary>
    public bool ShowsPageChoice => IsPdfSource && SelectedTarget?.Extension is ".png" or ".jpg" or ".heic";

    public string FormatPlaceholder => Loc.Get(HasSource ? "Format.Placeholder" : "Format.PlaceholderNoFile");

    public string FormatHelpText => Source is null ? Loc.Get("Format.HelpNone") : DescribeAvailableFormats(Source.Extension);

    private string DescribeAvailableFormats(string extension)
    {
        var count = TargetOptions.Count(o => o.IsEnabled);
        return count == 1 ? Loc.Format("Format.HelpOne", extension) : Loc.Format("Format.HelpCount", count, extension);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInputMessage))]
    public partial string? InputMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity InputMessageSeverity { get; set; }

    public bool HasInputMessage => InputMessage is not null;

    // ---- Processing ----

    public string ProcessingTitle => Loc.Format("Processing.Title", Source?.Name ?? string.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessingCaption))]
    public partial string? DestinationPath { get; set; }

    public string ProcessingCaption => Loc.Format("Processing.Caption", SelectedTarget?.DisplayName ?? string.Empty, DestinationPath ?? string.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double ProgressPercent { get; set; }

    public string ProgressText => $"{ProgressPercent:0}%";

    public ObservableCollection<ConversionStepViewModel> Steps { get; } = [];

    // ---- Done and failed ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultIntegrity))]
    public partial ConversionResult? Result { get; set; }

    /// <summary>"PNG → JPG", like the design's "DOCX → PDF".</summary>
    public string ConversionFormat => Source is null || SelectedTarget is null
        ? string.Empty
        : $"{FileExtension.ToCode(Source.Extension)} → {SelectedTarget.DisplayName}";

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

    // ---- Choosing a file ----

    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var path = await _desktop.PickSourceFileAsync(SourceExtensions());
        if (path is not null)
        {
            SelectSource(path);
        }
    }

    /// <summary>Drag and drop: exactly one file is accepted.</summary>
    /// <param name="filePaths">An empty path is a file with no place on disk, such as one inside a ZIP.</param>
    public void SelectDropped(IReadOnlyList<string> filePaths, int folderCount)
    {
        if (folderCount > 0 && filePaths.Count == 0)
        {
            ShowInputMessage(Loc.Get("Input.DropFolder"), InfoBarSeverity.Warning);
        }
        else if (filePaths.Count + folderCount > 1)
        {
            ShowInputMessage(Loc.Get("Input.DropMany"), InfoBarSeverity.Warning);
        }
        else if (filePaths.Count == 1 && filePaths[0].Length == 0)
        {
            ReportUnreadableDrop();
        }
        else if (filePaths.Count == 1)
        {
            SelectSource(filePaths[0]);
        }
    }

    public void ReportUnreadableDrop() =>
        ShowInputMessage(Loc.Get("Input.DropUnreadable"), InfoBarSeverity.Warning);

    public void SelectSource(string path)
    {
        if (State != ConverterState.Input)
        {
            return;
        }

        var extension = FileExtension.FromPath(path);
        var options = extension.Length == 0 ? [] : TargetOptionsFor(extension);
        if (options.Count == 0)
        {
            // A DWG dropped on Convert File: say where it goes instead.
            var elsewhere = extension.Length > 0 && _registry.GetTargetOptions(extension).Count > 0;
            ShowInputMessage(
                elsewhere ? Loc.Format("Input.UseArchitecture", extension) : DescribeUnsupported(extension),
                elsewhere ? InfoBarSeverity.Informational : InfoBarSeverity.Warning);
            return;
        }

        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInputMessage(Loc.Get("Input.CannotOpen"), InfoBarSeverity.Warning);
            return;
        }

        int? pageCount = null;
        if (extension == ".pdf")
        {
            try
            {
                pageCount = PdfInspector.CountPages(path);
            }
            catch (LockedPdfException)
            {
                ShowInputMessage(ErrorMessages.LockedPdf, InfoBarSeverity.Warning);
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // PdfPig throws its own exception types for damaged files.
                ShowInputMessage(Loc.Get("Input.PdfUnreadable"), InfoBarSeverity.Warning);
                return;
            }

            Pdf.Load(path, pageCount.Value);
        }

        // Keep the chosen format when the new file can be converted to it too.
        var previous = SelectedTarget?.Extension;
        TargetOptions.Clear();
        foreach (var option in options)
        {
            TargetOptions.Add(new FormatOption(option));
        }

        InputMessage = null;
        Source = new SourceFile(path, extension, size, pageCount);
        SelectedTarget = TargetOptions.FirstOrDefault(o => o.Extension == previous && o.Option.IsEnabled);

        if (options.FirstOrDefault(o => !o.IsEnabled)?.DisabledReason is { } reason)
        {
            ShowInputMessage(reason, InfoBarSeverity.Informational);
        }
    }

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
        && Source is not null
        && SelectedTarget is { Option.IsEnabled: true };

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        if (Source is null || SelectedTarget is null)
        {
            return;
        }

        // A smaller MP4 is an MP4 again: its name is suggested with a suffix, so the source isn't the first thing offered to be replaced.
        var suggestedName = Path.GetFileNameWithoutExtension(Source.Path)
            + (SelectedTarget.Extension == Source.Extension ? Loc.Get("Convert.SameFormatSuffix") : string.Empty);
        var destination = await _desktop.PickDestinationAsync(
            suggestedName,
            Path.GetDirectoryName(Source.Path),
            SelectedTarget.DisplayName,
            SelectedTarget.Extension);
        if (destination is null)
        {
            return;
        }

        // A name typed with another extension still gets the real one, so the file is never mislabeled.
        if (FileExtension.FromPath(destination) != SelectedTarget.Extension)
        {
            destination += SelectedTarget.Extension;
        }

        ConversionOptions? options = IsPdfSource ? Pdf.BuildOptions(SelectedTarget.Extension) : ShowsMediaOptions ? Media.BuildOptions() : null;
        await RunAsync(new ConversionJob(Source.Path, SelectedTarget.Extension, destination, options));
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
    private void ConvertAnother()
    {
        TargetOptions.Clear();
        SelectedTarget = null;
        Source = null;
        Result = null;
        ResultNotes.Clear();
        ResultMessage = null;
        InputMessage = null;
        State = ConverterState.Input;
    }

    private async Task RunAsync(ConversionJob job)
    {
        _lastJob = job;
        _lastStage = PipelineStage.Decode;
        _chunkCount = 0;
        DestinationPath = job.DestinationPath;
        InputMessage = null;
        ResultMessage = null;
        ResultNotes.Clear();
        ProgressPercent = 0;
        ResetSteps();
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
            _log.Info(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Conversion verified: {kind}, {result.ChunkCount} chunks, {started.Elapsed.TotalSeconds:0.0} s"));
            await RecordHistoryAsync(job, result);
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

    private void ResetSteps()
    {
        Steps.Clear();
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.Decode")));
        Steps.Add(new ConversionStepViewModel(Loc.Format("Step.Encode", SelectedTarget?.DisplayName ?? string.Empty)));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.VerifyChunks")));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.VerifyIntegrity")));
    }

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
        if (progress.ChunkCount > 0)
        {
            _chunkCount = progress.ChunkCount;
        }

        // Rounded down, so 100% only shows once the file is really saved.
        ProgressPercent = Math.Floor(progress.OverallFraction * 100);

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
        PipelineStage.Decode => progress.Detail ?? Loc.Format("Step.Reading", Source?.Name ?? string.Empty),
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

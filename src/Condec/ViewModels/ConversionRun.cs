// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.History;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Pipeline;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

public enum RunState
{
    /// <summary>Nothing is running and there is no result: the page shows its own input.</summary>
    Idle,
    Processing,
    Done,
    Failed,
}

/// <summary>
/// One conversion from start to finish for a page that has its own input (Architecture): the progress list, the finished
/// card, the failed card, and the history entry. The page decides what to show while this is <see cref="RunState.Idle"/>.
/// </summary>
public sealed partial class ConversionRun : ObservableObject
{
    private readonly HistoryStore _history;
    private readonly IDesktopServices _desktop;
    private readonly ActivityLog _log;
    private CancellationTokenSource? _cancellation;
    private ConversionPipeline? _lastPipeline;
    private ConversionJob? _lastJob;
    private string _sourceName = string.Empty;
    private string _format = string.Empty;
    private string _targetLabel = string.Empty;
    private PipelineStage _lastStage;
    private int _chunkCount;

    public ConversionRun(HistoryStore history, IDesktopServices desktop, ActivityLog log)
    {
        _history = history;
        _desktop = desktop;
        _log = log;
    }

    /// <summary>The user stopped the conversion; the page goes back to its input.</summary>
    public event EventHandler? Cancelled;

    /// <summary>"Ubah pilihan" on the failed card.</summary>
    public event EventHandler? BackRequested;

    /// <summary>"Konversi file lain" on the finished card.</summary>
    public event EventHandler? AnotherRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsProcessing), nameof(IsDone), nameof(IsFailed))]
    public partial RunState State { get; set; }

    /// <summary>A conversion is running or its card is showing.</summary>
    public bool IsActive => State != RunState.Idle;

    public bool IsProcessing => State == RunState.Processing;

    public bool IsDone => State == RunState.Done;

    public bool IsFailed => State == RunState.Failed;

    public string SourceName => _sourceName;

    public string ProcessingTitle => Loc.Format("Processing.Title", _sourceName);

    [ObservableProperty]
    public partial string ProcessingCaption { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double ProgressPercent { get; set; }

    public string ProgressText => $"{ProgressPercent:0}%";

    public ObservableCollection<ConversionStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultIntegrity))]
    public partial ConversionResult? Result { get; set; }

    /// <summary>"PNG → DWG", like "DOCX → PDF" in Convert File.</summary>
    public string ConversionFormat => _format;

    public string ResultIntegrity => Result?.ChunkCount == 1
        ? Loc.Get("Result.IntegrityOne")
        : Loc.Format("Result.Integrity", Result?.ChunkCount ?? 0);

    public ObservableCollection<ResultNoteItem> ResultNotes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultMessage))]
    public partial string? ResultMessage { get; set; }

    public bool HasResultMessage => ResultMessage is not null;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>Back to <see cref="RunState.Idle"/>, forgetting the last result.</summary>
    public void Reset()
    {
        Result = null;
        ResultNotes.Clear();
        ResultMessage = null;
        ErrorMessage = null;
        State = RunState.Idle;
    }

    /// <param name="format">The conversion as the cards say it: "PNG → DWG".</param>
    /// <param name="targetLabel">The result's format as errors name it: "DWG".</param>
    public async Task RunAsync(ConversionPipeline pipeline, ConversionJob job, string format, string targetLabel)
    {
        _lastPipeline = pipeline;
        _lastJob = job;
        _sourceName = Path.GetFileName(job.SourcePath);
        _format = format;
        _targetLabel = targetLabel;
        _lastStage = PipelineStage.Decode;
        _chunkCount = 0;
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(ProcessingTitle));
        OnPropertyChanged(nameof(ConversionFormat));
        ProcessingCaption = Loc.Format("Processing.Caption", targetLabel, job.DestinationPath);
        ResultMessage = null;
        ResultNotes.Clear();
        ProgressPercent = 0;
        ResetSteps();
        State = RunState.Processing;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        // Created here, on the UI thread, so progress reports come back to it.
        var progress = new Progress<PipelineProgress>(OnProgress);
        var started = System.Diagnostics.Stopwatch.StartNew();
        _log.Info($"Conversion started: {format}");
        try
        {
            // Hashing and verification run on a worker thread; the window stays responsive.
            var result = await Task.Run(() => pipeline.RunAsync(job, progress, cancellation.Token), CancellationToken.None);
            ResultNotes.Clear();
            foreach (var note in result.Notes ?? [])
            {
                ResultNotes.Add(new ResultNoteItem(note.Message, note.Severity == NoteSeverity.Warning ? InfoBarSeverity.Warning : InfoBarSeverity.Informational));
                _log.Info($"Conversion note ({note.Severity}): {format}");
            }

            Result = result;
            State = RunState.Done;
            _log.Info(string.Create(CultureInfo.InvariantCulture, $"Conversion verified: {format}, {result.ChunkCount} chunks, {started.Elapsed.TotalSeconds:0.0} s"));
            await RecordHistoryAsync(job, result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            State = RunState.Idle;
            _log.Info($"Conversion cancelled: {format}, at {_lastStage}");
            Cancelled?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorMessage = ErrorMessages.Describe(ex, _lastStage, targetLabel);
            State = RunState.Failed;
            _log.Error($"Conversion failed: {format}, at {_lastStage}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _cancellation = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_lastPipeline is not null && _lastJob is not null)
        {
            await RunAsync(_lastPipeline, _lastJob, _format, _targetLabel);
        }
    }

    [RelayCommand]
    private void Back()
    {
        State = RunState.Idle;
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Another()
    {
        Reset();
        AnotherRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ResetSteps()
    {
        Steps.Clear();
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.Decode")));
        Steps.Add(new ConversionStepViewModel(Loc.Format("Step.Encode", _targetLabel)));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.VerifyChunks")));
        Steps.Add(new ConversionStepViewModel(Loc.Get("Step.VerifyIntegrity")));
    }

    private void OnProgress(PipelineProgress progress)
    {
        if (State != RunState.Processing)
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
        PipelineStage.Decode => progress.Detail ?? Loc.Format("Step.Reading", _sourceName),
        PipelineStage.Encode => progress.Detail ?? Loc.Format("Step.Encoding", FileExtension.Normalize(_lastJob?.TargetExtension ?? string.Empty)),
        PipelineStage.VerifyChunks when progress.ChunkNumber > 0 => Loc.Format("Step.ChunkOf", progress.ChunkNumber, progress.ChunkCount),
        PipelineStage.VerifyChunks => Loc.Get("Step.RereadingFromDisk"),
        _ => Loc.Get(progress.StageFraction < 0.5 ? "Step.ComputingHash" : "Step.Reopening"),
    };

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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ResultMessage = Loc.Get("History.RecordFailed");
        }
    }
}

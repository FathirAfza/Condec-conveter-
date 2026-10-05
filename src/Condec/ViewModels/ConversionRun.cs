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

/// <summary>One result of a batch: the job, the pipeline that makes it, and the name the cards and history give its source ("gambar.pdf, halaman 2").</summary>
public sealed record BatchEntry(BatchJob Job, string SourceName);

/// <summary>
/// One conversion from start to finish for a page that has its own input (Architecture): the progress list, the finished
/// card, the failed card, and the history entry. The page decides what to show while this is <see cref="RunState.Idle"/>.
/// A batch (several results saved into one folder, DESIGN §6.1.3) runs here too, one file after another.
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
    private string? _historyName;
    private string _targetExtension = string.Empty;
    private List<BatchEntry> _batch = [];
    private int _batchCount;

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

    /// <summary>The page is Compress Image: its history entries say "Kompres" (DESIGN §6.5).</summary>
    public bool RecordsCompression { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(IsProcessing), nameof(IsDone), nameof(IsFailed), nameof(IsSingleDone), nameof(IsBatchDone))]
    public partial RunState State { get; set; }

    /// <summary>A conversion is running or its card is showing.</summary>
    public bool IsActive => State != RunState.Idle;

    public bool IsProcessing => State == RunState.Processing;

    public bool IsDone => State == RunState.Done;

    public bool IsFailed => State == RunState.Failed;

    public string SourceName => _sourceName;

    public string ProcessingTitle => IsBatch ? Loc.Format("Batch.ProcessingTitle", _batchCount) : Loc.Format("Processing.Title", _sourceName);

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
        IsBatch = false;
        BatchResults.Clear();
        _batch = [];
        State = RunState.Idle;
    }

    // ---- A batch ----

    /// <summary>Several results into one folder: the processing and finished cards show the list instead of one file.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingle), nameof(ProcessingTitle), nameof(IsSingleDone), nameof(IsBatchDone))]
    public partial bool IsBatch { get; set; }

    public bool IsSingle => !IsBatch;

    public bool IsSingleDone => IsDone && !IsBatch;

    public bool IsBatchDone => IsDone && IsBatch;

    public ObservableCollection<BatchResultItem> BatchResults { get; } = [];

    [ObservableProperty]
    public partial string BatchFolder { get; set; } = string.Empty;

    /// <summary>"File 2 dari 5: gambar.pdf, halaman 2", above the four stages.</summary>
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

    /// <summary>At least one result was not saved: it can be made again.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryBatchCommand))]
    public partial bool BatchHasUnsaved { get; set; }

    /// <summary>"Coba lagi file yang gagal" when something failed; "Konversi sisanya" when the rest was only stopped.</summary>
    [ObservableProperty]
    public partial string BatchRetryLabel { get; set; } = string.Empty;

    /// <summary>Nothing was saved: "Ubah pilihan" is offered instead of opening an empty folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchHasSaved))]
    public partial bool BatchSavedNothing { get; set; }

    public bool BatchHasSaved => !BatchSavedNothing;

    /// <summary>Converts every entry, one after another, into <paramref name="folder"/>. Result n belongs to entry n.</summary>
    /// <param name="format">The conversion as the cards say it: "PDF → DWG".</param>
    /// <param name="targetLabel">The results' format as errors name it: "DWG".</param>
    public async Task RunBatchAsync(IReadOnlyList<BatchEntry> entries, string folder, string format, string targetLabel)
    {
        _batch = [.. entries];
        _format = format;
        _targetLabel = targetLabel;
        BatchFolder = folder;
        BatchResults.Clear();
        for (var i = 0; i < _batch.Count; i++)
        {
            BatchResults.Add(new BatchResultItem(i + 1, _batch[i].SourceName, Path.GetFileName(_batch[i].Job.Job.DestinationPath), ShowPathInFolder));
        }

        OnPropertyChanged(nameof(ConversionFormat));
        await RunBatchRowsAsync([.. Enumerable.Range(0, _batch.Count)]);
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Runs the rows <paramref name="rows"/> of the batch: all of them, or the ones that weren't saved.</summary>
    private async Task RunBatchRowsAsync(IReadOnlyList<int> rows)
    {
        var jobs = rows.Select(r => _batch[r].Job).ToList();
        foreach (var row in rows)
        {
            BatchResults[row].State = BatchRowState.Waiting;
            BatchResults[row].Detail = null;
            BatchResults[row].OutputPath = null;
        }

        _batchCount = jobs.Count;
        _targetExtension = jobs[0].Job.TargetExtension;
        IsBatch = true;
        _lastStage = PipelineStage.Decode;
        _chunkCount = 0;
        ResultMessage = null;
        ProcessingCaption = Loc.Format("Batch.ProcessingCaption", _targetLabel, BatchFolder);
        BatchCurrentText = string.Empty;
        ProgressPercent = 0;
        ResetSteps();
        OnPropertyChanged(nameof(ProcessingTitle));
        State = RunState.Processing;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var started = System.Diagnostics.Stopwatch.StartNew();
        _log.Info($"Batch started: {_format}, {jobs.Count} files");

        // Created here, on the UI thread, so progress reports come back to it.
        var progress = new Progress<BatchProgress>(p => OnBatchProgress(p, rows));
        var outcomes = await ConversionBatch.RunAsync(jobs, progress, r => OnBatchItemFinishedAsync(r, rows[r.Index]), Exists, cancellation.Token);
        _cancellation = null;

        var done = BatchResults.Count(r => r.IsDone);
        var failed = BatchResults.Count(r => r.IsFailed);
        var notSaved = BatchResults.Count - done;
        _log.Info(string.Create(CultureInfo.InvariantCulture, $"Batch finished: {_format}, {done} saved, {failed} failed, {notSaved - failed} not converted, {started.Elapsed.TotalSeconds:0.0} s"));

        if (done == 0 && failed == 0)
        {
            // Stopped before anything was saved: back to the page's choices, as a single conversion does.
            Reset();
            Cancelled?.Invoke(this, EventArgs.Empty);
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
        State = RunState.Done;
    }

    private void OnBatchProgress(BatchProgress progress, IReadOnlyList<int> rows)
    {
        if (State != RunState.Processing)
        {
            return;
        }

        var row = BatchResults[rows[progress.Index]];
        _sourceName = row.SourceName;
        BatchCurrentText = Loc.Format("Batch.Current", progress.Index + 1, progress.Count, row.SourceName);

        // Rounded down, so 100% only shows once the last file is really saved.
        ProgressPercent = Math.Floor(progress.OverallFraction * 100);
        UpdateSteps(progress.Item);
    }

    private async Task OnBatchItemFinishedAsync(BatchItemResult result, int rowIndex)
    {
        var row = BatchResults[rowIndex];
        row.OutputName = Path.GetFileName(result.Job.DestinationPath);
        switch (result.Outcome)
        {
            case BatchItemOutcome.Done:
                row.OutputPath = result.Result!.OutputPath;
                row.Detail = result.Result.Notes?.FirstOrDefault()?.Message;
                row.State = BatchRowState.Done;

                // A retry keeps the name this file was really saved under.
                _batch[rowIndex] = _batch[rowIndex] with { Job = _batch[rowIndex].Job with { Job = result.Job } };
                await RecordHistoryAsync(result.Job, result.Result, row.SourceName);
                break;
            case BatchItemOutcome.Failed:
                row.Detail = ErrorMessages.Describe(result.Error!, result.LastStage, _targetLabel);
                row.State = BatchRowState.Failed;
                _log.Error($"Batch file failed: {_format}, at {result.LastStage}: {result.Error!.GetType().Name}: {result.Error.Message}");
                break;
            default:
                row.State = BatchRowState.Cancelled;
                break;
        }

        // Each file is a step of its own: the list starts fresh for the next one.
        _chunkCount = 0;
        _lastStage = PipelineStage.Decode;
    }

    /// <summary>"Coba lagi file yang gagal" / "Konversi sisanya": the results that weren't saved, again, into the same folder.</summary>
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

    /// <param name="format">The conversion as the cards say it: "PNG → DWG".</param>
    /// <param name="targetLabel">The result's format as errors name it: "DWG".</param>
    /// <param name="sourceName">The name the cards and history give the source; the file name when null.</param>
    public async Task RunAsync(ConversionPipeline pipeline, ConversionJob job, string format, string targetLabel, string? sourceName = null)
    {
        IsBatch = false;
        BatchResults.Clear();
        _historyName = sourceName;
        _lastPipeline = pipeline;
        _lastJob = job;
        _sourceName = sourceName ?? Path.GetFileName(job.SourcePath);
        _targetExtension = job.TargetExtension;
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
            await RecordHistoryAsync(job, result, _sourceName);
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
            await RunAsync(_lastPipeline, _lastJob, _format, _targetLabel, _historyName);
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

        // Rounded down, so 100% only shows once the file is really saved.
        ProgressPercent = Math.Floor(progress.OverallFraction * 100);
        UpdateSteps(progress);
    }

    /// <summary>The four stages for the file being converted (the only one, or the current one of a batch).</summary>
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
        PipelineStage.Decode => progress.Detail ?? Loc.Format("Step.Reading", _sourceName),
        PipelineStage.Encode => progress.Detail ?? Loc.Format("Step.Encoding", FileExtension.Normalize(_targetExtension)),
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

    /// <param name="sourceName">"gambar.pdf, halaman 2" for one page of a file, the file name otherwise.</param>
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
                VerificationStatus.Verified,
                Compressed: RecordsCompression));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ResultMessage = Loc.Get("History.RecordFailed");
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Pipeline;

namespace Condec.Core.Batch;

/// <summary>One result of a batch, run through the pipeline that can make it (Architecture uses two).</summary>
public sealed record BatchJob(ConversionJob Job, ConversionPipeline Pipeline);

public enum BatchItemOutcome
{
    Done,
    Failed,

    /// <summary>The user stopped the batch before or while this file was made; nothing of it was saved.</summary>
    Cancelled,
}

/// <param name="Index">Position in the batch, from 0: result n belongs to job n.</param>
/// <param name="Job">The job as it ran: its destination may have moved to a free " (n)" name just before it started.</param>
/// <param name="LastStage">Where a failed file stopped, for the error text.</param>
public sealed record BatchItemResult(int Index, ConversionJob Job, BatchItemOutcome Outcome, ConversionResult? Result, Exception? Error, PipelineStage LastStage);

/// <param name="Index">The file being made, from 0.</param>
/// <param name="Item">That file's own progress through the four stages.</param>
public sealed record BatchProgress(int Index, int Count, PipelineProgress Item)
{
    /// <summary>The whole batch, 0 to 1: every file weighs the same.</summary>
    public double OverallFraction => Count == 0 ? 1 : (Index + Item.OverallFraction) / Count;
}

/// <summary>
/// Runs a batch one file after another (DESIGN §6.1, owner decision 2026-10-03). A file that fails doesn't stop the rest; a
/// cancel stops the file being made (its temporary file is removed by the pipeline) and every file after it. Each file is
/// saved and verified on its own, exactly like a single conversion.
/// </summary>
/// <remarks>
/// The awaits here continue on the caller's context on purpose: <paramref name="onFinished"/> and the progress run on the UI
/// thread when the batch is started from it, so the page can update its list and write the history between files.
/// </remarks>
public static class ConversionBatch
{
    /// <param name="onFinished">Called after each file, done, failed or cancelled, before the next one starts.</param>
    /// <param name="exists">Whether a path is taken on disk; File.Exists in the app.</param>
    public static async Task<IReadOnlyList<BatchItemResult>> RunAsync(
        IReadOnlyList<BatchJob> jobs,
        IProgress<BatchProgress>? progress,
        Func<BatchItemResult, Task>? onFinished,
        Func<string, bool> exists,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var results = new List<BatchItemResult>(jobs.Count);

        // Every planned name stays reserved for its own file, so a file moved to "name (2)" can't take a later file's name.
        var planned = new HashSet<string>(jobs.Select(j => Path.GetFullPath(j.Job.DestinationPath)), StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < jobs.Count; index++)
        {
            var (job, pipeline) = jobs[index];
            BatchItemResult result;
            if (ct.IsCancellationRequested)
            {
                result = new BatchItemResult(index, job, BatchItemOutcome.Cancelled, null, null, PipelineStage.Decode);
            }
            else
            {
                var stage = PipelineStage.Decode;
                var count = jobs.Count;
                var current = index;
                var itemProgress = new ForwardingProgress(p =>
                {
                    stage = p.Stage;
                    progress?.Report(new BatchProgress(current, count, p));
                });

                var own = Path.GetFullPath(job.DestinationPath);
                planned.Remove(own);
                try
                {
                    // Something may have appeared in the folder since the names were planned; it is never overwritten.
                    job = job with { DestinationPath = OutputNames.Free(own, p => planned.Contains(p) || exists(p)) };
                    var saved = await Task.Run(() => pipeline.RunAsync(job, itemProgress, ct), CancellationToken.None);
                    result = new BatchItemResult(index, job, BatchItemOutcome.Done, saved, null, PipelineStage.VerifyIntegrity);
                }
                catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
                {
                    result = new BatchItemResult(index, job, BatchItemOutcome.Cancelled, null, ex, stage);
                }
                catch (Exception ex)
                {
                    result = new BatchItemResult(index, job, BatchItemOutcome.Failed, null, ex, stage);
                }
                finally
                {
                    planned.Add(Path.GetFullPath(job.DestinationPath));
                }
            }

            results.Add(result);
            if (onFinished is not null)
            {
                await onFinished(result);
            }
        }

        return results;
    }

    /// <summary>
    /// Reports straight through on the pipeline's thread. The caller's IProgress (a Progress&lt;T&gt; made on the UI thread)
    /// does the marshalling; the stage is remembered here so a failure can say where it stopped.
    /// </summary>
    private sealed class ForwardingProgress(Action<PipelineProgress> report) : IProgress<PipelineProgress>
    {
        public void Report(PipelineProgress value) => report(value);
    }
}

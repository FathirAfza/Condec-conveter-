using Condec.Core.Conversion;
using Condec.Core.Formats;

namespace Condec.Core.Pipeline;

/// <param name="DestinationPath">The final path, normally chosen in the save picker. Its folder must exist.</param>
public sealed record ConversionJob(
    string SourcePath,
    string TargetExtension,
    string DestinationPath,
    ConversionOptions? Options = null);

/// <param name="Sha256">SHA-256 of the saved file, as lower-case hex.</param>
public sealed record ConversionResult(string OutputPath, long Length, int ChunkCount, string Sha256);

/// <summary>
/// Runs one conversion through Decode, Encode, VerifyChunks and VerifyIntegrity. The output goes to a
/// temporary file next to the destination and is moved into place only after every check passed.
/// On failure or cancellation the temporary file is deleted, so no half-written file is ever left behind.
/// </summary>
public sealed class ConversionPipeline
{
    public const string TempExtension = ".condec-tmp";

    private readonly ConverterRegistry _registry;
    private readonly TempFileJournal _journal;
    private readonly int _chunkSize;

    public ConversionPipeline(ConverterRegistry registry, TempFileJournal journal)
        : this(registry, journal, ChunkHasher.DefaultChunkSize)
    {
    }

    /// <summary>Lets tests use small chunks.</summary>
    internal ConversionPipeline(ConverterRegistry registry, TempFileJournal journal, int chunkSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        _registry = registry;
        _journal = journal;
        _chunkSize = chunkSize;
    }

    public async Task<ConversionResult> RunAsync(ConversionJob job, IProgress<PipelineProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);

        var sourcePath = Path.GetFullPath(job.SourcePath);
        var destinationPath = Path.GetFullPath(job.DestinationPath);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The output would overwrite the source file.", nameof(job));
        }

        var sourceExtension = FileExtension.FromPath(sourcePath);
        var targetExtension = FileExtension.Normalize(job.TargetExtension);
        var converter = (sourceExtension.Length == 0 ? null : _registry.FindConverter(sourceExtension, targetExtension))
            ?? throw new NotSupportedException($"No available converter from '{sourceExtension}' to '{targetExtension}'.");

        // Unverified output is never kept, so a format without a validator can't be a target at all.
        var validator = _registry.FindValidator(targetExtension)
            ?? throw new NotSupportedException($"No output validator for '{targetExtension}'.");

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The source file doesn't exist.", sourcePath);
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (destinationDirectory is null || !Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException($"The destination folder doesn't exist: '{destinationDirectory}'.");
        }

        ct.ThrowIfCancellationRequested();

        var reporter = new StageReporter(progress);
        var tempPath = ChooseTempPath(destinationPath);
        using var lease = _journal.Register(tempPath);
        var moved = false;
        try
        {
            reporter.Report(PipelineStage.Decode, 0);
            var manifest = await EncodeAsync(
                converter,
                new ConversionRequest(sourcePath, sourceExtension, targetExtension, Stream.Null, job.Options),
                tempPath,
                reporter,
                ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            var chunkCount = manifest.ChunkCount;
            reporter.Report(PipelineStage.VerifyChunks, chunkCount == 0 ? 1 : 0, chunkCount: chunkCount);
            await OutputVerifier.VerifyChunksAsync(
                tempPath,
                manifest,
                (number, count) => reporter.Report(PipelineStage.VerifyChunks, (double)number / count, number, count),
                ct).ConfigureAwait(false);

            // Hashing the whole file is the first half of VerifyIntegrity, reopening it with a decoder the second.
            ct.ThrowIfCancellationRequested();
            reporter.Report(PipelineStage.VerifyIntegrity, 0, chunkCount: chunkCount);
            await OutputVerifier.VerifyFileHashAsync(
                tempPath,
                manifest,
                fraction => reporter.Report(PipelineStage.VerifyIntegrity, fraction / 2, chunkCount: chunkCount),
                ct).ConfigureAwait(false);
            await ValidateAsync(validator, tempPath, targetExtension, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            File.Move(tempPath, destinationPath, overwrite: true);
            moved = true;
            reporter.Report(PipelineStage.VerifyIntegrity, 1, chunkCount: chunkCount);

            return new ConversionResult(destinationPath, manifest.Length, chunkCount, Convert.ToHexStringLower(manifest.FileHash));
        }
        finally
        {
            // If the delete fails, the journal entry stays and the next start cleans the file up.
            if (moved || TryDelete(tempPath))
            {
                lease.MarkResolved();
            }
        }
    }

    private async Task<WriteManifest> EncodeAsync(
        IConverter converter,
        ConversionRequest request,
        string tempPath,
        StageReporter reporter,
        CancellationToken ct)
    {
        using var hasher = new ChunkHasher(_chunkSize);
        using (var file = new FileStream(tempPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        }))
        {
            using var output = new ChunkHashingStream(file, hasher, ct);
            await converter.ConvertAsync(request with { Output = output }, new ConverterProgress(reporter), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // Push FileStream's buffer and the OS cache to the disk before the file is read back.
            file.Flush(flushToDisk: true);
        }

        reporter.Report(PipelineStage.Encode, 1);
        return hasher.Complete();
    }

    private static async Task ValidateAsync(IOutputValidator validator, string path, string targetExtension, CancellationToken ct)
    {
        try
        {
            await validator.ValidateAsync(path, targetExtension, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not VerificationFailedException)
        {
            throw new VerificationFailedException(
                VerificationFailure.InvalidOutput,
                $"The output can't be opened as {targetExtension}.",
                innerException: ex);
        }
    }

    /// <summary>
    /// "&lt;name&gt;.condec-tmp" next to the destination, so the final move is a rename on the same volume.
    /// An existing file with that name isn't ours to delete, so a numbered name is used instead.
    /// </summary>
    private static string ChooseTempPath(string destinationPath)
    {
        var candidate = destinationPath + TempExtension;
        for (var n = 2; File.Exists(candidate); n++)
        {
            candidate = $"{destinationPath}.{n}{TempExtension}";
        }

        return candidate;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class StageReporter(IProgress<PipelineProgress>? target)
    {
        public void Report(PipelineStage stage, double fraction, int chunkNumber = 0, int chunkCount = 0, string? detail = null)
        {
            if (target is null)
            {
                return;
            }

            var clamped = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
            var (start, end) = PipelineProgress.GetRange(stage);
            target.Report(new PipelineProgress(stage, clamped, start + ((end - start) * clamped), chunkNumber, chunkCount, detail));
        }
    }

    private sealed class ConverterProgress(StageReporter reporter) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => reporter.Report(
            value.Stage == ConversionStage.Decode ? PipelineStage.Decode : PipelineStage.Encode,
            value.Fraction,
            detail: value.Detail);
    }
}

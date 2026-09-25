using System.Text;

namespace Condec.Core.Pipeline;

/// <summary>
/// Records every temporary output while it exists, so a crash or power cut can't leave a half-written
/// file behind for good: <see cref="CleanupStale"/> deletes the leftovers the next time the app starts.
/// </summary>
/// <remarks>
/// Each conversion gets its own entry file, held open without sharing until the conversion ends.
/// A second running instance therefore can't open it and leaves that conversion alone, while a crash
/// closes the handle and exposes the entry to the next cleanup.
/// </remarks>
public sealed class TempFileJournal
{
    internal const string EntryExtension = ".pending";

    private readonly string _directory;

    public TempFileJournal(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    /// <summary>Deletes temporary outputs left by conversions that never finished. Returns how many were deleted.</summary>
    public int CleanupStale()
    {
        if (!Directory.Exists(_directory))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var entryPath in Directory.EnumerateFiles(_directory, "*" + EntryExtension))
        {
            string tempPath;
            try
            {
                using var entry = new FileStream(entryPath, FileMode.Open, FileAccess.Read, FileShare.None);
                using var reader = new StreamReader(entry, Encoding.UTF8);
                tempPath = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still held by a conversion that is running right now.
                continue;
            }

            // Only ever delete our own temporary files, even if an entry was damaged or tampered with.
            if (IsTempOutputPath(tempPath) && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Keep the entry and try again on the next start.
                    continue;
                }
            }

            TryDelete(entryPath);
        }

        return deleted;
    }

    /// <summary>Records <paramref name="tempPath"/> before the file is created.</summary>
    internal TempFileLease Register(string tempPath)
    {
        var fullPath = Path.GetFullPath(tempPath);
        if (!IsTempOutputPath(fullPath))
        {
            throw new ArgumentException($"Only {ConversionPipeline.TempExtension} files can be journaled.", nameof(tempPath));
        }

        Directory.CreateDirectory(_directory);
        var entryPath = Path.Combine(_directory, Guid.NewGuid().ToString("N") + EntryExtension);
        var handle = new FileStream(entryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        try
        {
            handle.Write(Encoding.UTF8.GetBytes(fullPath));
            handle.Flush(flushToDisk: true);
        }
        catch
        {
            handle.Dispose();
            TryDelete(entryPath);
            throw;
        }

        return new TempFileLease(handle, entryPath);
    }

    private static bool IsTempOutputPath(string path) =>
        Path.IsPathFullyQualified(path)
        && path.EndsWith(ConversionPipeline.TempExtension, StringComparison.OrdinalIgnoreCase);

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>A journal entry for one temporary output. Disposing it keeps the entry unless it was resolved.</summary>
internal sealed class TempFileLease : IDisposable
{
    private readonly FileStream _handle;
    private readonly string _entryPath;
    private bool _resolved;
    private bool _disposed;

    internal TempFileLease(FileStream handle, string entryPath)
    {
        _handle = handle;
        _entryPath = entryPath;
    }

    /// <summary>The temporary file has been moved to its final path or deleted, so nothing is left to clean up.</summary>
    public void MarkResolved() => _resolved = true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handle.Dispose();
        if (_resolved)
        {
            TempFileJournal.TryDelete(_entryPath);
        }
    }
}

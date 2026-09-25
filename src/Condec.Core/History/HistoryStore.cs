using System.Text.Json;
using System.Text.Json.Serialization;

namespace Condec.Core.History;

public enum VerificationStatus
{
    Verified,
    Failed,
}

/// <summary>One finished conversion. Only metadata: nothing of the file's content is stored.</summary>
public sealed record HistoryEntry(
    string SourceFileName,
    string SourceExtension,
    string TargetExtension,
    DateTimeOffset CompletedAt,
    string OutputPath,
    VerificationStatus Verification);

/// <summary>
/// The conversion history in <c>history.json</c>, newest first. Every change is written atomically
/// (temporary file, then rename), so a crash can't leave a half-written history behind.
/// </summary>
public sealed class HistoryStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HistoryDocument _document = new();

    public HistoryStore(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
    }

    /// <summary>When off, <see cref="AddAsync"/> records nothing. Existing entries stay until cleared.</summary>
    public bool IsEnabled => _document.Enabled;

    /// <summary>A snapshot, newest first.</summary>
    public IReadOnlyList<HistoryEntry> Entries => [.. _document.Entries];

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _document = await ReadAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task AddAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ChangeAsync(
            document =>
            {
                if (!document.Enabled)
                {
                    return false;
                }

                document.Entries.Insert(0, entry);
                return true;
            },
            ct);
    }

    public Task ClearAsync(CancellationToken ct = default) =>
        ChangeAsync(
            document =>
            {
                document.Entries.Clear();
                return true;
            },
            ct);

    public Task SetEnabledAsync(bool enabled, CancellationToken ct = default) =>
        ChangeAsync(
            document =>
            {
                document.Enabled = enabled;
                return true;
            },
            ct);

    private async Task ChangeAsync(Func<HistoryDocument, bool> change, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (change(_document))
            {
                await WriteAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HistoryDocument> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return new HistoryDocument();
        }

        try
        {
            using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync(stream, HistoryJsonContext.Default.HistoryDocument, ct).ConfigureAwait(false)
                ?? new HistoryDocument();
        }
        catch (JsonException)
        {
            // A damaged file costs the history list, never the app. It is replaced on the next change.
            return new HistoryDocument();
        }
    }

    private async Task WriteAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = _filePath + ".tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, _document, HistoryJsonContext.Default.HistoryDocument, ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }
    }
}

internal sealed class HistoryDocument
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public List<HistoryEntry> Entries { get; set; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(HistoryDocument))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext
{
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;

namespace Condec.Tests;

/// <summary>A fresh folder under %TEMP% per test, never the real %LOCALAPPDATA%\Condec.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "condec-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class TestData
{
    public static byte[] Bytes(int length, int seed = 1)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}

internal sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => onReport(value);
}

internal delegate Task ConvertBody(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct);

internal sealed class FakeConverter(string source, params string[] targets) : IConverter
{
    public ConvertBody Body { get; init; } = (_, _, _) => Task.CompletedTask;

    public IReadOnlyList<string> GetTargets(string sourceExtension) => sourceExtension == source ? targets : [];

    public Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct) =>
        Body(request, progress, ct);

    /// <summary>Writes <paramref name="payload"/> in uneven pieces, reporting both converter stages.</summary>
    public static ConvertBody Writing(byte[] payload) => async (request, progress, ct) =>
    {
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1, "decoded"));
        var written = 0;
        foreach (var size in PieceSizes())
        {
            var take = Math.Min(size, payload.Length - written);
            await request.Output.WriteAsync(payload.AsMemory(written, take), ct);
            written += take;
            progress.Report(new ConversionProgress(ConversionStage.Encode, (double)written / payload.Length));
            if (written == payload.Length)
            {
                break;
            }
        }
    };

    private static IEnumerable<int> PieceSizes()
    {
        int[] sizes = [7, 50, 1, 100, 13];
        for (var i = 0; ; i++)
        {
            yield return sizes[i % sizes.Length];
        }
    }
}

internal sealed class FakeToolConverter(string source, ExternalToolStatus status, params string[] targets) : IExternalToolConverter
{
    public IReadOnlyList<string> GetTargets(string sourceExtension) => sourceExtension == source ? targets : [];

    public Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct) =>
        Task.CompletedTask;

    public ExternalToolStatus GetToolStatus() => status;
}

internal sealed class FakeValidator(string extension, Func<string, Task>? check = null) : IOutputValidator
{
    public List<string> ValidatedPaths { get; } = [];

    public bool CanValidate(string targetExtension) => targetExtension == extension;

    public async Task ValidateAsync(string path, string targetExtension, CancellationToken ct)
    {
        ValidatedPaths.Add(path);
        if (check is not null)
        {
            await check(path);
        }
    }
}

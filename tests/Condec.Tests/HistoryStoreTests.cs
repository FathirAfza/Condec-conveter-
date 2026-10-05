// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.History;

namespace Condec.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly string _file;

    public HistoryStoreTests()
    {
        _file = Path.Combine(_dir.Path, "Condec", "history.json");
    }

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MissingFile_MeansEmptyAndEnabled()
    {
        var store = new HistoryStore(_file);

        await store.LoadAsync(Ct);

        Assert.Empty(store.Entries);
        Assert.True(store.IsEnabled);
    }

    [Fact]
    public async Task Entries_SurviveReload_NewestFirst()
    {
        var store = new HistoryStore(_file);
        await store.AddAsync(Entry("tugas-biologi.pptx", ".pptx", ".pdf", 1), Ct);
        await store.AddAsync(Entry("foto-praktikum.heic", ".heic", ".jpg", 2), Ct);

        var reloaded = new HistoryStore(_file);
        await reloaded.LoadAsync(Ct);

        Assert.Equal(["foto-praktikum.heic", "tugas-biologi.pptx"], reloaded.Entries.Select(e => e.SourceFileName));
        Assert.Equal(Entry("foto-praktikum.heic", ".heic", ".jpg", 2), reloaded.Entries[0]);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_file)!, "*.tmp"));
    }

    [Fact]
    public async Task AnUpscale_KeepsItsScale_AndAnOldEntryWithoutOneStillReads()
    {
        var store = new HistoryStore(_file);
        var upscale = Entry("foto.jpg", ".jpg", ".png", 3) with { UpscaleScale = 2.5 };
        await store.AddAsync(upscale, Ct);

        var reloaded = new HistoryStore(_file);
        await reloaded.LoadAsync(Ct);
        Assert.Equal(2.5, reloaded.Entries[0].UpscaleScale);
        Assert.Equal(upscale, reloaded.Entries[0]);

        // A history.json written before upscales were recorded has no such field.
        await File.WriteAllTextAsync(_file, """
            {"enabled":true,"entries":[{"sourceFileName":"a.docx","sourceExtension":".docx","targetExtension":".pdf","completedAt":"2026-10-01T10:00:00+07:00","outputPath":"C:\\a.pdf","verification":"Verified"}]}
            """, Ct);
        var old = new HistoryStore(_file);
        await old.LoadAsync(Ct);
        Assert.Equal("a.docx", Assert.Single(old.Entries).SourceFileName);
        Assert.Null(old.Entries[0].UpscaleScale);
        Assert.False(old.Entries[0].Compressed);
    }

    [Fact]
    public async Task ACompression_IsMarked()
    {
        var store = new HistoryStore(_file);
        var compressed = Entry("foto.heic", ".heic", ".jpg", 1) with { Compressed = true };
        await store.AddAsync(compressed, Ct);

        var reloaded = new HistoryStore(_file);
        await reloaded.LoadAsync(Ct);
        Assert.True(reloaded.Entries[0].Compressed);
        Assert.Equal(compressed, reloaded.Entries[0]);
    }

    [Fact]
    public async Task Clear_IsPersisted()
    {
        var store = new HistoryStore(_file);
        await store.AddAsync(Entry("a.docx", ".docx", ".pdf", 1), Ct);

        await store.ClearAsync(Ct);
        var reloaded = new HistoryStore(_file);
        await reloaded.LoadAsync(Ct);

        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public async Task Disabled_RecordsNothing_AndStaysDisabled()
    {
        var store = new HistoryStore(_file);
        await store.SetEnabledAsync(false, Ct);

        await store.AddAsync(Entry("a.docx", ".docx", ".pdf", 1), Ct);
        var reloaded = new HistoryStore(_file);
        await reloaded.LoadAsync(Ct);

        Assert.Empty(reloaded.Entries);
        Assert.False(reloaded.IsEnabled);
    }

    [Fact]
    public async Task DamagedFile_LoadsAsEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        await File.WriteAllTextAsync(_file, "{ bukan json", Ct);
        var store = new HistoryStore(_file);

        await store.LoadAsync(Ct);

        Assert.Empty(store.Entries);
        Assert.True(store.IsEnabled);
    }

    private static HistoryEntry Entry(string name, string source, string target, int minutesAfter) => new(
        name,
        source,
        target,
        new DateTimeOffset(2026, 9, 24, 10, minutesAfter, 0, TimeSpan.FromHours(7)),
        @"C:\Users\x\Documents\" + Path.ChangeExtension(name, target),
        VerificationStatus.Verified);
}

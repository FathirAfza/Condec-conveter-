using Condec.Core.Pipeline;

namespace Condec.Tests;

public sealed class TempFileJournalTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly string _journalDirectory;
    private readonly TempFileJournal _journal;

    public TempFileJournalTests()
    {
        _journalDirectory = _dir.File("journal");
        _journal = new TempFileJournal(_journalDirectory);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void RunningConversion_IsLeftAlone()
    {
        var temp = _dir.File("denah.dxf.condec-tmp");
        using var lease = _journal.Register(temp);
        File.WriteAllText(temp, "sedang ditulis");

        Assert.Equal(0, _journal.CleanupStale());
        Assert.True(File.Exists(temp));
    }

    [Fact]
    public void ConversionThatNeverFinished_IsCleanedUpOnNextStart()
    {
        var temp = _dir.File("denah.dxf.condec-tmp");
        var lease = _journal.Register(temp);
        File.WriteAllText(temp, "setengah jadi");

        // Like a crash: the handle closes, but nothing marked the temp file as resolved.
        lease.Dispose();

        Assert.Equal(1, _journal.CleanupStale());
        Assert.False(File.Exists(temp));
        Assert.Empty(Directory.GetFiles(_journalDirectory));
    }

    [Fact]
    public void ResolvedLease_RemovesItsEntry()
    {
        var lease = _journal.Register(_dir.File("foto.png.condec-tmp"));

        lease.MarkResolved();
        lease.Dispose();

        Assert.Empty(Directory.GetFiles(_journalDirectory));
    }

    [Fact]
    public void Entry_PointingAtAnythingElse_NeverDeletesIt()
    {
        var document = _dir.File("skripsi.docx");
        File.WriteAllText(document, "penting");
        Directory.CreateDirectory(_journalDirectory);
        File.WriteAllText(Path.Combine(_journalDirectory, "tampered" + TempFileJournal.EntryExtension), document);

        Assert.Equal(0, _journal.CleanupStale());
        Assert.True(File.Exists(document));
        Assert.Empty(Directory.GetFiles(_journalDirectory));
    }

    [Fact]
    public void Register_RefusesNonTemporaryFiles()
    {
        Assert.Throws<ArgumentException>(() => _journal.Register(_dir.File("skripsi.docx")));
    }

    [Fact]
    public void Cleanup_WithoutJournalFolder_DoesNothing()
    {
        Assert.Equal(0, _journal.CleanupStale());
    }
}

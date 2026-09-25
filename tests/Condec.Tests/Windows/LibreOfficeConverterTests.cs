using System.Diagnostics;
using Condec.Core.Conversion;
using Condec.Core.Documents;
using Condec.Core.Pipeline;

namespace Condec.Tests.Documents;

/// <summary>
/// Sample documents made once by the installed LibreOffice, in a profile of its own. Tests that need
/// LibreOffice are skipped on machines without it.
/// </summary>
public sealed class LibreOfficeFixture : IDisposable
{
    private const string Presentation = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
            xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
            office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.presentation">
          <office:body><office:presentation><draw:page draw:name="Slide1">
            <draw:frame svg:x="2cm" svg:y="2cm" svg:width="12cm" svg:height="3cm"><draw:text-box><text:p>Halo dari Condec</text:p></draw:text-box></draw:frame>
          </draw:page></office:presentation></office:body>
        </office:document>
        """;

    public LibreOfficeFixture()
    {
        BundledSoffice = FindRepositoryBundle();
        Soffice = LibreOfficeLocator.FindInstalledSoffice() ?? BundledSoffice;
        if (Soffice is null)
        {
            return;
        }

        File.WriteAllText(Samples.File("dokumen.txt"), "Halo dari Condec.\r\nBaris kedua: é ü 漢字\r\n");
        File.WriteAllText(Samples.File("tabel.csv"), "Nama,Jumlah\r\nApel,3\r\nJeruk,5\r\n");
        File.WriteAllText(Samples.File("slide.fodp"), Presentation);

        Make("dokumen.txt", "docx");
        Make("tabel.csv", "xlsx");
        Make("slide.fodp", "pptx");
    }

    /// <summary>The installed LibreOffice, or else the packaged copy.</summary>
    public string? Soffice { get; }

    /// <summary>The copy from tools\fetch-libreoffice.ps1 that the MSIX packages, when it has been fetched.</summary>
    public string? BundledSoffice { get; }

    internal TempDirectory BundledProfile { get; } = new();

    internal TempDirectory Samples { get; } = new();

    internal TempDirectory Profile { get; } = new();

    public string Sample(string name) => Samples.File(name);

    public void Dispose()
    {
        Samples.Dispose();
        Profile.Dispose();
        BundledProfile.Dispose();
    }

    internal static string? FindRepositoryBundle()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Condec.sln")))
            {
                var soffice = Path.Combine(dir.FullName, "third_party", "libreoffice", "program", "soffice.exe");
                return File.Exists(soffice) ? soffice : null;
            }
        }

        return null;
    }

    private void Make(string input, string target) => Run(Soffice!, Profile.Path, Samples.File(input), target, Samples.Path);

    /// <summary>Runs LibreOffice directly to make a sample; <paramref name="convertTo"/> may carry a filter and options.</summary>
    internal static string Run(string soffice, string profile, string input, string convertTo, string outputDirectory)
    {
        var start = new ProcessStartInfo(soffice) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-env:UserInstallation=" + new Uri(profile).AbsoluteUri);
        foreach (var argument in (string[])["--headless", "--norestore", "--convert-to", convertTo, "--outdir", outputDirectory, input])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        var output = Path.Combine(outputDirectory, Path.ChangeExtension(Path.GetFileName(input), convertTo.Split(':')[0]));
        return File.Exists(output) ? output : throw new InvalidOperationException($"LibreOffice could not make {output}.");
    }
}

public sealed class LibreOfficeConverterTests(LibreOfficeFixture fixture) : IClassFixture<LibreOfficeFixture>, IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Conversions => new()
    {
        { "dokumen.txt", ".pdf" },
        { "dokumen.docx", ".pdf" },
        { "dokumen.docx", ".odt" },
        { "dokumen.docx", ".rtf" },
        { "tabel.xlsx", ".pdf" },
        { "tabel.xlsx", ".ods" },
        { "slide.pptx", ".pdf" },
        { "slide.pptx", ".odp" },
    };

    [Theory]
    [MemberData(nameof(Conversions))]
    public async Task Converts_AndPassesVerification(string sample, string target)
    {
        SkipWithoutLibreOffice();
        var destination = _dir.File("hasil" + target);

        var result = await CreatePipeline(CreateConverter()).RunAsync(new ConversionJob(fixture.Sample(sample), target, destination), null, Ct);

        Assert.True(File.Exists(destination));
        Assert.True(result.ChunkCount >= 1);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.File("staging")));
    }

    public static TheoryData<string, string> BundledConversions => new()
    {
        { "dokumen.docx", ".pdf" },
        { "dokumen.docx", ".odt" },
        { "tabel.xlsx", ".pdf" },
        { "slide.pptx", ".pdf" },
    };

    /// <summary>The trimmed copy the MSIX ships must still convert every document family.</summary>
    [Theory]
    [MemberData(nameof(BundledConversions))]
    public async Task BundledCopy_Converts(string sample, string target)
    {
        Assert.SkipWhen(fixture.BundledSoffice is null, "LibreOffice has not been fetched into third_party (tools\\fetch-libreoffice.ps1).");
        Directory.CreateDirectory(_dir.File("staging"));
        var converter = new LibreOfficeConverter(() => fixture.BundledSoffice, fixture.BundledProfile.Path, _dir.File("staging"));
        var destination = _dir.File("hasil" + target);

        await CreatePipeline(converter).RunAsync(new ConversionJob(fixture.Sample(sample), target, destination), null, Ct);

        Assert.True(File.Exists(destination));
    }

    [Fact]
    public async Task Fails_WhenLibreOfficeCantReadTheDocument()
    {
        SkipWithoutLibreOffice();
        var docx = File.ReadAllBytes(fixture.Sample("dokumen.docx"));
        var damaged = _dir.File("rusak.docx");
        File.WriteAllBytes(damaged, docx[..(docx.Length / 2)]);
        var destination = _dir.File("hasil.pdf");

        await Assert.ThrowsAsync<ExternalToolException>(() =>
            CreatePipeline(CreateConverter()).RunAsync(new ConversionJob(damaged, ".pdf", destination), null, Ct));

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Cancelling_StopsLibreOffice_AndKeepsNothing()
    {
        SkipWithoutLibreOffice();
        var destination = _dir.File("hasil.pdf");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreatePipeline(CreateConverter()).RunAsync(new ConversionJob(fixture.Sample("tabel.xlsx"), ".pdf", destination), null, cancellation.Token));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_dir.File("staging")));
    }

    [Fact]
    public void WithoutLibreOffice_DocumentFormatsAreListedDisabled()
    {
        var converter = new LibreOfficeConverter(() => null, _dir.File("profile"), _dir.File("staging"));
        var registry = new ConverterRegistry([converter], [new PdfOutputValidator()]);

        var options = registry.GetTargetOptions(".docx");

        Assert.Equal([".pdf", ".doc", ".odt", ".rtf"], options.Select(o => o.Extension));
        Assert.All(options, o => Assert.False(o.IsEnabled));
        Assert.All(options, o => Assert.Contains("LibreOffice", o.DisabledReason));
        Assert.Null(registry.FindConverter(".docx", ".pdf"));
        Assert.Contains(".docx", registry.GetSourceExtensions());
    }

    [Fact]
    public void ProfileIsPassedAsFileUri()
    {
        var arguments = LibreOfficeConverter.BuildArguments(@"C:\a b\in\dokumen.docx", ".pdf", @"C:\a b\out", @"C:\Data Condec\profil");

        Assert.Equal("-env:UserInstallation=file:///C:/Data%20Condec/profil", arguments[0]);
        Assert.Equal(["--headless", "--norestore", "--convert-to", "pdf", "--outdir", @"C:\a b\out", @"C:\a b\in\dokumen.docx"], arguments.Skip(1));
    }

    private void SkipWithoutLibreOffice() => Assert.SkipWhen(fixture.Soffice is null, "LibreOffice is not installed.");

    private LibreOfficeConverter CreateConverter()
    {
        Directory.CreateDirectory(_dir.File("staging"));
        return new LibreOfficeConverter(() => fixture.Soffice, fixture.Profile.Path, _dir.File("staging"));
    }

    private ConversionPipeline CreatePipeline(LibreOfficeConverter converter) => new(
        new ConverterRegistry([converter], [new PdfOutputValidator(), new OfficeDocumentValidator()]),
        new TempFileJournal(_dir.File("journal")));
}

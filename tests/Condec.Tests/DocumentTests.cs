using System.IO.Compression;
using System.Text;
using Condec.Core.Documents;

namespace Condec.Tests;

public sealed class DocumentFormatsTests
{
    [Theory]
    [InlineData(".docx", ".pdf,.docx,.doc,.odt,.rtf")]
    [InlineData(".txt", ".pdf,.docx,.doc,.odt,.rtf")]
    [InlineData(".xls", ".pdf,.xlsx,.xls,.ods")]
    [InlineData(".ppt", ".pdf,.pptx,.ppt,.odp")]
    [InlineData(".dwg", ".pdf")]
    [InlineData(".pdf", ".docx,.doc,.odt,.pptx,.ppt,.odp")]
    [InlineData(".png", "")]
    public void Targets_FollowTheLibreOfficeModule(string source, string expected) =>
        Assert.Equal(expected, string.Join(',', DocumentFormats.GetTargets(source)));

    [Theory]
    [InlineData(".pdf", ".docx", "writer_pdf_import")]
    [InlineData(".pdf", ".ppt", "impress_pdf_import")]
    [InlineData(".docx", ".pdf", null)]
    public void PdfImport_PicksTheModuleOfTheTarget(string source, string target, string? filter) =>
        Assert.Equal(filter, DocumentFormats.GetImportFilter(source, target));
}

public sealed class OfficeDocumentValidatorTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly OfficeDocumentValidator _validator = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(".docx", "word/document.xml", "document")]
    [InlineData(".xlsx", "xl/workbook.xml", "workbook")]
    [InlineData(".pptx", "ppt/presentation.xml", "presentation")]
    public async Task AcceptsOfficeOpenXml(string extension, string mainPart, string root)
    {
        var path = WriteZip(("[Content_Types].xml", "<Types/>"), (mainPart, $"<x:{root} xmlns:x=\"urn:test\"><x:body/></x:{root}>"));

        await _validator.ValidateAsync(path, extension, Ct);
    }

    [Theory]
    [InlineData(".odt", "application/vnd.oasis.opendocument.text")]
    [InlineData(".ods", "application/vnd.oasis.opendocument.spreadsheet")]
    [InlineData(".odp", "application/vnd.oasis.opendocument.presentation")]
    public async Task AcceptsOpenDocument(string extension, string mimeType)
    {
        var path = WriteZip(("mimetype", mimeType), ("content.xml", "<o:document-content xmlns:o=\"urn:test\"/>"));

        await _validator.ValidateAsync(path, extension, Ct);
    }

    [Fact]
    public async Task Rejects_MissingMainPart()
    {
        var path = WriteZip(("[Content_Types].xml", "<Types/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => _validator.ValidateAsync(path, ".docx", Ct));
    }

    [Fact]
    public async Task Rejects_WrongRootElement()
    {
        var path = WriteZip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<workbook/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => _validator.ValidateAsync(path, ".docx", Ct));
    }

    [Fact]
    public async Task Rejects_MalformedXml()
    {
        var path = WriteZip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<document><body>"));

        await Assert.ThrowsAnyAsync<System.Xml.XmlException>(() => _validator.ValidateAsync(path, ".docx", Ct));
    }

    [Fact]
    public async Task Rejects_OpenDocumentOfAnotherKind()
    {
        var path = WriteZip(("mimetype", "application/vnd.oasis.opendocument.text"), ("content.xml", "<document-content/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => _validator.ValidateAsync(path, ".ods", Ct));
    }

    [Fact]
    public async Task Rejects_TruncatedPackage()
    {
        var path = WriteZip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<document/>"));
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

        await Assert.ThrowsAsync<InvalidDataException>(() => _validator.ValidateAsync(path, ".docx", Ct));
    }

    [Theory]
    [InlineData(@"{\rtf1\ansi Halo}", true)]
    [InlineData("{\\rtf1\\ansi Halo}\r\n", true)]
    [InlineData(@"{\rtf1\ansi Halo", false)]
    [InlineData("Halo", false)]
    public async Task ChecksRtfGroups(string content, bool valid)
    {
        var path = _dir.File("hasil.rtf");
        File.WriteAllText(path, content);

        if (valid)
        {
            await _validator.ValidateAsync(path, ".rtf", Ct);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => _validator.ValidateAsync(path, ".rtf", Ct));
        }
    }

    [Fact]
    public void ValidatesOfficeFormatsOnly()
    {
        Assert.True(_validator.CanValidate(".DOCX"));
        Assert.True(_validator.CanValidate(".doc"));
        Assert.False(_validator.CanValidate(".pdf"));
    }

    [Theory]
    [InlineData(true, 3 * 512, true)]
    [InlineData(true, 512, false)]
    [InlineData(false, 3 * 512, false)]
    public async Task ChecksTheCompoundFileHeaderOfLegacyFormats(bool signature, int length, bool valid)
    {
        var bytes = new byte[length];
        if (signature)
        {
            new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(bytes, 0);
        }

        var path = _dir.File("hasil.condec-tmp");
        File.WriteAllBytes(path, bytes);

        if (valid)
        {
            await _validator.ValidateAsync(path, ".ppt", Ct);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => _validator.ValidateAsync(path, ".ppt", Ct));
        }
    }

    private string WriteZip(params (string Name, string Content)[] entries)
    {
        var path = _dir.File(Guid.NewGuid().ToString("N") + ".condec-tmp");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return path;
    }
}

public sealed class LibreOfficeProfileTests : IDisposable
{
    private const string Header =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<oor:items xmlns:oor=\"http://openoffice.org/2001/registry\" xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">\n";

    private const string UpdateItem =
        "<item oor:path=\"/org.openoffice.Office.Jobs/Jobs/org.openoffice.Office.Jobs:Job['UpdateCheck']/Arguments\">"
        + "<prop oor:name=\"AutoCheckEnabled\" oor:op=\"fuse\" oor:type=\"xs:boolean\"><value>{0}</value></prop></item>\n";

    private const string OtherItem =
        "<item oor:path=\"/org.openoffice.Setup/Office\"><prop oor:name=\"ooSetupInstCompleted\" oor:op=\"fuse\"><value>true</value></prop></item>\n";

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Xcu => Path.Combine(_dir.Path, "user", "registrymodifications.xcu");

    [Fact]
    public void DoesNothing_BeforeLibreOfficeCreatedTheProfile()
    {
        Assert.False(LibreOfficeProfile.DisableUpdateCheck(_dir.Path));
        Assert.False(File.Exists(Xcu));
    }

    [Fact]
    public void TurnsTheUpdateCheckOff_AndKeepsOtherSettings()
    {
        Write(Header + string.Format(UpdateItem, "true") + OtherItem + "</oor:items>");

        Assert.True(LibreOfficeProfile.DisableUpdateCheck(_dir.Path));

        var values = System.Xml.Linq.XDocument.Load(Xcu).Descendants("value").Select(v => v.Value);
        Assert.Equal(["false", "true"], values);
        Assert.False(LibreOfficeProfile.DisableUpdateCheck(_dir.Path));
    }

    [Fact]
    public void AddsTheSetting_WhenTheProfileHasNone()
    {
        Write(Header + OtherItem + "</oor:items>");

        Assert.True(LibreOfficeProfile.DisableUpdateCheck(_dir.Path));

        Assert.Contains("AutoCheckEnabled", File.ReadAllText(Xcu));
        Assert.False(LibreOfficeProfile.DisableUpdateCheck(_dir.Path));
    }

    [Fact]
    public void LeavesAnAlreadyDisabledProfileUntouched()
    {
        Write(Header + string.Format(UpdateItem, "false") + "</oor:items>");
        var before = File.GetLastWriteTimeUtc(Xcu);

        Assert.False(LibreOfficeProfile.DisableUpdateCheck(_dir.Path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(Xcu));
    }

    private void Write(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Xcu)!);
        File.WriteAllText(Xcu, content);
    }
}

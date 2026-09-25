// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Documents;
using Condec.Core.Imaging;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using Windows.Graphics.Imaging;

namespace Condec.Tests.Documents;

/// <summary>
/// Sample PDFs: a vector drawing and two protected copies made by LibreOffice, plus a scanned page and an
/// empty page written by hand.
/// </summary>
public sealed class PdfSamplesFixture : IDisposable
{
    /// <summary>A4 at 1:1, drawn in Draw: a 4 × 2 cm rectangle, a circle of 3 cm, a 10 cm line and a word.</summary>
    private const string Drawing = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
            xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
            xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
            xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
            office:version="1.3" office:mimetype="application/vnd.oasis.opendocument.graphics">
          <office:body><office:drawing><draw:page draw:name="page1">
            <draw:rect svg:x="2cm" svg:y="2cm" svg:width="4cm" svg:height="2cm"/>
            <draw:circle svg:x="8cm" svg:y="2cm" svg:width="3cm" svg:height="3cm"/>
            <draw:line svg:x1="2cm" svg:y1="8cm" svg:x2="12cm" svg:y2="8cm"/>
            <draw:frame svg:x="2cm" svg:y="10cm" svg:width="8cm" svg:height="1cm"><draw:text-box><text:p>Denah</text:p></draw:text-box></draw:frame>
          </draw:page></office:drawing></office:body>
        </office:document>
        """;

    public PdfSamplesFixture()
    {
        Soffice = LibreOfficeLocator.FindInstalledSoffice() ?? LibreOfficeFixture.FindRepositoryBundle();
        WriteImagePdf(Samples.File("scan.pdf"), withImage: true);
        WriteImagePdf(Samples.File("kosong.pdf"), withImage: false);
        if (Soffice is null)
        {
            return;
        }

        var source = Samples.File("denah.fodg");
        File.WriteAllText(source, Drawing);
        LibreOfficeFixture.Run(Soffice, Profile.Path, source, "pdf", Samples.Path);

        var locked = Directory.CreateDirectory(Samples.File("terkunci")).FullName;
        File.Copy(source, Path.Combine(locked, "sandi.fodg"));
        File.Copy(source, Path.Combine(locked, "izin.fodg"));
        LibreOfficeFixture.Run(Soffice, Profile.Path, Path.Combine(locked, "sandi.fodg"),
            """pdf:draw_pdf_Export:{"EncryptFile":{"type":"boolean","value":"true"},"DocumentOpenPassword":{"type":"string","value":"rahasia"}}""", locked);
        LibreOfficeFixture.Run(Soffice, Profile.Path, Path.Combine(locked, "izin.fodg"),
            """pdf:draw_pdf_Export:{"RestrictPermissions":{"type":"boolean","value":"true"},"PermissionPassword":{"type":"string","value":"rahasia"}}""", locked);
    }

    public string? Soffice { get; }

    internal TempDirectory Samples { get; } = new();

    internal TempDirectory Profile { get; } = new();

    public string Sample(string name) => Samples.File(name);

    public void Dispose()
    {
        Samples.Dispose();
        Profile.Dispose();
    }

    /// <summary>
    /// An A4 page whose only content is a 200 × 283 grayscale image stretched over the whole page: white paper
    /// with a black square and a black ring. Without the image the page is empty.
    /// </summary>
    private static void WriteImagePdf(string path, bool withImage)
    {
        const int w = 200, h = 283;
        var pixels = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var square = x is >= 20 and < 80 && y is >= 20 and < 80;
                var r = Math.Sqrt(Math.Pow(x - 140, 2) + Math.Pow(y - 180, 2));
                pixels[(y * w) + x] = (byte)(square || r is >= 25 and <= 35 ? 0 : 255);
            }
        }

        var content = withImage ? "q 595 0 0 842 0 0 cm /Im1 Do Q\n" : "\n";
        var objects = new List<byte[]>
        {
            Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
            Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Encoding.ASCII.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im1 4 0 R >> >> /Contents 5 0 R >>"),
            (byte[])[.. Encoding.ASCII.GetBytes($"<< /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceGray /BitsPerComponent 8 /Length {pixels.Length} >>\nstream\n"), .. pixels, .. Encoding.ASCII.GetBytes("\nendstream")],
            Encoding.ASCII.GetBytes($"<< /Length {content.Length} >>\nstream\n{content}endstream"),
        };

        using var file = new MemoryStream();
        void Write(string text) => file.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(file.Position);
            Write($"{i + 1} 0 obj\n");
            file.Write(objects[i]);
            Write("\nendobj\n");
        }

        var xref = file.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, file.ToArray());
    }
}

public sealed class PdfConversionTests(PdfSamplesFixture fixture) : IClassFixture<PdfSamplesFixture>, IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VectorPage_KeepsRectangleCircleLineAndText_InMillimeters()
    {
        SkipWithoutLibreOffice();
        Assert.Equal(PdfPageKind.Vector, PdfInspector.GetPageKind(fixture.Sample("denah.pdf"), 1));

        var drawing = await ConvertToCadAsync(".dxf", new CadOptions(1));

        Assert.Equal(ACadSharp.Types.Units.UnitsType.Millimeters, drawing.Header.InsUnits);
        // Filled and stroked by LibreOffice, so painted twice in the PDF, but written once.
        Assert.Single(drawing.Entities.OfType<LwPolyline>(), p => p.IsClosed && Size(p) is var s && Near(s.W, 40) && Near(s.H, 20));

        // The circle's Bézier arcs come back as one CIRCLE (Arc derives from Circle, hence the exact type).
        var circle = Assert.Single(drawing.Entities, e => e.GetType() == typeof(Circle));
        Assert.InRange(((Circle)circle).Radius, 14.8, 15.1);
        Assert.Empty(drawing.Entities.OfType<Arc>());

        // No white page background.
        Assert.DoesNotContain(drawing.Entities.OfType<LwPolyline>(), p => Size(p).W > 200);
        Assert.Contains(drawing.Entities.OfType<LwPolyline>(), p => !p.IsClosed && Size(p) is var s && Near(s.W, 100) && s.H < 0.1);
        Assert.Contains(drawing.Entities.OfType<TextEntity>(), t => t.Value == "Denah");
    }

    [Fact]
    public async Task Scale_MultipliesTheDrawing_AndUnitsFollowTheChoice()
    {
        SkipWithoutLibreOffice();

        var drawing = await ConvertToCadAsync(".dxf", new CadOptions(1, CadUnit.Meters, ScaleDenominator: 100));

        Assert.Equal(ACadSharp.Types.Units.UnitsType.Meters, drawing.Header.InsUnits);
        Assert.Contains(drawing.Entities.OfType<LwPolyline>(), p => p.IsClosed && Size(p) is var s && Near(s.W, 4, 0.01) && Near(s.H, 2, 0.01));
    }

    [Fact]
    public async Task Options_SeparateLines_AndNoText()
    {
        SkipWithoutLibreOffice();

        var drawing = await ConvertToCadAsync(".dxf", new CadOptions(1, KeepText: false, JoinLines: false));

        Assert.DoesNotContain(drawing.Entities, e => e is LwPolyline or TextEntity);
        Assert.Contains(drawing.Entities.OfType<Line>(), l => Near(Math.Abs(l.EndPoint.X - l.StartPoint.X), 100));
    }

    [Fact]
    public async Task Dwg_IsWritten_AndReadsBack()
    {
        SkipWithoutLibreOffice();

        var drawing = await ConvertToCadAsync(".dwg", new CadOptions(1));

        Assert.Single(drawing.Entities, e => e.GetType() == typeof(Circle));
    }

    [Fact]
    public async Task ScannedPage_IsTracedIntoClosedOutlines()
    {
        Assert.Equal(PdfPageKind.Scan, PdfInspector.GetPageKind(fixture.Sample("scan.pdf"), 1));
        var destination = _dir.File("scan.dxf");

        await CreatePipeline().RunAsync(new ConversionJob(fixture.Sample("scan.pdf"), ".dxf", destination, new CadOptions(1)), null, Ct);

        // The square and both edges of the ring, each a closed outline. The square's side is 60 of 200
        // pixels across 210 mm, so about 63 mm.
        var outlines = ReadCad(destination).Entities.OfType<LwPolyline>().ToList();
        Assert.All(outlines, o => Assert.True(o.IsClosed));
        Assert.Equal(3, outlines.Count);
        Assert.Contains(outlines, o => Size(o) is var s && Near(s.W, 63, 1.5) && Near(s.H, 63, 1.5));
    }

    [Fact]
    public async Task EmptyPage_FailsWithNothingToConvert()
    {
        Assert.Equal(PdfPageKind.Empty, PdfInspector.GetPageKind(fixture.Sample("kosong.pdf"), 1));

        await Assert.ThrowsAsync<NothingToConvertException>(() =>
            CreatePipeline().RunAsync(new ConversionJob(fixture.Sample("kosong.pdf"), ".dxf", _dir.File("kosong.dxf"), new CadOptions(1)), null, Ct));
        Assert.False(File.Exists(_dir.File("kosong.dxf")));
    }

    [Theory]
    [InlineData("sandi.pdf", ".dxf")]
    [InlineData("izin.pdf", ".dxf")]
    [InlineData("izin.pdf", ".png")]
    [InlineData("izin.pdf", ".docx")]
    public async Task LockedPdf_IsRefused(string sample, string target)
    {
        SkipWithoutLibreOffice();
        var source = fixture.Sample(Path.Combine("terkunci", sample));
        Assert.Throws<LockedPdfException>(() => PdfInspector.CountPages(source));

        var destination = _dir.File("hasil" + target);
        await Assert.ThrowsAsync<LockedPdfException>(() =>
            CreatePipeline().RunAsync(new ConversionJob(source, target, destination), null, Ct));
        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".jpg")]
    public async Task PdfPage_RendersAt200Dpi(string target)
    {
        var destination = _dir.File("halaman" + target);

        await CreatePipeline().RunAsync(new ConversionJob(fixture.Sample("scan.pdf"), target, destination, new PdfPageOptions(1)), null, Ct);

        // 595 × 842 pt at 200 / 72 pixels per point.
        using var file = File.OpenRead(destination);
        var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());
        Assert.Equal((1653u, 2339u), (decoder.PixelWidth, decoder.PixelHeight));
    }

    [Fact]
    public async Task PdfPage_ToHeic_WhenThisMachineCanWriteHeic()
    {
        Assert.SkipUnless(new PdfToImageConverter().GetTargets(".pdf").Contains(".heic"), "HEIC can't be written on this machine.");

        await CreatePipeline().RunAsync(new ConversionJob(fixture.Sample("scan.pdf"), ".heic", _dir.File("halaman.heic"), new PdfPageOptions(1)), null, Ct);
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".doc")]
    [InlineData(".pptx")]
    [InlineData(".ppt")]
    public async Task PdfToOfficeFormats_ThroughLibreOffice(string target)
    {
        SkipWithoutLibreOffice();

        await CreatePipeline().RunAsync(new ConversionJob(fixture.Sample("denah.pdf"), target, _dir.File("hasil" + target)), null, Ct);
    }

    [Fact]
    public async Task Dwg_ToDxf_ToDwg_AndToPdf()
    {
        SkipWithoutLibreOffice();
        var dwg = _dir.File("denah.dwg");
        await CreatePipeline().RunAsync(new ConversionJob(fixture.Sample("denah.pdf"), ".dwg", dwg, new CadOptions(1)), null, Ct);

        await CreatePipeline().RunAsync(new ConversionJob(dwg, ".dxf", _dir.File("ulang.dxf")), null, Ct);
        await CreatePipeline().RunAsync(new ConversionJob(_dir.File("ulang.dxf"), ".dwg", _dir.File("ulang.dwg")), null, Ct);
        await CreatePipeline().RunAsync(new ConversionJob(dwg, ".pdf", _dir.File("denah-dari-dwg.pdf")), null, Ct);
        await CreatePipeline().RunAsync(new ConversionJob(_dir.File("ulang.dxf"), ".pdf", _dir.File("denah-dari-dxf.pdf")), null, Ct);

        Assert.Single(ReadCad(_dir.File("ulang.dwg")).Entities, e => e.GetType() == typeof(Circle));
    }

    private async Task<CadDocument> ConvertToCadAsync(string target, CadOptions options)
    {
        var destination = _dir.File("denah" + target);
        await CreatePipeline().RunAsync(new ConversionJob(fixture.Sample("denah.pdf"), target, destination, options), null, Ct);
        return ReadCad(destination);
    }

    private static CadDocument ReadCad(string path) => CadFiles.Read(path, Path.GetExtension(path));

    private static (double W, double H) Size(LwPolyline polyline)
    {
        var xs = polyline.Vertices.Select(v => v.Location.X).ToList();
        var ys = polyline.Vertices.Select(v => v.Location.Y).ToList();
        return (xs.Max() - xs.Min(), ys.Max() - ys.Min());
    }

    private static bool Near(double actual, double expected, double tolerance = 0.2) => Math.Abs(actual - expected) <= tolerance;

    private void SkipWithoutLibreOffice() => Assert.SkipWhen(fixture.Soffice is null, "LibreOffice is not available.");

    private ConversionPipeline CreatePipeline()
    {
        Directory.CreateDirectory(_dir.File("staging"));
        var libreOffice = new LibreOfficeConverter(() => fixture.Soffice, fixture.Profile.Path, _dir.File("staging"));
        return new ConversionPipeline(
            new ConverterRegistry(
                [new PdfToImageConverter(), new PdfToCadConverter(new PdfPageRenderer()), new CadFileConverter(), libreOffice],
                [new ImageOutputValidator(), new PdfOutputValidator(), new OfficeDocumentValidator(), new CadOutputValidator()]),
            new TempFileJournal(_dir.File("journal")));
    }
}

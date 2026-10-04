// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using System.Text;

namespace Condec.Tests;

/// <summary>
/// Writes small one-page PDFs by hand, so the PDF → CAD tests can state their input as a content stream
/// ("10 10 m 60 10 l S") and don't depend on any PDF producer.
/// </summary>
internal static class TestPdf
{
    /// <summary>The usual Bézier approximation of a quarter circle: control points at 0.5523 of the radius.</summary>
    public const double Kappa = 0.5522847498;

    /// <param name="content">The page content stream. Helvetica is available as /F1.</param>
    /// <param name="form">Optional form XObject content, available as /Fx with the given matrix.</param>
    /// <param name="innerForm">Optional form XObject content with the identity matrix, available inside /Fx (not on the page) as /Fy.</param>
    /// <param name="image">Adds a 1 × 1 pixel grey image XObject, available on the page as /Im.</param>
    /// <param name="picture">Instead of the grey pixel: the /Im dictionary entries after /Subtype, and its stream bytes.</param>
    /// <param name="softMask">A DeviceGray soft mask for /Im: its size and 8-bit samples.</param>
    public static string Write(
        string path,
        string content,
        double width = 200,
        double height = 100,
        int rotate = 0,
        string? mediaBox = null,
        string? cropBox = null,
        string? form = null,
        string formMatrix = "1 0 0 1 0 0",
        string? innerForm = null,
        bool image = false,
        (string Dictionary, byte[] Data)? picture = null,
        (int Width, int Height, byte[] Data)? softMask = null)
    {
        var media = mediaBox ?? Invariant($"0 0 {width} {height}");
        var crop = cropBox is null ? "" : $" /CropBox [{cropBox}]";

        // 1 catalog, 2 page tree, 3 page (written last, once its XObjects are numbered), 4 content stream, 5 font.
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "",
            Stream("", content),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var xobjects = new List<string>();
        if (form is not null)
        {
            var formResources = "";
            if (innerForm is not null)
            {
                objects.Add(Stream("/Type /XObject /Subtype /Form /BBox [-10000 -10000 10000 10000]", innerForm));
                formResources = Invariant($" /Resources << /XObject << /Fy {objects.Count} 0 R >> >>");
            }

            objects.Add(Stream($"/Type /XObject /Subtype /Form /BBox [-10000 -10000 10000 10000] /Matrix [{formMatrix}]{formResources}", form));
            xobjects.Add(Invariant($"/Fx {objects.Count} 0 R"));
        }

        if (image || picture is not null)
        {
            var mask = "";
            if (softMask is { } m)
            {
                objects.Add(Stream(Invariant($"/Type /XObject /Subtype /Image /Width {m.Width} /Height {m.Height} /ColorSpace /DeviceGray /BitsPerComponent 8"), Encoding.Latin1.GetString(m.Data)));
                mask = Invariant($" /SMask {objects.Count} 0 R");
            }

            objects.Add(picture is { } own
                ? Stream($"/Type /XObject /Subtype /Image {own.Dictionary}{mask}", Encoding.Latin1.GetString(own.Data))
                : Stream($"/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8{mask}", "\u0080"));
            xobjects.Add(Invariant($"/Im {objects.Count} 0 R"));
        }

        var xobjectEntry = xobjects.Count == 0 ? "" : $" /XObject << {string.Join(" ", xobjects)} >>";
        objects[2] = $"<< /Type /Page /Parent 2 0 R /MediaBox [{media}]{crop} /Rotate {rotate} /Contents 4 0 R /Resources << /Font << /F1 5 0 R >>{xobjectEntry} >> >>";

        var output = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Length);
            output.Append(Invariant($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"));
        }

        var xref = output.Length;
        output.Append(Invariant($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            output.Append(Invariant($"{offset:0000000000} 00000 n \n"));
        }

        output.Append(Invariant($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(output.ToString()));
        return path;
    }

    /// <summary>A PDF with one page per content stream, all <paramref name="width"/> × <paramref name="height"/> points.</summary>
    public static string WritePages(string path, IReadOnlyList<string> contents, double width = 200, double height = 100)
    {
        // 1 catalog, 2 page tree, 3 font, then a page and its content stream for each page.
        var kids = string.Join(" ", contents.Select((_, i) => Invariant($"{4 + (i * 2)} 0 R")));
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            Invariant($"<< /Type /Pages /Kids [{kids}] /Count {contents.Count} >>"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        for (var i = 0; i < contents.Count; i++)
        {
            objects.Add(Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Contents {5 + (i * 2)} 0 R /Resources << /Font << /F1 3 0 R >> >> >>"));
            objects.Add(Stream("", contents[i]));
        }

        var output = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Length);
            output.Append(Invariant($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"));
        }

        var xref = output.Length;
        output.Append(Invariant($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            output.Append(Invariant($"{offset:0000000000} 00000 n \n"));
        }

        output.Append(Invariant($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        File.WriteAllBytes(path, Encoding.Latin1.GetBytes(output.ToString()));
        return path;
    }

    /// <summary>A circle of four Béziers, the way PDF producers draw one; closed with <c>h</c> unless told otherwise.</summary>
    public static string Circle(double cx, double cy, double r, bool close = true)
    {
        var k = Kappa * r;
        var text = Invariant($"{cx + r} {cy} m\n")
            + Invariant($"{cx + r} {cy + k} {cx + k} {cy + r} {cx} {cy + r} c\n")
            + Invariant($"{cx - k} {cy + r} {cx - r} {cy + k} {cx - r} {cy} c\n")
            + Invariant($"{cx - r} {cy - k} {cx - k} {cy - r} {cx} {cy - r} c\n")
            + Invariant($"{cx + k} {cy - r} {cx + r} {cy - k} {cx + r} {cy} c\n");
        return close ? text + "h\n" : text;
    }

    private static string Stream(string dictionary, string content) =>
        Invariant($"<< {dictionary} /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream");

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

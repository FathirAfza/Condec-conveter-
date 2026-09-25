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
    public static string Write(
        string path,
        string content,
        double width = 200,
        double height = 100,
        int rotate = 0,
        string? mediaBox = null,
        string? cropBox = null,
        string? form = null,
        string formMatrix = "1 0 0 1 0 0")
    {
        var media = mediaBox ?? Invariant($"0 0 {width} {height}");
        var crop = cropBox is null ? "" : $" /CropBox [{cropBox}]";
        var xobjects = form is null ? "" : " /XObject << /Fx 6 0 R >>";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [{media}]{crop} /Rotate {rotate} /Contents 4 0 R /Resources << /Font << /F1 5 0 R >>{xobjects} >> >>",
            Stream("", content),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        if (form is not null)
        {
            objects.Add(Stream($"/Type /XObject /Subtype /Form /BBox [-10000 -10000 10000 10000] /Matrix [{formMatrix}]", form));
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

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using System.Text;
using Condec.Core.Cad;
using Condec.Core.Conversion;

namespace Condec.Core.Architecture;

public enum PaperSize
{
    A4,
    A3,
}

/// <summary>What to draw a CAD file as (DESIGN §6.3.2).</summary>
/// <param name="Paper">The sheet the drawing is fitted to.</param>
/// <param name="Dpi">Pixels per inch of a picture result; a PDF is vector and ignores it.</param>
/// <param name="TransparentBackground">Leave the paper transparent (PNG only).</param>
/// <param name="HiddenLayers">Layers that are not drawn.</param>
public sealed record CadRenderOptions(PaperSize Paper, int Dpi, bool TransparentBackground, IReadOnlySet<string> HiddenLayers) : ConversionOptions;

/// <summary>
/// Writes a drawing as a one page PDF: the shapes as vector paths and the text in Helvetica. The drawing is fitted to the
/// sheet and centered, on whichever way round (landscape or portrait) it fills better. Nothing outside this device is involved.
/// </summary>
public static class CadPdfWriter
{
    public const double PointsPerMillimeter = 72 / 25.4;

    /// <summary>White border kept all around the drawing, in millimeters.</summary>
    public const double MarginMm = 10;

    /// <summary>Cap height of Helvetica as a share of its font size; CAD text height is the cap height.</summary>
    private const double CapHeightRatio = 0.718;

    /// <summary>
    /// The letters of WinAnsiEncoding outside Latin 1, each mapped to the character whose Latin 1 byte is its code
    /// (PDF 32000-1, Annex D.2).
    /// </summary>
    private static readonly Dictionary<char, char> WinAnsiExtras = new()
    {
        ['\u20AC'] = '\u0080', // euro sign
        ['\u201A'] = '\u0082', // single low-9 quotation mark
        ['\u0192'] = '\u0083', // f with hook
        ['\u201E'] = '\u0084', // double low-9 quotation mark
        ['\u2026'] = '\u0085', // ellipsis
        ['\u2020'] = '\u0086', // dagger
        ['\u2021'] = '\u0087', // double dagger
        ['\u02C6'] = '\u0088', // circumflex accent
        ['\u2030'] = '\u0089', // per mille sign
        ['\u0160'] = '\u008A', // S with caron
        ['\u2039'] = '\u008B', // single left angle quotation mark
        ['\u0152'] = '\u008C', // OE ligature
        ['\u017D'] = '\u008E', // Z with caron
        ['\u2018'] = '\u0091', // left single quotation mark
        ['\u2019'] = '\u0092', // right single quotation mark
        ['\u201C'] = '\u0093', // left double quotation mark
        ['\u201D'] = '\u0094', // right double quotation mark
        ['\u2022'] = '\u0095', // bullet
        ['\u2013'] = '\u0096', // en dash
        ['\u2014'] = '\u0097', // em dash
        ['\u02DC'] = '\u0098', // small tilde
        ['\u2122'] = '\u0099', // trade mark sign
        ['\u0161'] = '\u009A', // s with caron
        ['\u203A'] = '\u009B', // single right angle quotation mark
        ['\u0153'] = '\u009C', // oe ligature
        ['\u017E'] = '\u009E', // z with caron
        ['\u0178'] = '\u009F', // Y with diaeresis
    };

    /// <summary>The sheet in millimeters, landscape: A3 is 420 × 297 and A4 297 × 210.</summary>
    public static (double Width, double Height) SheetMillimeters(PaperSize paper) => paper == PaperSize.A3 ? (420, 297) : (297, 210);

    /// <summary>The page in points the way round the drawing fits better.</summary>
    public static (double Width, double Height) PagePoints(PaperSize paper, bool landscape)
    {
        var (w, h) = SheetMillimeters(paper);
        return landscape ? (w * PointsPerMillimeter, h * PointsPerMillimeter) : (h * PointsPerMillimeter, w * PointsPerMillimeter);
    }

    public static byte[] Write(CadScene scene, Func<string, bool> visible, PaperSize paper)
    {
        var bounds = scene.BoundsOf(visible) ?? throw new NothingToConvertException("No layer of the drawing is shown, so there is nothing to draw.");
        var (minX, minY, maxX, maxY) = bounds;
        var width = Math.Max(maxX - minX, 1e-9);
        var height = Math.Max(maxY - minY, 1e-9);

        var landscape = width >= height;
        var (pageWidth, pageHeight) = PagePoints(paper, landscape);

        // Points of paper per drawing unit, so the whole drawing fits inside the margins.
        var margin = MarginMm * PointsPerMillimeter;
        var scale = Math.Min((pageWidth - (2 * margin)) / width, (pageHeight - (2 * margin)) / height);
        var originX = ((pageWidth - (width * scale)) / 2) - (minX * scale);
        var originY = ((pageHeight - (height * scale)) / 2) - (minY * scale);

        var content = new StringBuilder();
        content.Append("1 J 1 j\n");
        Rgb? stroke = null;
        double? lineWidth = null;
        Rgb? fill = null;

        foreach (var path in scene.Paths.Where(p => visible(p.Layer)))
        {
            if (path.IsFilled)
            {
                if (fill != path.Color)
                {
                    content.Append(Color(path.Color)).Append(" rg\n");
                    fill = path.Color;
                }

                AppendPath(content, path, originX, originY, scale);
                content.Append("f\n");
                continue;
            }

            if (stroke != path.Color)
            {
                content.Append(Color(path.Color)).Append(" RG\n");
                stroke = path.Color;
            }

            var widthPoints = path.WidthMm * PointsPerMillimeter;
            if (lineWidth != widthPoints)
            {
                content.Append(Number(widthPoints)).Append(" w\n");
                lineWidth = widthPoints;
            }

            AppendPath(content, path, originX, originY, scale);
            content.Append(path.IsClosed ? "s\n" : "S\n");
        }

        Rgb? text = null;
        foreach (var label in scene.Labels.Where(l => visible(l.Layer)))
        {
            var size = label.Height * scale / CapHeightRatio;
            if (size < 0.1 || !double.IsFinite(size))
            {
                continue;
            }

            if (text != label.Color)
            {
                content.Append(Color(label.Color)).Append(" rg\n");
                text = label.Color;
            }

            var cos = Math.Cos(label.Rotation);
            var sin = Math.Sin(label.Rotation);
            content.Append("BT /F1 ").Append(Number(size)).Append(" Tf ").Append(Number(label.WidthFactor * 100)).Append(" Tz ")
                .Append(Number(cos)).Append(' ').Append(Number(sin)).Append(' ').Append(Number(-sin)).Append(' ').Append(Number(cos)).Append(' ')
                .Append(Number((label.X * scale) + originX)).Append(' ').Append(Number((label.Y * scale) + originY))
                .Append(" Tm (").Append(Escape(label.Text)).Append(") Tj ET\n");
        }

        return Assemble(pageWidth, pageHeight, content.ToString());
    }

    private static void AppendPath(StringBuilder content, CadPath path, double originX, double originY, double scale)
    {
        for (var i = 0; i < path.Points.Count; i++)
        {
            var (x, y) = path.Points[i];
            content.Append(Number((x * scale) + originX)).Append(' ').Append(Number((y * scale) + originY)).Append(i == 0 ? " m\n" : " l\n");
        }
    }

    private static string Color(Rgb color) => string.Create(
        CultureInfo.InvariantCulture,
        $"{color.R / 255.0:0.###} {color.G / 255.0:0.###} {color.B / 255.0:0.###}");

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// The text as a PDF string in WinAnsiEncoding, the font's encoding: Latin 1, plus the curly quotes, dashes, euro
    /// sign and other letters Windows-1252 keeps at 0x80 to 0x9F. Control characters become spaces; letters the
    /// encoding hasn't become "?".
    /// </summary>
    internal static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\' or '(' or ')':
                    escaped.Append('\\').Append(c);
                    break;
                case < ' ' or (>= '\u007F' and <= '\u009F'):
                    escaped.Append(' ');
                    break;
                case <= 'ÿ':
                    escaped.Append(c);
                    break;
                default:
                    escaped.Append(WinAnsiExtras.TryGetValue(c, out var code) ? code : '?');
                    break;
            }
        }

        return escaped.ToString();
    }

    private static byte[] Assemble(double width, double height, string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number(width)} {Number(height)}] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>"),
            string.Create(CultureInfo.InvariantCulture, $"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream"),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };

        var output = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Length);
            output.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = output.Length;
        output.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            output.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        }

        output.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(output.ToString());
    }
}

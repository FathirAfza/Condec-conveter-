// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Condec.Core.Cad;

/// <summary>A run of words on one baseline, close enough to read as one piece of text.</summary>
internal sealed record TextLine(IReadOnlyList<Word> Words, double Angle)
{
    public string Text => string.Join(" ", Words.Select(word => word.Text));

    public Letter First => Words[0].Letters[0];

    public Letter Last => Words[^1].Letters[^1];

    /// <summary>The extent of every letter, in page coordinates.</summary>
    public Rect Bounds() => Rect.Around(Words.SelectMany(word => word.Letters).SelectMany(letter => new[]
    {
        P(letter.BoundingBox.BottomLeft), P(letter.BoundingBox.BottomRight), P(letter.BoundingBox.TopLeft), P(letter.BoundingBox.TopRight),
    }));

    private static CSMath.XY P(PdfPoint point) => new(point.X, point.Y);
}

/// <summary>
/// Groups words into lines the way a CAD drawing holds them: one TEXT per label, not one per word. Words
/// share a line when they run in the same direction, sit on the same baseline, and the gap between them
/// is no wider than a few spaces. Text in the next table cell is further away and stays separate.
/// </summary>
internal static class TextLines
{
    /// <summary>How far, relative to the font size, two words may differ in baseline and still share a line.</summary>
    internal const double BaselineTolerance = 0.3;

    /// <summary>The widest gap between two words on one line, relative to the font size.</summary>
    internal const double MaxGap = 1.0;

    /// <summary>Directions within this many radians of each other count as the same.</summary>
    internal const double AngleTolerance = 0.03;

    public static List<TextLine> Group(IEnumerable<Word> words)
    {
        var placed = words
            .Where(word => word.Letters.Count > 0 && !string.IsNullOrWhiteSpace(word.Text))
            .Select(word => new Placed(word))
            .OrderBy(word => word.Start)
            .ToList();

        var lines = new List<List<Placed>>();
        foreach (var word in placed)
        {
            var line = lines.LastOrDefault(candidate => Follows(candidate[^1], word));
            if (line is null)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        return lines.Select(line => new TextLine([.. line.Select(w => w.Word)], line[0].Angle)).ToList();
    }

    private static bool Follows(Placed previous, Placed next)
    {
        var size = Math.Max(previous.Size, next.Size);
        return Math.Abs(AngleBetween(previous.Angle, next.Angle)) <= AngleTolerance
            && Math.Abs(previous.Baseline - next.Baseline) <= BaselineTolerance * size
            && next.Start - previous.End >= -0.2 * size
            && next.Start - previous.End <= MaxGap * size;
    }

    private static double AngleBetween(double a, double b)
    {
        var d = (b - a) % (2 * Math.PI);
        return d > Math.PI ? d - (2 * Math.PI) : d <= -Math.PI ? d + (2 * Math.PI) : d;
    }

    /// <summary>A word measured along its own reading direction: where it starts and ends, and which baseline it sits on.</summary>
    private sealed class Placed
    {
        public Placed(Word word)
        {
            Word = word;
            var first = word.Letters[0];
            var last = word.Letters[^1];
            Angle = Math.Atan2(last.EndBaseLine.Y - first.StartBaseLine.Y, last.EndBaseLine.X - first.StartBaseLine.X);
            var (dx, dy) = (Math.Cos(Angle), Math.Sin(Angle));
            Start = (first.StartBaseLine.X * dx) + (first.StartBaseLine.Y * dy);
            End = (last.EndBaseLine.X * dx) + (last.EndBaseLine.Y * dy);
            Baseline = (-first.StartBaseLine.X * dy) + (first.StartBaseLine.Y * dx);
            Size = first.PointSize;
        }

        public Word Word { get; }

        public double Angle { get; }

        public double Start { get; }

        public double End { get; }

        public double Baseline { get; }

        public double Size { get; }
    }
}

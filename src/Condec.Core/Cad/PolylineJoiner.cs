// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CSMath;

namespace Condec.Core.Cad;

/// <summary>
/// Joins open runs of straight segments that meet end to end into longer polylines. CAD exports paint
/// each segment of an outline as its own path, so the runs from one subpath alone stay short. A point
/// where exactly two run ends meet is a join; a T or a star (three or more ends) is not, and the runs
/// there stay separate.
/// </summary>
internal sealed class PolylineJoiner(double tolerance)
{
    private sealed class Run(List<XY> points, int group)
    {
        public List<XY> Points { get; } = points;

        public int Group { get; } = group;

        public bool Consumed { get; set; }

        public XY Start => Points[0];

        public XY End => Points[^1];
    }

    private readonly List<Run> _runs = [];

    /// <summary>Adds an open run. Runs join only within the same group (for example, the same colour).</summary>
    public void Add(IReadOnlyList<XY> points, int group)
    {
        if (points.Count >= 2)
        {
            _runs.Add(new Run([.. points], group));
        }
    }

    /// <summary>The joined runs. A run that comes back to its own start with three or more corners is closed.</summary>
    public IEnumerable<(IReadOnlyList<XY> Points, int Group, bool Closed)> Join()
    {
        var ends = new Dictionary<(long, long, int), List<(Run Run, bool AtStart)>>();
        foreach (var run in _runs)
        {
            Register(ends, run, atStart: true);
            Register(ends, run, atStart: false);
        }

        foreach (var run in _runs)
        {
            if (run.Consumed)
            {
                continue;
            }

            run.Consumed = true;
            var points = new List<XY>(run.Points);
            var closed = Extend(ends, points, run, forward: true) || Extend(ends, points, run, forward: false);
            if (closed)
            {
                points.RemoveAt(points.Count - 1);
            }

            yield return (points, run.Group, closed);
        }
    }

    /// <summary>Grows the chain from one end while the end meets exactly one other run end. True when the chain closes on itself.</summary>
    private bool Extend(Dictionary<(long, long, int), List<(Run Run, bool AtStart)>> ends, List<XY> points, Run first, bool forward)
    {
        while (true)
        {
            var tip = forward ? points[^1] : points[0];
            if (!ends.TryGetValue(Key(tip, first.Group), out var meeting) || meeting.Count != 2)
            {
                return false;
            }

            var next = meeting.FirstOrDefault(m => !m.Run.Consumed);
            if (next.Run is null)
            {
                // Both ends here belong to this chain: it has closed.
                return points.Count >= 4 && CadGeometry.Distance(points[0], points[^1]) <= tolerance;
            }

            next.Run.Consumed = true;
            if (forward)
            {
                // Entering at its start means walking it forwards; entering at its end means backwards.
                var ordered = next.AtStart ? next.Run.Points : Enumerable.Reverse(next.Run.Points).ToList();
                points.AddRange(ordered.Skip(1));
            }
            else
            {
                var ordered = next.AtStart ? Enumerable.Reverse(next.Run.Points).ToList() : next.Run.Points;
                points.InsertRange(0, ordered.Take(ordered.Count - 1));
            }
        }
    }

    private void Register(Dictionary<(long, long, int), List<(Run Run, bool AtStart)>> ends, Run run, bool atStart)
    {
        var key = Key(atStart ? run.Start : run.End, run.Group);
        if (!ends.TryGetValue(key, out var list))
        {
            ends[key] = list = [];
        }

        list.Add((run, atStart));
    }

    private (long, long, int) Key(XY p, int group) => ((long)Math.Round(p.X / tolerance), (long)Math.Round(p.Y / tolerance), group);
}

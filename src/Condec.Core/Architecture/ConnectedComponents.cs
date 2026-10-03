// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Architecture;

/// <summary>A group of touching ink pixels (8-connected).</summary>
/// <param name="Id">The number in the label picture (1 and up; 0 is background).</param>
public sealed record Blob(int Id, int MinX, int MinY, int MaxX, int MaxY, int Area)
{
    public int Width => MaxX - MinX + 1;

    public int Height => MaxY - MinY + 1;

    public PixelRect Bounds => new(MinX, MinY, MaxX + 1, MaxY + 1);
}

public static class ConnectedComponents
{
    /// <summary>
    /// Labels every group of touching set pixels. <paramref name="labels"/> holds, for each pixel, the id of its blob (0 for
    /// none). Iterative, so a picture that is one huge blob can't overflow the stack.
    /// </summary>
    public static List<Blob> Label(BitMask mask, out int[] labels)
    {
        var width = mask.Width;
        var height = mask.Height;
        labels = new int[width * height];
        var blobs = new List<Blob>();
        var stack = new Stack<int>();

        for (var start = 0; start < labels.Length; start++)
        {
            if (!mask.Bits[start] || labels[start] != 0)
            {
                continue;
            }

            var id = blobs.Count + 1;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, area = 0;
            labels[start] = id;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                var x = at % width;
                var y = at / width;
                area++;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if ((uint)ny >= (uint)height)
                    {
                        continue;
                    }

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if ((uint)nx >= (uint)width)
                        {
                            continue;
                        }

                        var neighbor = (ny * width) + nx;
                        if (mask.Bits[neighbor] && labels[neighbor] == 0)
                        {
                            labels[neighbor] = id;
                            stack.Push(neighbor);
                        }
                    }
                }
            }

            blobs.Add(new Blob(id, minX, minY, maxX, maxY, area));
        }

        return blobs;
    }
}

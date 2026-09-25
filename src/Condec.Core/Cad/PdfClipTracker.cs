// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using UglyToad.PdfPig.Content;

namespace Condec.Core.Cad;

/// <summary>
/// Works out which clip region each path of a page was painted in. PDF producers clip viewports and
/// frames with <c>re W n</c>, and CAD exports routinely leave geometry outside those windows that a viewer
/// never shows. PdfPig lists the clipping paths among <see cref="Page.Paths"/> but not where <c>q</c>/<c>Q</c>
/// end their reach, so this replays the page's operators and lines them up with the paths.
/// </summary>
internal static class PdfClipTracker
{
    /// <summary>
    /// One clip rectangle per entry of <see cref="Page.Paths"/>, in page coordinates and never larger than
    /// <paramref name="pageBox"/>. Clipping paths that aren't rectangles count as their bounding box, which
    /// keeps everything a viewer shows and possibly a little more. Returns null when the operators don't
    /// line up with the paths (form XObjects add paths without operators here); callers then clip to the
    /// page only.
    /// </summary>
    public static Rect[]? Resolve(Page page, Rect pageBox)
    {
        var paths = page.Paths;
        var clips = new Rect[paths.Count];
        var saved = new Stack<Rect>();
        var clip = pageBox;
        var index = 0;
        var hasPath = false;
        var pendingClip = false;

        foreach (var operation in page.Operations)
        {
            switch (operation.Operator)
            {
                case "q":
                    saved.Push(clip);
                    break;
                case "Q":
                    if (saved.Count > 0)
                    {
                        clip = saved.Pop();
                    }

                    break;
                case "m" or "l" or "c" or "v" or "y" or "h" or "re":
                    hasPath = true;
                    break;
                case "W" or "W*":
                    pendingClip = hasPath;
                    break;
                case "n" or "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                    if (!hasPath)
                    {
                        break;
                    }

                    if (index >= paths.Count || paths[index].IsClipping != pendingClip)
                    {
                        return null;
                    }

                    clips[index] = clip;
                    if (pendingClip && paths[index].GetBoundingRectangle() is { } bounds)
                    {
                        clip = clip.Intersect(PdfToCadConverter.ToRect(bounds));
                    }

                    index++;
                    hasPath = false;
                    pendingClip = false;
                    break;
            }
        }

        return index == paths.Count ? clips : null;
    }
}

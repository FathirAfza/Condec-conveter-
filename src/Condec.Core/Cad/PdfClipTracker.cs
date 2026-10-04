// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Logging;
using UglyToad.PdfPig.Parser;
using UglyToad.PdfPig.Tokens;

namespace Condec.Core.Cad;

/// <summary>The clip region one path of a page was painted in.</summary>
/// <param name="Bounds">Every clipping path in effect, each counted as its bounding box, intersected; never larger than the page box.</param>
/// <param name="Shape">The clipping path set last (the innermost one), or null when only the page clips.</param>
internal readonly record struct PathClip(Rect Bounds, PdfPath? Shape);

/// <summary>
/// Works out which clip region each path of a page was painted in. PDF producers clip viewports and
/// frames with <c>re W n</c>, CAD exports routinely leave geometry outside those windows that a viewer
/// never shows, and some producers draw a shape by filling far more than shows and clipping it to the
/// shape. PdfPig lists the clipping paths among <see cref="Page.Paths"/> but not where <c>q</c>/<c>Q</c>
/// end their reach, so this replays the page's operators, and those of the form XObjects it paints, and
/// lines them up with the paths.
/// </summary>
internal static class PdfClipTracker
{
    /// <summary>Form XObjects nested deeper than this are taken for a loop; the page is then clipped by the page box only.</summary>
    internal const int MaximumFormDepth = 16;

    /// <summary>
    /// One clip per entry of <see cref="Page.Paths"/>, in page coordinates. Returns null when the operators
    /// don't line up with the paths (something PdfPig paints that the replay doesn't know); callers then clip
    /// to the page only. A form's bounding box is not counted: it rarely cuts anything its clips don't.
    /// </summary>
    public static PathClip[]? Resolve(PdfDocument document, Page page, Rect pageBox)
    {
        var replay = new Replay(document, page, pageBox);
        try
        {
            return replay.Run() ? replay.Clips : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // PdfPig read these streams already, so this is a stream it was lenient with. Whatever the
            // failure, clipping by the page alone is what the page got before forms were followed.
            return null;
        }
    }

    private sealed class Replay(PdfDocument document, Page page, Rect pageBox)
    {
        private readonly IReadOnlyList<PdfPath> _paths = page.Paths;
        private readonly Stack<PathClip> _saved = new();
        private readonly Dictionary<StreamToken, IReadOnlyList<IGraphicsStateOperation>> _forms = new(ReferenceEqualityComparer.Instance);
        private PathClip _clip = new(pageBox, null);
        private int _index;
        private bool _hasPath;
        private bool _pendingClip;
        private bool _failed;

        public PathClip[] Clips { get; } = new PathClip[page.Paths.Count];

        public bool Run()
        {
            var resources = Resolve(PageResources(page.Dictionary)) as DictionaryToken;
            Walk(page.Operations, resources is null ? [] : [resources], 0, 0);
            return !_failed && _index == _paths.Count;
        }

        /// <param name="floor">How many saved states belong to the callers; a <c>Q</c> the content didn't match with a <c>q</c> leaves them alone.</param>
        private void Walk(IReadOnlyList<IGraphicsStateOperation> operations, IReadOnlyList<DictionaryToken> resources, int depth, int floor)
        {
            foreach (var operation in operations)
            {
                if (_failed)
                {
                    return;
                }

                switch (operation.Operator)
                {
                    case "q":
                        _saved.Push(_clip);
                        break;
                    case "Q":
                        if (_saved.Count > floor)
                        {
                            _clip = _saved.Pop();
                        }

                        break;
                    case "m" or "l" or "c" or "v" or "y" or "h" or "re":
                        _hasPath = true;
                        break;
                    case "W" or "W*":
                        _pendingClip = _hasPath;
                        break;
                    case "n" or "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                        Paint();
                        break;
                    case "Do" when operation is InvokeNamedXObject invoke:
                        PaintForm(invoke.Name, resources, depth);
                        break;
                }
            }
        }

        private void Paint()
        {
            if (!_hasPath)
            {
                return;
            }

            if (_index >= _paths.Count || _paths[_index].IsClipping != _pendingClip)
            {
                _failed = true;
                return;
            }

            Clips[_index] = _clip;
            if (_pendingClip && _paths[_index].GetBoundingRectangle() is { } bounds)
            {
                _clip = new PathClip(_clip.Bounds.Intersect(PdfToCadConverter.ToRect(bounds)), _paths[_index]);
            }

            _index++;
            _hasPath = false;
            _pendingClip = false;
        }

        /// <summary>
        /// A form XObject runs its own content inside a saved graphics state (PDF 32000-1, 8.10.1), with its own
        /// resources in front of the ones it was painted with. Images and other XObjects add no paths.
        /// </summary>
        private void PaintForm(NameToken name, IReadOnlyList<DictionaryToken> resources, int depth)
        {
            var form = resources
                .Select(dictionary => Resolve(Resolve(dictionary.Data.GetValueOrDefault("XObject")) is DictionaryToken xobjects
                    ? xobjects.Data.GetValueOrDefault(name.Data)
                    : null))
                .OfType<StreamToken>()
                .FirstOrDefault();
            if (form is null)
            {
                _failed = true;
                return;
            }

            if (form.StreamDictionary.Data.GetValueOrDefault("Subtype") is not NameToken { Data: "Form" })
            {
                return;
            }

            if (depth >= MaximumFormDepth)
            {
                _failed = true;
                return;
            }

            // Symbols painted many times are read once.
            if (!_forms.TryGetValue(form, out var parsed))
            {
                parsed = new PageContentParser(ReflectionGraphicsStateOperationFactory.Instance, StackDepthGuard.Infinite, true)
                    .Parse(page.Number, new MemoryInputBytes(form.Decode(DefaultFilterProvider.Instance)), SilentLog.Instance);
                _forms[form] = parsed;
            }

            // PdfPig leaves out a form's call to itself, so the replay does too.
            var operations = parsed.Where(operation => operation is not InvokeNamedXObject call || call.Name.Data != name.Data).ToList();
            var own = Resolve(form.StreamDictionary.Data.GetValueOrDefault("Resources")) as DictionaryToken;

            // A q the form didn't match with a Q ends with the form.
            var floor = _saved.Count;
            _saved.Push(_clip);
            Walk(operations, own is null ? resources : [own, .. resources], depth + 1, floor + 1);
            while (_saved.Count > floor + 1)
            {
                _saved.Pop();
            }

            _clip = _saved.Pop();
        }

        /// <summary>The page's resources, which a page may inherit from the page tree above it.</summary>
        private IToken? PageResources(DictionaryToken dictionary)
        {
            // The page tree is shallow; the limit only stops a /Parent loop.
            var node = dictionary;
            for (var level = 0; node is not null && level < 64; level++)
            {
                if (node.Data.TryGetValue("Resources", out var resources))
                {
                    return resources;
                }

                node = Resolve(node.Data.GetValueOrDefault("Parent")) as DictionaryToken;
            }

            return null;
        }

        private IToken? Resolve(IToken? token) =>
            token is IndirectReferenceToken reference ? document.Structure.GetObject(reference.Data)?.Data : token;
    }

    /// <summary>Content stream warnings are PdfPig's to report; it already read these streams once.</summary>
    private sealed class SilentLog : ILog
    {
        public static readonly SilentLog Instance = new();

        public void Debug(string message)
        {
        }

        public void Debug(string message, Exception ex)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message)
        {
        }

        public void Error(string message, Exception ex)
        {
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Diagnostics;

namespace Condec.Core.Upscale;

/// <summary>
/// Lowers Condec's priority while a render runs (DESIGN §7.6), so other apps get the processor first when both want it. The whole
/// process is lowered because ONNX Runtime renders on threads of its own; the window is not slowed while the processor is free.
/// Renders may overlap (Upscale Image and Architecture), so the priority returns to what it was when the last one ends.
/// </summary>
public static class RenderPriority
{
    private static readonly Lock Gate = new();
    private static int _renders;
    private static ProcessPriorityClass? _before;

    /// <summary>Lowers the priority until the result is disposed.</summary>
    public static IDisposable Lower()
    {
        lock (Gate)
        {
            if (_renders++ == 0)
            {
                _before = Set(ProcessPriorityClass.BelowNormal);
            }
        }

        return new Scope();
    }

    private static void Restore()
    {
        lock (Gate)
        {
            if (--_renders == 0 && _before is { } before)
            {
                Set(before);
                _before = null;
            }
        }
    }

    /// <summary>Sets the priority and returns the one before; null when Windows refuses (the render then runs as it is).</summary>
    private static ProcessPriorityClass? Set(ProcessPriorityClass priority)
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var before = process.PriorityClass;
            process.PriorityClass = priority;
            return before;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed class Scope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Restore();
            }
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Condec.Views;

/// <summary>The content column of every page (DESIGN §3.3): as wide as the page allows, at most 960, aligned left.</summary>
internal static class PageLayout
{
    public const double ColumnMaxWidth = 960;

    /// <summary>
    /// Keeps the column's width set from the scroller's. With only MaxWidth, WinUI lays a left-aligned column out at the
    /// width its content asks for, which makes the cards change width with their text.
    /// </summary>
    public static void FitColumn(ScrollViewer scroller, FrameworkElement column) =>
        scroller.SizeChanged += (_, _) =>
            column.Width = Math.Min(ColumnMaxWidth, Math.Max(0, scroller.ActualWidth - column.Margin.Left - column.Margin.Right));
}

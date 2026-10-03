// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.Specialized;
using System.Runtime.InteropServices.WindowsRuntime;
using Condec.Core.Architecture;
using Condec.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Condec.Views;

/// <summary>Turns the pixels the view models hand over into something an Image can show.</summary>
internal static class PreviewBitmap
{
    public static WriteableBitmap Create(PreviewImage image)
    {
        var bitmap = new WriteableBitmap(image.Width, image.Height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(image.Bgra, 0, image.Bgra.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }
}

/// <summary>Walks the visual tree.</summary>
internal static class ViewTree
{
    /// <summary>Whether <paramref name="element"/> is <paramref name="container"/> or lies inside it.</summary>
    public static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>What the drop areas of Architecture have in common with the converter card of Convert File.</summary>
internal static class DropFiles
{
    /// <summary>Accepts the drag when it carries files and the page can take one.</summary>
    public static void OnDragOver(DragEventArgs e, bool canTake)
    {
        if (canTake && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.Get("Drag.Caption");
        }
    }

    /// <summary>Reads what was dropped: the paths of the files (empty for one with no place on disk) and the number of folders.</summary>
    public static async Task<(List<string> Files, int Folders)?> ReadAsync(DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return null;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        return ([.. items.OfType<IStorageFile>().Select(file => file.Path ?? string.Empty)], items.OfType<IStorageFolder>().Count());
    }
}

/// <summary>
/// Keeps the selection of a multiple-selection ListView equal to a flag on each of its rows: the checkboxes of "Objek terdeteksi"
/// and of "Layer". The flag starts the selection; ticking or unticking a row sets the flag.
/// </summary>
internal sealed class MultiSelectSync<T>
    where T : class
{
    private readonly ListView _list;
    private readonly Func<T, bool> _get;
    private readonly Action<T, bool> _set;
    private bool _syncing;
    private bool _pending;

    public MultiSelectSync(ListView list, INotifyCollectionChanged rows, Func<T, bool> get, Action<T, bool> set)
    {
        _list = list;
        _get = get;
        _set = set;
        list.SelectionChanged += OnSelectionChanged;
        list.Loaded += (_, _) => Refresh();
        rows.CollectionChanged += OnRowsChanged;
    }

    /// <summary>Selects the rows whose flag is on and deselects the others. Only what differs is touched, so no row is unticked by the sync itself.</summary>
    public void Refresh()
    {
        _pending = false;
        _syncing = true;
        try
        {
            foreach (var row in _list.Items.OfType<T>())
            {
                var selected = _list.SelectedItems.Contains(row);
                if (_get(row) && !selected)
                {
                    _list.SelectedItems.Add(row);
                }
                else if (!_get(row) && selected)
                {
                    _list.SelectedItems.Remove(row);
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>The list learns of new rows after this handler runs, so the selection waits until it has them.</summary>
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_pending || e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        _pending = true;
        _list.DispatcherQueue.TryEnqueue(Refresh);
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        foreach (var row in e.RemovedItems.OfType<T>())
        {
            _set(row, false);
        }

        foreach (var row in e.AddedItems.OfType<T>())
        {
            _set(row, true);
        }
    }
}

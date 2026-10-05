// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.ComponentModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Condec.Core.Localization;
using Condec.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Condec.Views;

public sealed partial class CompressPage : Page
{
    /// <summary>A larger result is decoded at this width for the preview; the card is never wider than that.</summary>
    private const int PreviewDecodeWidth = 1600;

    private int _previewVersion;
    private bool _hadSource;

    public CompressPage()
    {
        ViewModel = App.Current.Services.Compress;
        InitializeComponent();
        TargetBox.NumberFormatter = new TypedNumberFormatter();
        RunView.Run = ViewModel.Run;
        PageLayout.FitColumn(Scroller, Column);

        // The page is cached and its view model outlives it: the handler lives only while the page is shown.
        Loaded += (_, _) =>
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _hadSource = ViewModel.HasSource;
            _ = ShowPreviewAsync();
        };
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public CompressViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CompressViewModel.PreviewData))
        {
            _ = ShowPreviewAsync();
        }
        else if (e.PropertyName is nameof(CompressViewModel.HasSource) && ViewModel.IsInput && ViewModel.HasSource != _hadSource)
        {
            // Keep keyboard focus on the next useful control when the drop card and the choices swap places.
            _hadSource = ViewModel.HasSource;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                (ViewModel.HasSource ? (Control)ChangeFileButton : PickFileButton).Focus(FocusState.Programmatic));
        }
    }

    /// <summary>Decodes the measured result for the preview. A newer result that arrives meanwhile wins.</summary>
    private async Task ShowPreviewAsync()
    {
        var version = ++_previewVersion;
        if (ViewModel.PreviewData is not { } data)
        {
            PreviewImage.Source = null;
            return;
        }

        var bitmap = new BitmapImage();
        if (ViewModel.PreviewWidth > PreviewDecodeWidth)
        {
            bitmap.DecodePixelWidth = PreviewDecodeWidth;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(data.AsBuffer());
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The numbers stay; only the picture is missing.
            bitmap = null;
        }

        if (version == _previewVersion)
        {
            PreviewImage.Source = bitmap;
        }
    }

    private void OnSourceDragOver(object sender, DragEventArgs e)
    {
        if (ViewModel.IsInput && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = Loc.Get("Drag.Caption");
        }
    }

    private async void OnSourceDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            await ViewModel.SelectDroppedAsync(
                [.. items.OfType<IStorageFile>().Select(file => file.Path ?? string.Empty)],
                items.OfType<IStorageFolder>().Count());
        }
        catch (Exception)
        {
            // An async void handler must not let anything escape; the source app may have gone away.
            ViewModel.ReportUnreadableDrop();
        }
        finally
        {
            deferral.Complete();
        }
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.ComponentModel;
using Condec.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Condec.Views;

/// <summary>"DWG, DXF → Gambar, PDF" (DESIGN §6.3.2).</summary>
public sealed partial class FromCadView : UserControl
{
    private readonly MultiSelectSync<LayerRow> _layers;

    public FromCadView()
    {
        ViewModel = App.Current.Services.FromCad;
        InitializeComponent();
        RunView.Run = ViewModel.Run;

        _layers = new MultiSelectSync<LayerRow>(LayerList, ViewModel.Layers, row => row.IsShown, (row, on) => row.IsShown = on);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += (_, _) => ShowPreview();
    }

    public FromCadViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FromCadViewModel.Preview):
                ShowPreview();
                break;
            case nameof(FromCadViewModel.ShowsEmpty) or nameof(FromCadViewModel.ShowsReading) or nameof(FromCadViewModel.ShowsReview):
                MoveFocus();
                break;
        }
    }

    private void ShowPreview() =>
        PreviewImage.Source = ViewModel.Preview is { } preview ? PreviewBitmap.Create(preview) : null;

    private void OnDragOver(object sender, DragEventArgs e) => DropFiles.OnDragOver(e, ViewModel.ShowsEmpty);

    private async void OnDrop(object sender, DragEventArgs e)
    {
        (List<string> Files, int Folders)? dropped;
        var deferral = e.GetDeferral();
        try
        {
            dropped = await DropFiles.ReadAsync(e);
        }
        catch (Exception)
        {
            // An async void handler must not let anything escape; the source app may have gone away.
            ViewModel.ReportUnreadableDrop();
            return;
        }
        finally
        {
            deferral.Complete();
        }

        if (dropped is { } items)
        {
            await ViewModel.SelectDroppedAsync(items.Files, items.Folders);
        }
    }

    // Keep keyboard focus on the next useful control when a view is swapped out under it.
    private void MoveFocus()
    {
        if (!IsLoaded)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            Control? target = null;
            if (ViewModel.ShowsEmpty)
            {
                target = PickFileButton;
            }
            else if (ViewModel.ShowsReading)
            {
                target = CancelReadingButton;
            }
            else if (ViewModel.ShowsReview)
            {
                target = ConvertButton.IsEnabled ? ConvertButton : PickAnotherButton;
            }

            target?.Focus(FocusState.Programmatic);
        });
    }
}

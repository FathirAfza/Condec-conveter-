// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.ComponentModel;
using Condec.Core.Localization;
using Condec.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Condec.Views;

public sealed partial class UpscalePage : Page
{
    // The diagram is as wide as its card and no taller than this, so a tall picture doesn't push the estimate off the screen.
    private const double MinimumDiagramHeight = 200;
    private const double MaximumDiagramHeight = 340;

    public UpscalePage()
    {
        ViewModel = App.Current.Services.Upscale;
        InitializeComponent();
        BatchList.Items = ViewModel.BatchResults;
        PageLayout.FitColumn(Scroller, Column);

        // The page is cached and its view model outlives it: the handler lives only while the page is shown.
        Loaded += (_, _) =>
        {
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateDiagram();
        };
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public UpscaleViewModel ViewModel { get; }

    // Keep keyboard focus on the next useful control when a view is swapped out under it. Moving through the queue keeps it
    // where it is, so Next can be pressed again.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(UpscaleViewModel.Diagram))
        {
            UpdateDiagram();
        }
        else if (e.PropertyName is nameof(UpscaleViewModel.State)
            || (e.PropertyName is nameof(UpscaleViewModel.Source) && ViewModel is { IsInput: true, HasSource: true }))
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (ViewModel.IsInput && FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && ViewTree.IsInside(focused, QueueBar))
                {
                    return;
                }

                FocusTarget()?.Focus(FocusState.Programmatic);
            });
        }
    }

    private Control? FocusTarget() => ViewModel.State switch
    {
        ConverterState.Processing => CancelButton,
        ConverterState.Done when ViewModel.IsBatch => ViewModel.BatchHasSaved ? OpenFolderButton : RetryBatchButton,
        ConverterState.Done => OpenResultButton,
        ConverterState.Failed => RetryButton,
        _ when !ViewModel.HasSource => PickFileButton,
        _ when StartButton.IsEnabled => StartButton,
        _ => ScaleComboBox,
    };

    private void OnDiagramSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDiagram();

    /// <summary>
    /// The result is the dashed box, in the shape of the picture; the original sits in its corner at 100 / scale percent
    /// of its width and height (DESIGN §6.2).
    /// </summary>
    private void UpdateDiagram()
    {
        var geometry = ViewModel.Diagram;
        if (geometry.SourceWidth <= 0 || DiagramArea.ActualWidth <= 0)
        {
            return;
        }

        var available = Math.Clamp(DiagramArea.ActualHeight, MinimumDiagramHeight, MaximumDiagramHeight);
        var width = Math.Min(DiagramArea.ActualWidth, available * geometry.Aspect);
        var height = width / geometry.Aspect;
        DiagramFrame.Width = width;
        DiagramFrame.Height = height;
        OriginalBox.Width = Math.Max(4, width * geometry.OriginalShare);
        OriginalBox.Height = Math.Max(4, height * geometry.OriginalShare);
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

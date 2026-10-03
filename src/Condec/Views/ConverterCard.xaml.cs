// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.ComponentModel;
using Condec.Core.Localization;
using Condec.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Condec.Views;

public sealed partial class ConverterCard : UserControl
{
    private ConverterViewModel? _viewModel;

    public ConverterCard()
    {
        InitializeComponent();
    }

    /// <summary>Set by the page right after it is created, before the card loads.</summary>
    public ConverterViewModel ViewModel
    {
        get => _viewModel!;
        set
        {
            _viewModel = value;
            BatchList.Items = value.BatchResults;
            value.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>Label style for a row in the progress list, used by x:Bind.</summary>
    public static Style StepLabelStyle(StepState state) => (Style)Application.Current.Resources[state switch
    {
        StepState.Done => "StepDoneTextStyle",
        StepState.Active => "StepActiveTextStyle",
        _ => "StepWaitingTextStyle",
    }];

    // Keep keyboard focus on the next useful control when a view is swapped out under it.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsLoaded
            && (e.PropertyName is nameof(ConverterViewModel.State)
                || (e.PropertyName is nameof(ConverterViewModel.Source) && ViewModel is { IsInput: true, HasSource: true })))
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => FocusTarget()?.Focus(FocusState.Programmatic));
        }
    }

    private Control? FocusTarget() => ViewModel.State switch
    {
        ConverterState.Processing => CancelButton,
        ConverterState.Done when ViewModel.IsBatch => ViewModel.BatchHasSaved ? OpenFolderButton : RetryFailedButton,
        ConverterState.Done => OpenResultButton,
        ConverterState.Failed => RetryButton,
        _ when !ViewModel.HasSource => PickFileButton,
        _ when ConvertButton.IsEnabled => ConvertButton,
        _ => FormatComboBox,
    };

    // A format that needs a missing program stays in the list, captioned "Unavailable", but can't be chosen.
    // Disabling its ComboBoxItem instead closes the open list when that item has keyboard focus.
    private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FormatComboBox.SelectedItem is FormatOption { IsEnabled: false })
        {
            FormatComboBox.SelectedItem = e.RemovedItems.OfType<FormatOption>().FirstOrDefault();
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

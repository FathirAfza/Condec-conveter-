// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.ComponentModel;
using Condec.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Condec.Views;

/// <summary>Processing, done and failed (DESIGN §6.1) for one <see cref="ConversionRun"/>.</summary>
public sealed partial class RunStatusView : UserControl
{
    private ConversionRun? _run;

    public RunStatusView()
    {
        InitializeComponent();
    }

    /// <summary>Set by the page right after it is created, before the view loads.</summary>
    public ConversionRun Run
    {
        get => _run!;
        set
        {
            _run = value;
            BatchList.Items = value.BatchResults;
            value.PropertyChanged += OnRunPropertyChanged;
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
    private void OnRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsLoaded && e.PropertyName == nameof(ConversionRun.State))
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => FocusTarget()?.Focus(FocusState.Programmatic));
        }
    }

    private Control? FocusTarget() => Run.State switch
    {
        RunState.Processing => CancelButton,
        RunState.Done when Run.IsBatch => Run.BatchHasSaved ? OpenFolderButton : RetryBatchButton,
        RunState.Done => OpenResultButton,
        RunState.Failed => RetryButton,
        _ => null,
    };
}

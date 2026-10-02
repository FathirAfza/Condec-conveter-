// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace Condec.Views;

public sealed partial class ConvertPage : Page
{
    public ConvertPage()
    {
        ViewModel = App.Current.Services.Convert;
        InitializeComponent();
        Card.ViewModel = ViewModel;
        PageLayout.FitColumn(Scroller, Column);
    }

    public ConverterViewModel ViewModel { get; }

    // Architecture writes into the same history, so the list is read again whenever the page shows.
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshHistory();
    }

    // HyperlinkButton has no Flyout property of its own, so the confirmation is an attached flyout.
    private void OnClearHistoryClicked(object sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);

    private void OnClearHistoryConfirmed(object sender, RoutedEventArgs e) => ClearHistoryFlyout.Hide();
}

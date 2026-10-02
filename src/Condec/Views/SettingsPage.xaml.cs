// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Condec.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Current.Services.SettingsPage;
        InitializeComponent();
        PageLayout.FitColumn(Scroller, Column);
    }

    public SettingsViewModel ViewModel { get; }

    // The cache can have grown since the page was last open.
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshCacheAsync();
    }

    // A scale above the device stays in the list, dimmed and captioned, but can't be chosen. Disabling its ComboBoxItem
    // instead closes the open list when that item has keyboard focus (same as the format list in Convert File).
    private void OnScaleLimitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((ComboBox)sender).SelectedItem is ScaleLimitOption { IsEnabled: false })
        {
            ((ComboBox)sender).SelectedItem = e.RemovedItems.OfType<ScaleLimitOption>().FirstOrDefault();
        }
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var general = sender.SelectedItem == GeneralTab;
        GeneralPanel.Visibility = general ? Visibility.Visible : Visibility.Collapsed;
        RenderPanel.Visibility = general ? Visibility.Collapsed : Visibility.Visible;
    }
}

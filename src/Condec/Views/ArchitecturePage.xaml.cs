// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Condec.Views;

public sealed partial class ArchitecturePage : Page
{
    public ArchitecturePage()
    {
        InitializeComponent();
        PageLayout.FitColumn(Scroller, Column);
    }

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var fromCad = sender.SelectedItem == FromCadTab;
        FromCad.Visibility = fromCad ? Visibility.Visible : Visibility.Collapsed;
        ToCad.Visibility = fromCad ? Visibility.Collapsed : Visibility.Visible;
    }
}

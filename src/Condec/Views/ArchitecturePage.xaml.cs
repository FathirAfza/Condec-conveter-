// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Microsoft.UI.Xaml.Controls;

namespace Condec.Views;

public sealed partial class ArchitecturePage : Page
{
    public ArchitecturePage()
    {
        InitializeComponent();
        Card.ViewModel = App.Current.Services.Architecture;
        PageLayout.FitColumn(Scroller, Column);
    }
}

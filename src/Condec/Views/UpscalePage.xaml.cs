// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Microsoft.UI.Xaml.Controls;

namespace Condec.Views;

public sealed partial class UpscalePage : Page
{
    public UpscalePage()
    {
        InitializeComponent();
        PageLayout.FitColumn(Scroller, Column);
    }
}

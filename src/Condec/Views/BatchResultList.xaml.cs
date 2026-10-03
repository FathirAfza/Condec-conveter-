// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using Condec.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Condec.Views;

/// <summary>One row per result of a batch: its name, the file it comes from, and whether it was saved (DESIGN §6.1.3).</summary>
public sealed partial class BatchResultList : UserControl
{
    public BatchResultList()
    {
        InitializeComponent();
    }

    /// <summary>Set by the owner right after it is created, before the view loads.</summary>
    public ObservableCollection<BatchResultItem> Items { get; set; } = [];
}

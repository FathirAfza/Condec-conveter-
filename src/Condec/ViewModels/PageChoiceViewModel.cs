// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CommunityToolkit.Mvvm.ComponentModel;
using Condec.Core.Batch;
using Condec.Core.Localization;

namespace Condec.ViewModels;

/// <summary>
/// Which pages of a PDF or a multi-page TIFF become results (DESIGN §6.1, owner decision 2026-10-03): every page, the first
/// one, or pages typed as "1-3, 5". The same choice applies to every file of a batch.
/// </summary>
public sealed partial class PageChoiceViewModel : ObservableObject
{
    public const int AllIndex = 0;
    public const int FirstIndex = 1;
    public const int CustomIndex = 2;

    /// <summary>0 every page, 1 the first page, 2 the pages typed in <see cref="RangeText"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustom), nameof(Range), nameof(IsValid), nameof(ShowsError))]
    public partial int ModeIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Range), nameof(IsValid), nameof(ShowsError))]
    public partial string RangeText { get; set; } = string.Empty;

    public bool IsCustom => ModeIndex == CustomIndex;

    /// <summary>The chosen pages, or null while the typed pages can't be read.</summary>
    public PageRange? Range => ModeIndex switch
    {
        FirstIndex => PageRange.First,
        CustomIndex => PageRange.TryParse(RangeText, out var range) ? range : null,
        _ => PageRange.All,
    };

    public bool IsValid => Range is not null;

    /// <summary>The hint under the box once something unreadable was typed; an empty box only blocks the conversion.</summary>
    public bool ShowsError => IsCustom && !IsValid && !string.IsNullOrWhiteSpace(RangeText);

    public string ErrorText => Loc.Get("Pages.Invalid");

    /// <summary>Back to every page, for a newly chosen set of files.</summary>
    public void Reset()
    {
        ModeIndex = AllIndex;
        RangeText = string.Empty;
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CommunityToolkit.Mvvm.ComponentModel;
using Condec.Core.Conversion;
using Condec.Core.Localization;
using Condec.Core.Pdf;

namespace Condec.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Options for a PDF source: the page, and for DXF/DWG (Architecture) the unit, scale and two switches. The page is checked
/// for being a vector drawing or a scan, which decides whether text can be kept.
/// </summary>
public sealed partial class PdfOptionsViewModel : ObservableObject
{
    /// <summary>A custom scale is entered in a NumberBox; this marks the "Custom" entry.</summary>
    private const double CustomScale = 0;

    private string? _path;
    private int _kindRequest;

    public IReadOnlyList<Choice<CadUnit>> Units { get; } =
    [
        new(CadUnit.Millimeters, Loc.Get("Unit.Mm")),
        new(CadUnit.Centimeters, Loc.Get("Unit.Cm")),
        new(CadUnit.Meters, Loc.Get("Unit.M")),
        new(CadUnit.Inches, Loc.Get("Unit.In")),
    ];

    public IReadOnlyList<Choice<double>> Scales { get; } =
    [
        new(1, "1 : 1"),
        new(50, "1 : 50"),
        new(100, "1 : 100"),
        new(200, "1 : 200"),
        new(CustomScale, Loc.Get("Scale.Custom")),
    ];

    public List<Choice<int>> Pages { get; private set; } = [];

    [ObservableProperty]
    public partial Choice<CadUnit>? SelectedUnit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomScale), nameof(HasValidScale))]
    public partial Choice<double>? SelectedScale { get; set; }

    /// <summary>The n of 1 : n when "Kustom" is chosen. NaN when the NumberBox is cleared.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidScale))]
    public partial double CustomDenominator { get; set; } = 100;

    public bool IsCustomScale => SelectedScale?.Value == CustomScale;

    /// <summary>A cleared or out of range custom scale blocks the conversion instead of guessing a value.</summary>
    public bool HasValidScale => !IsCustomScale || (double.IsFinite(CustomDenominator) && CustomDenominator >= 1);

    [ObservableProperty]
    public partial Choice<int>? SelectedPage { get; set; }

    [ObservableProperty]
    public partial bool KeepText { get; set; } = true;

    [ObservableProperty]
    public partial bool JoinLines { get; set; } = true;

    /// <summary>Null while the page is being checked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScan), nameof(IsEmpty), nameof(CanKeepText), nameof(KeepTextNote))]
    public partial PdfPageKind? PageKind { get; set; }

    public bool IsScan => PageKind == PdfPageKind.Scan;

    public bool IsEmpty => PageKind == PdfPageKind.Empty;

    public bool CanKeepText => !IsScan;

    public string KeepTextNote => Loc.Get(IsScan ? "Pdf.KeepTextNoteScan" : "Pdf.KeepTextNote");

    /// <summary>Resets the options for a newly chosen PDF.</summary>
    public void Load(string path, int pageCount)
    {
        _path = path;
        Pages = [.. Enumerable.Range(1, pageCount).Select(n => new Choice<int>(n, Loc.Format("Pdf.PageOf", n, pageCount)))];
        OnPropertyChanged(nameof(Pages));
        SelectedUnit ??= Units[0];
        SelectedScale ??= Scales[0];
        SelectedPage = Pages[0];
    }

    public ConversionOptions? BuildOptions(string targetExtension)
    {
        var page = SelectedPage?.Value ?? 1;
        return targetExtension switch
        {
            ".dxf" or ".dwg" => new CadOptions(
                page,
                SelectedUnit?.Value ?? CadUnit.Millimeters,
                IsCustomScale ? CustomDenominator : SelectedScale?.Value ?? 1,
                KeepText && CanKeepText,
                JoinLines),
            ".png" or ".jpg" or ".heic" => new PdfPageOptions(page),
            _ => null,
        };
    }

    partial void OnSelectedPageChanged(Choice<int>? value)
    {
        if (_path is null || value is null)
        {
            return;
        }

        // Reading the page can take a moment on a large PDF; a newer choice wins over an older answer.
        var request = ++_kindRequest;
        var path = _path;
        PageKind = null;
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _ = Task.Run(() =>
        {
            PdfPageKind kind;
            try
            {
                kind = PdfInspector.GetPageKind(path, value.Value);
            }
            catch (Exception)
            {
                // The conversion itself reports an unreadable page; the notice just stays neutral.
                kind = PdfPageKind.Vector;
            }

            dispatcher.TryEnqueue(() =>
            {
                if (request == _kindRequest)
                {
                    PageKind = kind;
                }
            });
        });
    }
}

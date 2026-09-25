using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Condec.Core.Conversion;
using Condec.Core.Pdf;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Options for a PDF source: the page, and for DXF/DWG the unit, scale and two switches. The page is checked
/// for being a vector drawing or a scan, which decides the notice and whether text can be kept.
/// </summary>
public sealed partial class PdfOptionsViewModel : ObservableObject
{
    /// <summary>A custom scale is entered in a NumberBox; this marks the "Kustom" entry.</summary>
    private const double CustomScale = 0;

    private string? _path;
    private int _kindRequest;

    public IReadOnlyList<Choice<CadUnit>> Units { get; } =
    [
        new(CadUnit.Millimeters, "Milimeter (mm)"),
        new(CadUnit.Centimeters, "Sentimeter (cm)"),
        new(CadUnit.Meters, "Meter (m)"),
        new(CadUnit.Inches, "Inci (in)"),
    ];

    public IReadOnlyList<Choice<double>> Scales { get; } =
    [
        new(1, "1 : 1"),
        new(50, "1 : 50"),
        new(100, "1 : 100"),
        new(200, "1 : 200"),
        new(CustomScale, "Kustom"),
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
    [NotifyPropertyChangedFor(nameof(IsScan), nameof(IsEmpty), nameof(CanKeepText), nameof(KeepTextNote), nameof(KindTitle), nameof(KindMessage), nameof(KindSeverity), nameof(HasKind))]
    public partial PdfPageKind? PageKind { get; set; }

    public bool HasKind => PageKind is not null;

    public bool IsScan => PageKind == PdfPageKind.Scan;

    public bool IsEmpty => PageKind == PdfPageKind.Empty;

    public bool CanKeepText => !IsScan;

    public string KeepTextNote => IsScan ? "Tidak tersedia untuk PDF hasil scan" : "Disimpan sebagai entitas TEXT, bukan garis";

    public string KindTitle => PageKind switch
    {
        PdfPageKind.Scan => "PDF hasil scan terdeteksi",
        PdfPageKind.Empty => "Halaman ini kosong",
        _ => "PDF vektor terdeteksi",
    };

    public string KindMessage => PageKind switch
    {
        PdfPageKind.Scan => "Isinya gambar piksel, bukan garis. Hasil DXF berupa jejak garis perkiraan dan tidak presisi untuk ukuran.",
        PdfPageKind.Empty => "Tidak ada garis, teks, atau gambar yang bisa diubah. Pilih halaman lain.",
        _ => "Garis, busur, dan teks akan diubah menjadi objek CAD.",
    };

    public InfoBarSeverity KindSeverity => PageKind switch
    {
        PdfPageKind.Scan or PdfPageKind.Empty => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Success,
    };

    /// <summary>Resets the options for a newly chosen PDF.</summary>
    public void Load(string path, int pageCount)
    {
        _path = path;
        Pages = [.. Enumerable.Range(1, pageCount).Select(n => new Choice<int>(n, string.Create(CultureInfo.InvariantCulture, $"Halaman {n} dari {pageCount}")))];
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

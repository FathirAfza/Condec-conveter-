// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Architecture;
using Condec.Core.Conversion;
using Condec.Core.Devices;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using Condec.Core.Settings;
using Condec.Core.Upscale;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

public enum ArchitecturePhase
{
    /// <summary>No file yet: the drop area.</summary>
    Empty,

    /// <summary>The file is being read and looked at.</summary>
    Reading,

    /// <summary>Preview, banner and the list of objects.</summary>
    Review,
}

/// <summary>The InfoBar above the preview (DESIGN §6.3.1).</summary>
public enum ToCadBanner
{
    None,
    Unclear,
    NotEligible,
    Upscaling,
    Ready,
    Skipped,
    Vector,
    NothingFound,
}

public enum CadSourceKind
{
    /// <summary>A picture, or a scanned PDF page: it is looked at and traced.</summary>
    Raster,

    /// <summary>A DXF file: converted to DWG as it is.</summary>
    Dxf,

    /// <summary>A PDF page drawn with lines and text: its shapes are converted directly.</summary>
    VectorPdf,
}

public enum OverlayStyle
{
    Included,
    Excluded,
    Unclear,
}

/// <summary>A box on the preview, in the pixels of the picture the preview shows.</summary>
public sealed record OverlayBox(PixelRect Bounds, OverlayStyle Style);

/// <summary>One kind of object in the "Objek terdeteksi" list.</summary>
public sealed partial class ObjectRow : ObservableObject
{
    private readonly string _description;

    public ObjectRow(DrawingObjectKind kind, string name, string count, string description, bool isChecked)
    {
        Kind = kind;
        Name = name;
        Count = count;
        _description = description;
        IsChecked = isChecked;
    }

    public DrawingObjectKind Kind { get; }

    public string Name { get; }

    public string Count { get; }

    /// <summary>Whether this kind becomes CAD. Kept in step with the list's selection by the view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Description), nameof(AutomationName))]
    public partial bool IsChecked { get; set; }

    public string Description => IsChecked ? _description : Loc.Get("Architecture.Desc.Excluded");

    public string AutomationName => $"{Name}, {Count}, {Description}";
}

/// <summary>One entry of the "Upscale n× first" menu; a scale beyond the limit stays in it, locked.</summary>
public sealed record UpscaleChoiceItem(int Scale, bool IsAllowed)
{
    public string Text => IsAllowed ? ScaleText.Format(Scale, Loc.Culture) : $"{ScaleText.Format(Scale, Loc.Culture)} · {Loc.Get("Upscale.ScaleLocked")}";
}

/// <summary>
/// Architecture, "Gambar, PDF, DXF → DWG" (DESIGN §6.3.1): a picture is looked at and its lines, doors, text and tables are
/// listed for the user to choose from; a DXF or a PDF drawn with lines is converted as it is. Everything runs on this device.
/// </summary>
public sealed partial class ToCadViewModel : ObservableObject
{
    private const long GiB = 1024L * 1024 * 1024;

    /// <summary>Share of the progress bar the upscale takes; the rest is the second look at the larger picture.</summary>
    private const double UpscaleShare = 0.85;

    private static readonly string[] SourceExtensions = [".dxf", ".png", ".jpg", ".jpeg", ".heic", ".heif", ".pdf"];

    private readonly AppSettings _settings;
    private readonly ConversionPipeline _pipeline;
    private readonly ConversionPipeline _architecturePipeline;
    private readonly ConversionPipeline _upscalePipeline;
    private readonly IDesktopServices _desktop;
    private readonly ActivityLog _log;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private WindowsTextRecognizer? _recognizer;
    private CancellationTokenSource? _work;
    private SourceFile? _source;
    private CadSourceKind _kind;
    private DrawingAnalysis? _analysis;
    private UpscaleAdvice? _advice;
    private int _originalWidth;
    private int _originalHeight;
    private int _pictureWidth;
    private int _pictureHeight;
    private double _originalDpiX;
    private double _originalDpiY;
    private int _upscaleFactor = 1;
    private int _pendingScale = 2;
    private double? _calibration;
    private ToCadBanner _bannerBefore;

    /// <param name="pipeline">The main pipeline: a DXF and a PDF drawn with lines are converted with the converters of Convert File.</param>
    /// <param name="architecturePipeline">Turns an analyzed picture into DWG or DXF.</param>
    /// <param name="upscalePipeline">The pipeline of Upscale Image, used to enlarge a picture before it is read.</param>
    public ToCadViewModel(
        AppSettings settings,
        ConversionPipeline pipeline,
        ConversionPipeline architecturePipeline,
        ConversionPipeline upscalePipeline,
        ConversionRun run,
        IDesktopServices desktop,
        ActivityLog log)
    {
        _settings = settings;
        _pipeline = pipeline;
        _architecturePipeline = architecturePipeline;
        _upscalePipeline = upscalePipeline;
        Run = run;
        _desktop = desktop;
        _log = log;

        Rows.CollectionChanged += (_, _) => RaiseRows();
        Pdf.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PdfOptionsViewModel.HasValidScale))
            {
                ConvertCommand.NotifyCanExecuteChanged();
            }
        };
        Run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionRun.IsActive))
            {
                OnPropertyChanged(nameof(ShowsEmpty));
                OnPropertyChanged(nameof(ShowsReading));
                OnPropertyChanged(nameof(ShowsReview));
                ConvertCommand.NotifyCanExecuteChanged();
                PickSourceCommand.NotifyCanExecuteChanged();
            }
        };
        Run.Cancelled += (_, _) => ShowMessage(Loc.Get("Input.Cancelled"), InfoBarSeverity.Informational);
        Run.AnotherRequested += (_, _) => Reset();
        _settings.Changed += (_, _) => OnUi(RefreshAdvice);
    }

    /// <summary>The progress list, the finished card and the failed card.</summary>
    public ConversionRun Run { get; }

    /// <summary>Unit, scale and switches for a PDF drawn with lines (its shapes are converted as they are).</summary>
    public PdfOptionsViewModel Pdf { get; } = new();

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }

    // ---- Phase ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty), nameof(ShowsReading), nameof(ShowsReview))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial ArchitecturePhase Phase { get; set; }

    public bool ShowsEmpty => Phase == ArchitecturePhase.Empty && !Run.IsActive;

    public bool ShowsReading => Phase == ArchitecturePhase.Reading && !Run.IsActive;

    public bool ShowsReview => Phase == ArchitecturePhase.Review && !Run.IsActive;

    // ---- Messages (above the drop area and the review) ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; }

    public bool HasMessage => Message is not null;

    [RelayCommand]
    private void DismissMessage() => Message = null;

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        MessageSeverity = severity;
        Message = message;
    }

    public void ReportUnreadableDrop() => ShowMessage(Loc.Get("Input.DropUnreadable"), InfoBarSeverity.Warning);

    // ---- Reading ----

    [ObservableProperty]
    public partial string ReadingTitle { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingProgressText))]
    public partial double ReadingPercent { get; set; }

    public string ReadingProgressText => $"{ReadingPercent:0}%";

    [RelayCommand]
    private void CancelReading() => _work?.Cancel();

    // ---- Choosing a file ----

    private bool CanPick() => !Run.IsActive && Phase != ArchitecturePhase.Reading && Banner != ToCadBanner.Upscaling;

    [RelayCommand(CanExecute = nameof(CanPick))]
    private async Task PickSourceAsync()
    {
        var path = await _desktop.PickSourceFileAsync(SourceExtensions);
        if (path is null)
        {
            return;
        }

        if (Phase == ArchitecturePhase.Review)
        {
            Reset();
        }

        await SelectSourceAsync(path);
    }

    /// <summary>Drag and drop: exactly one file is accepted.</summary>
    /// <param name="filePaths">An empty path is a file with no place on disk, such as one inside a ZIP.</param>
    public Task SelectDroppedAsync(IReadOnlyList<string> filePaths, int folderCount)
    {
        if (folderCount > 0 && filePaths.Count == 0)
        {
            ShowMessage(Loc.Get("Input.DropFolder"), InfoBarSeverity.Warning);
        }
        else if (filePaths.Count + folderCount > 1)
        {
            ShowMessage(Loc.Get("Input.DropMany"), InfoBarSeverity.Warning);
        }
        else if (filePaths.Count == 1 && filePaths[0].Length == 0)
        {
            ReportUnreadableDrop();
        }
        else if (filePaths.Count == 1)
        {
            return SelectSourceAsync(filePaths[0]);
        }

        return Task.CompletedTask;
    }

    public async Task SelectSourceAsync(string path)
    {
        if (Phase != ArchitecturePhase.Empty || Run.IsActive)
        {
            return;
        }

        var extension = FileExtension.FromPath(path);
        if (!SourceExtensions.Contains(extension))
        {
            ShowMessage(
                extension.Length == 0
                    ? Loc.Get("Architecture.ToCad.UnsupportedNoExtension")
                    : Loc.Format("Architecture.ToCad.Unsupported", FileExtension.ToCode(extension)),
                InfoBarSeverity.Warning);
            return;
        }

        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage(Loc.Get("Input.CannotOpen"), InfoBarSeverity.Warning);
            return;
        }

        Message = null;
        ReadingTitle = Loc.Format("Architecture.Reading.Title", Path.GetFileName(path));
        ReadingPercent = 0;
        Phase = ArchitecturePhase.Reading;

        using var work = new CancellationTokenSource();
        _work = work;
        try
        {
            var shown = extension switch
            {
                ".dxf" => await LoadDxfAsync(path, size, work.Token),
                ".pdf" => await LoadPdfAsync(path, size, work.Token),
                _ => await LoadPictureAsync(path, extension, size, null, work.Token),
            };
            if (!shown)
            {
                Reset();
            }
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            Reset();
            ShowMessage(Loc.Get("Architecture.Reading.Cancelled"), InfoBarSeverity.Informational);
            _log.Info("Reading a drawing was cancelled");
        }
        catch (Exception ex)
        {
            Reset();
            ShowMessage(ErrorMessages.Describe(ex, PipelineStage.Decode, FileExtension.ToCode(extension)), InfoBarSeverity.Warning);
            _log.Error($"Reading a drawing failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _work = null;
        }
    }

    /// <summary>A DXF is converted as it is; it is read here for the preview, and so that a file that can't be read says so now.</summary>
    private async Task<bool> LoadDxfAsync(string path, long size, CancellationToken ct)
    {
        var scene = await ArchitectureFiles.ReadDrawingAsync(path, ct);
        ReadingPercent = 60;
        var preview = await TryPreviewAsync(() => ArchitectureFiles.RenderDrawingAsync(scene, name => scene.Layers.FirstOrDefault(l => l.Name == name)?.IsOn ?? true, PaperSize.A3, ct));
        ct.ThrowIfCancellationRequested();

        StartReview(new SourceFile(path, ".dxf", size), CadSourceKind.Dxf, preview);
        return true;
    }

    private async Task<bool> LoadPdfAsync(string path, long size, CancellationToken ct)
    {
        int pages;
        PdfPageKind kind;
        try
        {
            (pages, kind) = await Task.Run(() => (PdfInspector.CountPages(path), PdfInspector.GetPageKind(path, 1)), ct);
        }
        catch (LockedPdfException)
        {
            ShowMessage(ErrorMessages.LockedPdf, InfoBarSeverity.Warning);
            return false;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // PdfPig throws its own exception types for damaged files.
            ShowMessage(Loc.Get("Input.PdfUnreadable"), InfoBarSeverity.Warning);
            return false;
        }

        switch (kind)
        {
            case PdfPageKind.Empty:
                ShowMessage(Loc.Get("Pdf.KindEmptyMessage"), InfoBarSeverity.Warning);
                return false;
            case PdfPageKind.Scan:
                return await LoadPictureAsync(path, ".pdf", size, pages, ct);
            default:
                ReadingPercent = 40;
                var preview = await TryPreviewAsync(() => ArchitectureFiles.RenderPdfAsync(path, ct));
                ct.ThrowIfCancellationRequested();
                Pdf.Load(path, pages);
                StartReview(new SourceFile(path, ".pdf", size, pages), CadSourceKind.VectorPdf, preview);
                return true;
        }
    }

    /// <summary>A preview that can't be drawn doesn't stop the conversion; the page then shows a placeholder.</summary>
    private async Task<PreviewImage?> TryPreviewAsync(Func<Task<PreviewImage>> render)
    {
        try
        {
            return await render();
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            _log.Info($"No preview: {ex.GetType().Name}");
            return null;
        }
    }

    private async Task<bool> LoadPictureAsync(string path, string extension, long size, int? pdfPages, CancellationToken ct)
    {
        var picture = await Task.Run(() => WindowsPictureReader.ReadAsync(path, ct), ct);
        _originalWidth = _pictureWidth = picture.Width;
        _originalHeight = _pictureHeight = picture.Height;
        _originalDpiX = picture.DpiX;
        _originalDpiY = picture.DpiY;
        _upscaleFactor = 1;
        _calibration = null;
        ReadingPercent = 5;

        var analysis = await AnalyzeAsync(picture, percent => ReadingPercent = 5 + (percent * 95), ct);
        StartReview(new SourceFile(path, extension, size, pdfPages), CadSourceKind.Raster, _preview);
        ApplyAnalysis(analysis, afterUpscale: false);
        _log.Info($"Picture read: {analysis.ObjectCount} objects, {analysis.UnclearAreas.Count} unclear areas, text read {analysis.TextWasRead}");
        return true;
    }

    private PreviewImage? _preview;

    /// <param name="report">Fraction 0 to 1 of the analysis.</param>
    private async Task<DrawingAnalysis> AnalyzeAsync(RasterPicture picture, Action<double> report, CancellationToken ct)
    {
        // The preview is the picture the analysis works on, so the boxes of the objects lie exactly on it.
        var reduced = picture.ReduceTo(DrawingAnalyzer.MaximumSide).Picture;
        _preview = new PreviewImage(reduced.Width, reduced.Height, reduced.Bgra);

        var progress = new Progress<double>(report);
        return await Task.Run(
            () =>
            {
                _recognizer ??= new WindowsTextRecognizer();
                return DrawingAnalyzer.AnalyzeAsync(picture, _recognizer.IsAvailable ? _recognizer : null, progress, ct);
            },
            ct);
    }

    private void StartReview(SourceFile source, CadSourceKind kind, PreviewImage? preview)
    {
        _source = source;
        _kind = kind;
        Preview = preview;
        PdfNote = source.PageCount is > 1 and var pages ? Loc.Format("Architecture.Pdf.FirstPageOnly", pages) : null;
        if (kind != CadSourceKind.Raster)
        {
            _analysis = null;
            _advice = null;
            Rows.Clear();
            Banner = ToCadBanner.Vector;
            RaiseBanner();
        }

        FormatIndex = 0;
        Phase = ArchitecturePhase.Review;
        RaiseSource();
    }

    // ---- The file ----

    public string SourceName => _source?.Name ?? string.Empty;

    public string SourcePath => _source?.Path ?? string.Empty;

    public bool IsRaster => _kind == CadSourceKind.Raster;

    public bool IsVector => _kind != CadSourceKind.Raster;

    /// <summary>"3200 × 2400 · 7,7 MP · setelah upscale 2×", or the kind and size of a vector file.</summary>
    public string SizeCaption
    {
        get
        {
            if (_source is null)
            {
                return string.Empty;
            }

            if (_kind != CadSourceKind.Raster)
            {
                return _source.Description;
            }

            var dimensions = ScaleText.Dimensions(_pictureWidth, _pictureHeight);
            var megapixels = ScaleText.Megapixels((long)_pictureWidth * _pictureHeight, Loc.Culture);
            return _upscaleFactor > 1
                ? Loc.Format("Architecture.Preview.SizeUpscaled", dimensions, megapixels, ScaleText.Format(_upscaleFactor, Loc.Culture))
                : Loc.Format("Architecture.Preview.Size", dimensions, megapixels);
        }
    }

    public string TypeCaption => Loc.Get(_kind == CadSourceKind.Raster ? "Architecture.Preview.Raster" : "Architecture.Preview.Vector");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(HasNoPreview), nameof(PreviewAlt))]
    public partial PreviewImage? Preview { get; set; }

    public bool HasPreview => Preview is not null;

    public bool HasNoPreview => Preview is null;

    public string PreviewAlt => Loc.Format("Architecture.Preview.Alt", SourceName);

    /// <summary>"This PDF has 3 pages. Only the first page is converted."</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPdfNote))]
    public partial string? PdfNote { get; set; }

    public bool HasPdfNote => PdfNote is not null;

    private void RaiseSource()
    {
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(IsRaster));
        OnPropertyChanged(nameof(IsVector));
        OnPropertyChanged(nameof(ShowsPdfOptions));
        OnPropertyChanged(nameof(SizeCaption));
        OnPropertyChanged(nameof(TypeCaption));
        OnPropertyChanged(nameof(PreviewAlt));
        OnPropertyChanged(nameof(ShowsFormatChoice));
        OnPropertyChanged(nameof(VectorHint));
        OnPropertyChanged(nameof(CalibrationCaption));
    }

    // ---- The analysis ----

    public ObservableCollection<ObjectRow> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public bool HasCheckedRows => Rows.Any(r => r.IsChecked);

    /// <summary>Nothing is ticked: say so under the list, and the button waits.</summary>
    public bool ShowsNothingTicked => IsRaster && HasRows && !HasCheckedRows;

    /// <summary>The boxes drawn on the preview: blue for what becomes CAD, dashed gray for what doesn't, yellow where the picture is unclear.</summary>
    public IReadOnlyList<OverlayBox> Overlay
    {
        get
        {
            if (_analysis is null)
            {
                return [];
            }

            var boxes = new List<OverlayBox>();
            foreach (var row in Rows)
            {
                var style = row.IsChecked ? OverlayStyle.Included : OverlayStyle.Excluded;
                boxes.AddRange(_analysis.Group(row.Kind).Items.Select(item => new OverlayBox(item.Bounds, style)));
            }

            boxes.AddRange(_analysis.UnclearAreas.Select(area => new OverlayBox(area, OverlayStyle.Unclear)));
            return boxes;
        }
    }

    /// <summary>The size of the picture the boxes lie on.</summary>
    public (int Width, int Height) OverlaySize => _analysis is null ? (0, 0) : (_analysis.Width, _analysis.Height);

    public string VectorHint => Loc.Get(_kind == CadSourceKind.Dxf ? "Architecture.Objects.VectorHint" : "Architecture.Objects.PdfHint");

    /// <summary>A PDF drawn with lines: the unit and the scale of the drawing are the user's to choose.</summary>
    public bool ShowsPdfOptions => _kind == CadSourceKind.VectorPdf;

    private static readonly DrawingObjectKind[] KindOrder =
    [
        DrawingObjectKind.Walls,
        DrawingObjectKind.Openings,
        DrawingObjectKind.Text,
        DrawingObjectKind.Logo,
        DrawingObjectKind.Table,
    ];

    private void ApplyAnalysis(DrawingAnalysis analysis, bool afterUpscale)
    {
        _analysis = analysis;
        Preview = _preview;

        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();
        foreach (var kind in KindOrder)
        {
            var group = analysis.Group(kind);
            if (group.IsEmpty)
            {
                continue;
            }

            // Logos and tables are the title block and its legend, which most users don't want as drawing objects.
            var row = new ObjectRow(kind, KindName(kind), CountText(kind, group.Count), KindDescription(kind, analysis), kind is not (DrawingObjectKind.Logo or DrawingObjectKind.Table));
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        if (analysis.ObjectCount == 0)
        {
            Banner = ToCadBanner.NothingFound;
        }
        else if (afterUpscale)
        {
            _advice = null;
            Banner = ToCadBanner.Ready;
        }
        else
        {
            ApplyAdvice(keepSkipped: false);
        }

        RaiseSource();
        RaiseRows();
        RaiseBanner();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ObjectRow.IsChecked))
        {
            RaiseRows();
        }
    }

    private void RaiseRows()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasCheckedRows));
        OnPropertyChanged(nameof(ShowsNothingTicked));
        OnPropertyChanged(nameof(Overlay));
        OnPropertyChanged(nameof(OverlaySize));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    private static string KindName(DrawingObjectKind kind) => Loc.Get(kind switch
    {
        DrawingObjectKind.Walls => "Architecture.Kind.Walls",
        DrawingObjectKind.Openings => "Architecture.Kind.Openings",
        DrawingObjectKind.Text => "Architecture.Kind.Text",
        DrawingObjectKind.Logo => "Architecture.Kind.Logo",
        _ => "Architecture.Kind.Table",
    });

    private static string CountText(DrawingObjectKind kind, int count)
    {
        var (many, one) = kind switch
        {
            DrawingObjectKind.Walls => ("Architecture.Count.Lines", "Architecture.Count.LinesOne"),
            DrawingObjectKind.Text => ("Architecture.Count.Texts", "Architecture.Count.TextsOne"),
            DrawingObjectKind.Table => ("Architecture.Count.Tables", "Architecture.Count.TablesOne"),
            _ => ("Architecture.Count.Objects", "Architecture.Count.ObjectsOne"),
        };
        return count == 1 ? Loc.Get(one) : Loc.Format(many, count);
    }

    private static string KindDescription(DrawingObjectKind kind, DrawingAnalysis analysis) => Loc.Get(kind switch
    {
        DrawingObjectKind.Walls => "Architecture.Desc.Walls",
        DrawingObjectKind.Openings => "Architecture.Desc.Openings",
        DrawingObjectKind.Text => analysis.TextWasRead ? "Architecture.Desc.Text" : "Architecture.Desc.TextLines",
        _ => "Architecture.Desc.Object",
    });

    // ---- Banner and the upscale advice ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnclear), nameof(IsNotEligible), nameof(IsUpscaling), nameof(IsReady), nameof(IsSkipped), nameof(IsVectorBanner), nameof(IsNothingFound), nameof(ConvertLabel))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand), nameof(PickSourceCommand))]
    public partial ToCadBanner Banner { get; set; }

    public bool IsUnclear => Banner == ToCadBanner.Unclear;

    public bool IsNotEligible => Banner == ToCadBanner.NotEligible;

    public bool IsUpscaling => Banner == ToCadBanner.Upscaling;

    public bool IsReady => Banner == ToCadBanner.Ready;

    public bool IsSkipped => Banner == ToCadBanner.Skipped;

    public bool IsVectorBanner => Banner == ToCadBanner.Vector;

    public bool IsNothingFound => Banner == ToCadBanner.NothingFound;

    public ObservableCollection<UpscaleChoiceItem> UpscaleChoices { get; } = [];

    private RenderEngine EffectiveEngine => UpscaleSupport.EffectiveEngine(_settings.RenderMode, _settings.Device);

    private UpscaleAdvice Advise() => ArchitectureUpscalePolicy.Advise(
        _analysis?.UnclearAreas.Count ?? 0,
        _settings.Device,
        _originalWidth,
        _originalHeight,
        _settings.ScaleLimit,
        _settings.MemoryLimitGb * GiB,
        EffectiveEngine);

    private void ApplyAdvice(bool keepSkipped)
    {
        var advice = Advise();
        _advice = advice;
        UpscaleChoices.Clear();
        foreach (var choice in advice.Choices)
        {
            UpscaleChoices.Add(new UpscaleChoiceItem(choice.Scale, choice.IsAllowed));
        }

        if (!keepSkipped)
        {
            Banner = advice.Kind switch
            {
                UpscaleAdviceKind.NotNeeded => ToCadBanner.Ready,
                UpscaleAdviceKind.Suggest => ToCadBanner.Unclear,
                _ => ToCadBanner.NotEligible,
            };
        }

        RaiseBanner();
    }

    /// <summary>The limits in Settings changed while a picture is open: the offer follows them.</summary>
    private void RefreshAdvice()
    {
        if (Phase != ArchitecturePhase.Review || _kind != CadSourceKind.Raster || _analysis is null || _upscaleFactor > 1
            || Banner is ToCadBanner.Upscaling or ToCadBanner.Vector or ToCadBanner.NothingFound)
        {
            return;
        }

        ApplyAdvice(keepSkipped: Banner == ToCadBanner.Skipped);
    }

    private void RaiseBanner()
    {
        OnPropertyChanged(nameof(UnclearTitle));
        OnPropertyChanged(nameof(UnclearMessage));
        OnPropertyChanged(nameof(NotEligibleMessage));
        OnPropertyChanged(nameof(UpscaleButtonText));
        OnPropertyChanged(nameof(UpscaleButtonSpoken));
        OnPropertyChanged(nameof(CanUpscale));
        OnPropertyChanged(nameof(ReadyMessage));
        OnPropertyChanged(nameof(SkippedMessage));
        OnPropertyChanged(nameof(SkippedActionText));
        OnPropertyChanged(nameof(VectorTitle));
        OnPropertyChanged(nameof(VectorMessage));
        OnPropertyChanged(nameof(UpscalingTitle));
        OnPropertyChanged(nameof(ConvertLabel));
    }

    private int UnclearCount => _analysis?.UnclearAreas.Count ?? 0;

    public bool CanUpscale => _advice?.CanUpscale ?? false;

    private int DefaultScale => _advice?.DefaultScale ?? 2;

    public string UnclearTitle => Loc.Format("Architecture.Unclear.Title", UnclearCount);

    public string UnclearMessage => Loc.Format("Architecture.Unclear.Message", ScaleText.Format(_advice?.Limit.Effective ?? 2, Loc.Culture));

    public string UpscaleButtonText => Loc.Format("Architecture.Unclear.UpscaleFirst", ScaleText.Format(DefaultScale, Loc.Culture));

    public string UpscaleButtonSpoken => Loc.Format("Architecture.Unclear.UpscaleSpoken", ScaleText.Format(DefaultScale, Loc.Culture));

    public string NotEligibleMessage
    {
        get
        {
            if (_advice is not { } advice)
            {
                return string.Empty;
            }

            if (advice.IsRamShort)
            {
                return Loc.Format("Architecture.NotEligible.MessageRam", advice.RequiredRamGb, advice.InstalledRamGb);
            }

            return Loc.Format("Architecture.NotEligible.MessageLimit", NotEligibleReason(advice));
        }
    }

    /// <summary>Why a 2× upscale isn't possible although the RAM is enough: the picture, the limits, or the memory limit of Settings.</summary>
    private string NotEligibleReason(UpscaleAdvice advice)
    {
        if ((long)_originalWidth * _originalHeight > UpscaleSupport.MaximumSourcePixels)
        {
            return Loc.Get("Architecture.NotEligible.TooBig");
        }

        if (advice.Limit.Effective >= 2)
        {
            var (outputWidth, outputHeight) = UpscaleEstimator.OutputSize(_originalWidth, _originalHeight, 2);
            var peak = UpscaleMemory.Plan(_originalWidth, _originalHeight, outputWidth, outputHeight, EffectiveEngine, keepsAlpha: true, _settings.MemoryLimitGb * GiB).PeakBytes;
            return Loc.Format("Upscale.Estimate.MemoryShort", _settings.MemoryLimitGb, ScaleText.Gigabytes(peak, Loc.Culture));
        }

        var reasons = advice.Limit.DescribeReasons(Loc.Culture);
        return reasons.Length > 0 ? reasons : Loc.Get("Architecture.NotEligible.TooBig");
    }

    private int ClearPercent => (int)Math.Floor((_analysis?.ClearShare ?? 1) * 100);

    public string ReadyMessage => _upscaleFactor > 1
        ? Loc.Format("Architecture.Ready.MessageUpscaled", ScaleText.Format(_upscaleFactor, Loc.Culture), ClearPercent)
        : Loc.Format("Architecture.Ready.Message", ClearPercent);

    public string SkippedMessage => Loc.Format("Architecture.Skipped.Message", UnclearCount);

    public string SkippedActionText => Loc.Format("Architecture.Skipped.UpscaleNow", ScaleText.Format(DefaultScale, Loc.Culture));

    public string VectorTitle => Loc.Get("Architecture.Vector.Title");

    public string VectorMessage => Loc.Get(_kind == CadSourceKind.Dxf ? "Architecture.Vector.Dxf" : "Architecture.Vector.Message");

    public string NothingFoundTitle => Loc.Get("Architecture.NothingFound.Title");

    public string NothingFoundMessage => Loc.Get("Architecture.NothingFound.Message");

    [RelayCommand]
    private void SkipUpscale() => Banner = ToCadBanner.Skipped;

    [RelayCommand]
    private void ContinueWithoutUpscale() => Banner = ToCadBanner.Skipped;

    // ---- Upscale ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpscaleProgressText))]
    public partial double UpscalePercent { get; set; }

    public string UpscaleProgressText => $"{UpscalePercent:0}%";

    public string UpscalingTitle => Loc.Format("Architecture.Upscaling.Title", ScaleText.Format(_pendingScale, Loc.Culture));

    [RelayCommand]
    private Task UpscaleDefaultAsync() => StartUpscaleAsync(DefaultScale);

    [RelayCommand]
    private void CancelUpscale() => _work?.Cancel();

    /// <summary>Enlarges the picture and looks at it again; the choices made in the list are kept where the kinds still exist.</summary>
    public async Task StartUpscaleAsync(int scale)
    {
        if (Phase != ArchitecturePhase.Review || _kind != CadSourceKind.Raster || _source is null || _advice is null
            || !_advice.Choices.Any(c => c.Scale == scale && c.IsAllowed) || Banner == ToCadBanner.Upscaling)
        {
            return;
        }

        _bannerBefore = Banner;
        _pendingScale = scale;
        UpscalePercent = 0;
        Message = null;
        Banner = ToCadBanner.Upscaling;
        RaiseBanner();

        using var work = new CancellationTokenSource();
        _work = work;
        var ct = work.Token;
        var input = _source.Path;
        string? temporaryInput = null;
        var output = ArchitectureFiles.CachePath(".png");
        var started = System.Diagnostics.Stopwatch.StartNew();
        _log.Info($"Architecture upscale started: {ScaleText.Format(scale, CultureInfo.InvariantCulture)}");
        try
        {
            var width = _originalWidth;
            var height = _originalHeight;
            if (_source.Extension == ".pdf")
            {
                // The page is a picture only once it is drawn: write it out, so the upscale has a file to read.
                var page = await Task.Run(() => WindowsPictureReader.ReadAsync(_source.Path, ct), ct);
                temporaryInput = ArchitectureFiles.CachePath(".png");
                await ArchitectureFiles.WritePngAsync(page, temporaryInput, ct);
                input = temporaryInput;
                width = page.Width;
                height = page.Height;
            }

            var (outputWidth, outputHeight) = UpscaleEstimator.OutputSize(width, height, scale);
            var plan = UpscaleMemory.Plan(width, height, outputWidth, outputHeight, EffectiveEngine, keepsAlpha: true, _settings.MemoryLimitGb * GiB);
            var job = new ConversionJob(input, ".png", output, new UpscaleOptions(outputWidth, outputHeight, _settings.RenderMode, plan.TileSize));
            var progress = new Progress<PipelineProgress>(p => UpscalePercent = Math.Floor(p.OverallFraction * UpscaleShare * 100));
            await Task.Run(() => _upscalePipeline.RunAsync(job, progress, ct), CancellationToken.None);
            ct.ThrowIfCancellationRequested();

            var enlarged = await Task.Run(() => WindowsPictureReader.ReadAsync(output, ct), ct);

            // The enlarged picture draws the same sheet at more pixels: its resolution grows with it, so the size in the drawing stays.
            enlarged = enlarged with
            {
                DpiX = EffectiveDpi(_originalDpiX) * scale,
                DpiY = EffectiveDpi(_originalDpiY) * scale,
            };
            var analysis = await AnalyzeAsync(enlarged, percent => UpscalePercent = Math.Floor((UpscaleShare + (percent * (1 - UpscaleShare))) * 100), ct);

            _upscaleFactor = scale;
            _pictureWidth = enlarged.Width;
            _pictureHeight = enlarged.Height;
            ApplyAnalysis(analysis, afterUpscale: true);
            _log.Info(string.Create(CultureInfo.InvariantCulture, $"Architecture upscale done: {scale}x, {analysis.UnclearAreas.Count} unclear areas left, {started.Elapsed.TotalSeconds:0.0} s"));
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            Banner = _bannerBefore;
            ShowMessage(Loc.Get("Architecture.Upscale.Cancelled"), InfoBarSeverity.Informational);
            _log.Info("Architecture upscale cancelled");
        }
        catch (Exception ex)
        {
            Banner = ToCadBanner.Skipped;
            ShowMessage(
                ex is UpscaleModelUnavailableException ? ErrorMessages.Describe(ex, PipelineStage.Decode, "PNG") : Loc.Get("Architecture.Upscale.Failed"),
                InfoBarSeverity.Warning);
            _log.Error($"Architecture upscale failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _work = null;
            ArchitectureFiles.Delete(output);
            if (temporaryInput is not null)
            {
                ArchitectureFiles.Delete(temporaryInput);
            }

            RaiseBanner();
        }
    }

    /// <summary>The resolution a file states, or the one assumed when it doesn't (96 pixels per inch).</summary>
    private static double EffectiveDpi(double dpi) => double.IsFinite(dpi) && dpi > 0 ? dpi : ArchitectureCadBuilder.DefaultDpi;

    // ---- Format and scale ----

    /// <summary>0 is DWG, 1 is DXF. A DXF is only ever converted to DWG.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetExtension), nameof(TargetCode), nameof(ConvertLabel))]
    public partial int FormatIndex { get; set; }

    public bool ShowsFormatChoice => _kind != CadSourceKind.Dxf;

    public string TargetExtension => _kind != CadSourceKind.Dxf && FormatIndex == 1 ? ".dxf" : ".dwg";

    public string TargetCode => FileExtension.ToCode(TargetExtension);

    /// <summary>"Tetap konversi…" while the picture has unclear areas and nothing was decided about them.</summary>
    public string ConvertLabel => Loc.Format(Banner is ToCadBanner.Unclear or ToCadBanner.NotEligible ? "Architecture.ConvertAnyway" : "Architecture.Convert", TargetCode);

    public string CalibrationCaption => _calibration is { } value
        ? Loc.Format("Architecture.Scale.Calibrated", value.ToString(value >= 0.1 ? "0.###" : "0.####", Loc.Culture))
        : Loc.Get("Architecture.Scale.NotCalibrated");

    public string CalibrationButtonText => Loc.Get(_calibration is null ? "Architecture.Scale.Calibrate" : "Architecture.Scale.Recalibrate");

    /// <summary>
    /// Takes two points marked on the preview and the distance between them in the real drawing. The size is kept for the
    /// picture as it was chosen, so an upscale afterwards doesn't change it.
    /// </summary>
    /// <returns>False when the points or the distance mean nothing.</returns>
    public bool ApplyCalibration(double x1, double y1, double x2, double y2, double lengthMillimeters)
    {
        if (_analysis is null || ScaleCalibration.MillimetersPerPixel(x1, y1, x2, y2, lengthMillimeters) is not { } perAnalysisPixel)
        {
            return false;
        }

        // An analysis pixel stands for Reduction pixels of the picture; the picture may be an enlarged copy of the one chosen.
        _calibration = perAnalysisPixel / _analysis.Reduction * _upscaleFactor;
        OnPropertyChanged(nameof(CalibrationCaption));
        OnPropertyChanged(nameof(CalibrationButtonText));
        return true;
    }

    private double MillimetersPerAnalysisPixel(DrawingAnalysis analysis) =>
        _calibration is { } perPicturePixel
            ? perPicturePixel / _upscaleFactor * analysis.Reduction
            : ArchitectureCadBuilder.MillimetersPerPixelFromDpi(analysis);

    // ---- Converting ----

    private bool CanConvert() =>
        Phase == ArchitecturePhase.Review
        && !Run.IsActive
        && Banner is not (ToCadBanner.Upscaling or ToCadBanner.NothingFound or ToCadBanner.None)
        && (_kind != CadSourceKind.Raster || HasCheckedRows)
        && (_kind != CadSourceKind.VectorPdf || Pdf.HasValidScale);

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        if (_source is null)
        {
            return;
        }

        var extension = TargetExtension;
        var destination = await _desktop.PickDestinationAsync(Path.GetFileNameWithoutExtension(_source.Path), Path.GetDirectoryName(_source.Path), TargetCode, extension);
        if (destination is null)
        {
            return;
        }

        // A name typed with another extension still gets the real one, so the file is never mislabeled.
        if (FileExtension.FromPath(destination) != extension)
        {
            destination += extension;
        }

        Message = null;
        ConversionPipeline pipeline;
        ConversionOptions? options;
        switch (_kind)
        {
            case CadSourceKind.Raster when _analysis is not null:
                pipeline = _architecturePipeline;
                options = new ArchitectureCadOptions(_analysis, Rows.Where(r => r.IsChecked).Select(r => r.Kind).ToHashSet(), MillimetersPerAnalysisPixel(_analysis));
                break;
            case CadSourceKind.VectorPdf:
                pipeline = _pipeline;
                options = Pdf.BuildOptions(extension);
                break;
            case CadSourceKind.Dxf:
                pipeline = _pipeline;
                options = null;
                break;
            default:
                return;
        }

        await Run.RunAsync(
            pipeline,
            new ConversionJob(_source.Path, extension, destination, options),
            $"{FileExtension.ToCode(_source.Extension)} → {TargetCode}",
            TargetCode);
    }

    // ---- Starting over ----

    /// <summary>Forgets the file and goes back to the drop area.</summary>
    public void Reset()
    {
        _work?.Cancel();
        _source = null;
        _analysis = null;
        _advice = null;
        _preview = null;
        _upscaleFactor = 1;
        _calibration = null;
        Run.Reset();
        Rows.Clear();
        UpscaleChoices.Clear();
        Preview = null;
        PdfNote = null;
        Banner = ToCadBanner.None;
        Phase = ArchitecturePhase.Empty;
        RaiseSource();
        RaiseRows();
        RaiseBanner();
    }
}

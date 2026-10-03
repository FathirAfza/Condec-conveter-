// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Architecture;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Pipeline;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

/// <summary>One layer of the drawing in the "Layer" list.</summary>
public sealed partial class LayerRow : ObservableObject
{
    public LayerRow(string name, bool isShown)
    {
        Name = name;
        DisplayName = CadNames.Layer(name);
        IsShown = isShown;
    }

    /// <summary>The name the file gives the layer.</summary>
    public string Name { get; }

    /// <summary>The name shown: Condec's own layers are named in the app's language.</summary>
    public string DisplayName { get; }

    /// <summary>Whether the layer is drawn. Kept in step with the list's selection by the view.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Status), nameof(AutomationName))]
    public partial bool IsShown { get; set; }

    public string Status => Loc.Get(IsShown ? "Architecture.Layer.Shown" : "Architecture.Layer.Hidden");

    public string AutomationName => Loc.Format("Architecture.Layer.Spoken", DisplayName, Status);
}

/// <summary>
/// Architecture, "DWG, DXF → Gambar, PDF" (DESIGN §6.3.2): the drawing is read here and drawn with Condec's own renderer, so
/// the user can choose layers, paper, resolution and background. Everything runs on this device.
/// </summary>
public sealed partial class FromCadViewModel : ObservableObject
{
    private static readonly string[] SourceExtensions = [".dwg", ".dxf"];

    private static readonly int[] DpiValues = [150, 300, 600];

    private readonly ConversionPipeline _pipeline;
    private readonly IDesktopServices _desktop;
    private readonly ActivityLog _log;
    private CancellationTokenSource? _work;
    private CancellationTokenSource? _previewWork;
    private CadScene? _scene;
    private SourceFile? _source;

    /// <param name="pipeline">The pipeline whose registry holds <see cref="CadRenderConverter"/>.</param>
    public FromCadViewModel(ConversionPipeline pipeline, ConversionRun run, IDesktopServices desktop, ActivityLog log)
    {
        _pipeline = pipeline;
        Run = run;
        _desktop = desktop;
        _log = log;

        Layers.CollectionChanged += (_, _) => RaiseLayers();
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
    }

    /// <summary>The progress list, the finished card and the failed card.</summary>
    public ConversionRun Run { get; }

    // ---- Phase ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEmpty), nameof(ShowsReading), nameof(ShowsReview))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand), nameof(PickSourceCommand))]
    public partial ArchitecturePhase Phase { get; set; }

    public bool ShowsEmpty => Phase == ArchitecturePhase.Empty && !Run.IsActive;

    public bool ShowsReading => Phase == ArchitecturePhase.Reading && !Run.IsActive;

    public bool ShowsReview => Phase == ArchitecturePhase.Review && !Run.IsActive;

    // ---- Messages ----

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

    [RelayCommand]
    private void CancelReading() => _work?.Cancel();

    // ---- Choosing a file ----

    private bool CanPick() => !Run.IsActive && Phase != ArchitecturePhase.Reading;

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
                    ? Loc.Get("Architecture.FromCad.UnsupportedNoExtension")
                    : Loc.Format("Architecture.FromCad.Unsupported", FileExtension.ToCode(extension)),
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
        ReadingTitle = Loc.Format("Architecture.FromCad.ReadingTitle", Path.GetFileName(path));
        Phase = ArchitecturePhase.Reading;

        using var work = new CancellationTokenSource();
        _work = work;
        try
        {
            var scene = await ArchitectureFiles.ReadDrawingAsync(path, work.Token);
            var rows = LayersOf(scene);
            if (rows.Count == 0)
            {
                Reset();
                ShowMessage(Loc.Get("Architecture.FromCad.Empty"), InfoBarSeverity.Warning);
                return;
            }

            _scene = scene;
            _source = new SourceFile(path, extension, size);
            foreach (var row in rows)
            {
                row.PropertyChanged += OnLayerChanged;
                Layers.Add(row);
            }

            // The first picture is drawn before the page shows, so the card is not empty when it appears.
            await RenderPreviewAsync(delay: false, work.Token);
            work.Token.ThrowIfCancellationRequested();

            IsPreviewWorking = false;
            Phase = ArchitecturePhase.Review;
            RaiseSource();
            RaiseLayers();
            _log.Info($"Drawing read: {rows.Count} layers, unit {scene.Units}");
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

    /// <summary>The layers that have something to draw, in the order of the file's layer table; each starts as the file has it.</summary>
    private static List<LayerRow> LayersOf(CadScene scene)
    {
        var used = scene.Paths.Select(p => p.Layer).Concat(scene.Labels.Select(l => l.Layer)).ToHashSet();
        var rows = scene.Layers.Where(l => used.Contains(l.Name)).Select(l => new LayerRow(l.Name, l.IsOn)).ToList();

        // A shape on a layer the table doesn't list is still drawn by the file's own viewer.
        var listed = rows.Select(r => r.Name).ToHashSet();
        rows.AddRange(used.Where(name => !listed.Contains(name)).Order(StringComparer.Ordinal).Select(name => new LayerRow(name, true)));
        return rows;
    }

    // ---- The drawing ----

    public string SourceName => _source?.Name ?? string.Empty;

    public string ReadyTitle => Loc.Get(_source?.Extension == ".dxf" ? "Architecture.FromCad.ReadyDxf" : "Architecture.FromCad.ReadyDwg");

    public string ReadyMessage => Layers.Count == 1
        ? Loc.Format("Architecture.FromCad.ReadyMessageOne", Unit)
        : Loc.Format("Architecture.FromCad.ReadyMessage", Layers.Count, Unit);

    private string Unit => _scene is null ? string.Empty : CadNames.Unit(_scene.Units);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(HasNoPreview), nameof(PreviewAlt))]
    public partial PreviewImage? Preview { get; set; }

    public bool HasPreview => Preview is not null;

    public bool HasNoPreview => Preview is null;

    public string PreviewAlt => Loc.Format("Architecture.Preview.Alt", SourceName);

    /// <summary>The preview is being drawn again after a change.</summary>
    [ObservableProperty]
    public partial bool IsPreviewWorking { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewMessage))]
    public partial string? PreviewMessage { get; set; }

    public bool HasPreviewMessage => PreviewMessage is not null;

    public ObservableCollection<LayerRow> Layers { get; } = [];

    public bool HasShownLayers => Layers.Any(l => l.IsShown);

    public bool ShowsNoLayerNote => Layers.Count > 0 && !HasShownLayers;

    private void OnLayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LayerRow.IsShown))
        {
            RaiseLayers();
            SchedulePreview();
        }
    }

    private void RaiseLayers()
    {
        OnPropertyChanged(nameof(HasShownLayers));
        OnPropertyChanged(nameof(ShowsNoLayerNote));
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(ReadyMessage));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    private void RaiseSource()
    {
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(ReadyTitle));
        OnPropertyChanged(nameof(ReadyMessage));
        OnPropertyChanged(nameof(PreviewAlt));
        OnPropertyChanged(nameof(EstimateText));
    }

    // ---- Options ----

    /// <summary>0 PDF, 1 PNG, 2 JPG.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Format), nameof(IsPdf), nameof(IsPng), nameof(DpiEnabled), nameof(TransparentEnabled), nameof(EstimateText))]
    public partial int FormatIndex { get; set; }

    /// <summary>0 A4, 1 A3.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Paper), nameof(EstimateText))]
    public partial int PaperIndex { get; set; } = 1;

    /// <summary>0 150, 1 300, 2 600 dpi.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Dpi), nameof(EstimateText))]
    public partial int DpiIndex { get; set; } = 1;

    /// <summary>0 white, 1 transparent.</summary>
    [ObservableProperty]
    public partial int BackgroundIndex { get; set; }

    public CadResultFormat Format => FormatIndex switch
    {
        1 => CadResultFormat.Png,
        2 => CadResultFormat.Jpg,
        _ => CadResultFormat.Pdf,
    };

    public bool IsPdf => Format == CadResultFormat.Pdf;

    public bool IsPng => Format == CadResultFormat.Png;

    /// <summary>A PDF is vector: it has no resolution.</summary>
    public bool DpiEnabled => !IsPdf;

    /// <summary>Only a PNG can be transparent.</summary>
    public bool TransparentEnabled => IsPng;

    public PaperSize Paper => PaperIndex == 0 ? PaperSize.A4 : PaperSize.A3;

    public int Dpi => DpiValues[Math.Clamp(DpiIndex, 0, DpiValues.Length - 1)];

    private string TargetExtension => Format switch
    {
        CadResultFormat.Png => ".png",
        CadResultFormat.Jpg => ".jpg",
        _ => ".pdf",
    };

    private string TargetCode => FileExtension.ToCode(TargetExtension);

    partial void OnFormatIndexChanged(int value)
    {
        // A background that this format can't have goes back to white, so what is shown is what is made.
        if (value != 1 && BackgroundIndex == 1)
        {
            BackgroundIndex = 0;
        }
    }

    partial void OnPaperIndexChanged(int value) => SchedulePreview();

    /// <summary>"Perkiraan hasil: 4961 × 3508 px · ± 1,7 MB".</summary>
    public string EstimateText
    {
        get
        {
            if (_scene is null || !HasShownLayers)
            {
                return string.Empty;
            }

            if (IsPdf)
            {
                return Loc.Format("Architecture.Estimate", Loc.Format("Architecture.Estimate.Pdf", DisplayFormat.FormatFileSize(CadResultEstimate.PdfBytes, Loc.Culture)));
            }

            var landscape = IsLandscape(_scene, HiddenLayers());
            var (width, height) = CadResultEstimate.Pixels(Paper, Dpi, landscape);
            var bytes = CadResultEstimate.Bytes(Format, width, height);
            return Loc.Format("Architecture.Estimate", Loc.Format("Architecture.Estimate.Picture", width, height, DisplayFormat.FormatFileSize(bytes, Loc.Culture)));
        }
    }

    /// <summary>The sheet is used the way round the drawing fills it better, the same choice <see cref="CadPdfWriter"/> makes.</summary>
    private static bool IsLandscape(CadScene scene, IReadOnlySet<string> hidden) =>
        scene.BoundsOf(name => !hidden.Contains(name)) is not { } bounds || bounds.MaxX - bounds.MinX >= bounds.MaxY - bounds.MinY;

    private HashSet<string> HiddenLayers() => [.. Layers.Where(l => !l.IsShown).Select(l => l.Name)];

    // ---- The preview ----

    private void SchedulePreview()
    {
        _previewWork?.Cancel();
        if (_scene is null || Phase != ArchitecturePhase.Review)
        {
            return;
        }

        var work = new CancellationTokenSource();
        _previewWork = work;
        _ = DrawPreviewAfterPauseAsync(work);
    }

    /// <summary>Waits a moment so a run of clicks on the layer list draws once, then draws.</summary>
    private async Task DrawPreviewAfterPauseAsync(CancellationTokenSource work)
    {
        try
        {
            await RenderPreviewAsync(delay: true, work.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer change has taken over.
        }
        finally
        {
            if (ReferenceEquals(_previewWork, work))
            {
                _previewWork = null;
                IsPreviewWorking = false;
            }
        }
    }

    private async Task RenderPreviewAsync(bool delay, CancellationToken ct)
    {
        if (_scene is not { } scene)
        {
            return;
        }

        if (delay)
        {
            await Task.Delay(200, ct);
        }

        var hidden = HiddenLayers();
        if (Layers.All(l => hidden.Contains(l.Name)))
        {
            Preview = null;
            PreviewMessage = null;
            return;
        }

        IsPreviewWorking = true;
        try
        {
            var image = await ArchitectureFiles.RenderDrawingAsync(scene, name => !hidden.Contains(name), Paper, ct);
            ct.ThrowIfCancellationRequested();
            Preview = image;
            PreviewMessage = null;
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
        {
            // The file can still be converted: the same drawing is made again from the file itself.
            Preview = null;
            PreviewMessage = Loc.Get("Architecture.FromCad.PreviewFailed");
            _log.Info($"No preview: {ex.GetType().Name}");
        }
    }

    // ---- Converting ----

    private bool CanConvert() => Phase == ArchitecturePhase.Review && !Run.IsActive && HasShownLayers;

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
        var options = new CadRenderOptions(Paper, Dpi, IsPng && BackgroundIndex == 1, HiddenLayers());
        await Run.RunAsync(
            _pipeline,
            new ConversionJob(_source.Path, extension, destination, options),
            $"{FileExtension.ToCode(_source.Extension)} → {TargetCode}",
            TargetCode);
    }

    // ---- Starting over ----

    /// <summary>Forgets the file and goes back to the drop area.</summary>
    public void Reset()
    {
        _work?.Cancel();
        _previewWork?.Cancel();
        _previewWork = null;
        foreach (var row in Layers)
        {
            row.PropertyChanged -= OnLayerChanged;
        }

        _scene = null;
        _source = null;
        Run.Reset();
        Layers.Clear();
        Preview = null;
        PreviewMessage = null;
        IsPreviewWorking = false;
        Phase = ArchitecturePhase.Empty;
        RaiseSource();
        RaiseLayers();
    }
}

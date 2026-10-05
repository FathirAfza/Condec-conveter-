// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Batch;
using Condec.Core.Compression;
using Condec.Core.Formats;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Pipeline;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

/// <summary>One picture in Compress Image's list, numbered in the order its results are made.</summary>
public sealed partial class CompressItem : ObservableObject
{
    public CompressItem(SourceFile file, int width, int height, Action<CompressItem> remove)
    {
        File = file;
        Width = width;
        Height = height;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public SourceFile File { get; }

    public int Width { get; }

    public int Height { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NumberText), nameof(AutomationName))]
    public partial int Number { get; set; }

    public string NumberText => Number.ToString(CultureInfo.CurrentCulture) + ".";

    public string Name => File.Name;

    /// <summary>"4032 × 3024 · 3.1 MB".</summary>
    public string Caption => $"{ScaleText.Dimensions(Width, Height)} · {DisplayFormat.FormatFileSize(File.Size)}";

    public string AutomationName => Loc.Format("Compress.ItemSpoken", Number, Name, Caption);

    public string RemoveName => Loc.Format("Sources.RemoveSpoken", Name);

    public IRelayCommand RemoveCommand { get; }
}

/// <summary>
/// Compress Image (DESIGN §6.5): one or more pictures, one way to make them smaller for all of them, a result for the
/// picture shown that is really encoded and measured, then the same progress and result cards as Architecture.
/// </summary>
public sealed partial class CompressViewModel : ObservableObject
{
    /// <summary>The preview waits this long after the last change, so dragging a slider doesn't encode at every step.</summary>
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(250);

    private readonly ConversionPipeline _pipeline;
    private readonly IDesktopServices _desktop;
    private readonly ActivityLog _log;
    private readonly IReadOnlyList<string> _targets = ImageCompressor.TargetExtensions;
    private CompressSource? _loaded;
    private CompressItem? _loadedFor;
    private CancellationTokenSource? _preview;
    private bool _renumbering;

    public CompressViewModel(ConversionPipeline pipeline, ConversionRun run, IDesktopServices desktop, ActivityLog log)
    {
        _pipeline = pipeline;
        Run = run;
        _desktop = desktop;
        _log = log;
        FormatChoices = [.. _targets.Select(FileExtension.ToCode)];

        Run.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversionRun.IsActive))
            {
                OnPropertyChanged(nameof(IsInput));
                StartCommand.NotifyCanExecuteChanged();
            }
        };
        Run.Cancelled += (_, _) => ShowInputMessage(Loc.Get("Input.Cancelled"), InfoBarSeverity.Informational);
        Run.AnotherRequested += (_, _) => Clear();
    }

    /// <summary>The progress list, the finished card and the failed card, for one picture or several.</summary>
    public ConversionRun Run { get; }

    /// <summary>The choices are shown: nothing is running and no result card is open.</summary>
    public bool IsInput => !Run.IsActive;

    // ---- Messages above the input ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInputMessage))]
    public partial string? InputMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity InputMessageSeverity { get; set; }

    public bool HasInputMessage => InputMessage is not null;

    [RelayCommand]
    private void DismissInputMessage() => InputMessage = null;

    private void ShowInputMessage(string message, InfoBarSeverity severity)
    {
        InputMessageSeverity = severity;
        InputMessage = message;
    }

    // ---- Choosing pictures ----

    /// <summary>"Pilih gambar…" and "Ganti": one or more pictures, which start a new list.</summary>
    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var paths = await _desktop.PickSourceFilesAsync(PickerExtensions());
        if (paths.Count > 0)
        {
            await SelectSourcesAsync(paths, add: false);
        }
    }

    /// <summary>"Tambah gambar…": more pictures at the end of the list; the dialog opens in the folder of the first one.</summary>
    [RelayCommand]
    private async Task AddSourcesAsync()
    {
        var paths = await _desktop.PickSourceFilesAsync(PickerExtensions(), Items.Count > 0 ? Path.GetDirectoryName(Items[0].File.Path) : null);
        if (paths.Count > 0)
        {
            await SelectSourcesAsync(paths, add: true);
        }
    }

    private static List<string> PickerExtensions() => [.. ImageCompressor.SourceExtensions.Order(StringComparer.Ordinal)];

    /// <summary>Drag and drop: one or more pictures start a new list, as "Pilih gambar…" does.</summary>
    /// <param name="filePaths">An empty path is a file with no place on disk, such as one inside a ZIP.</param>
    public async Task SelectDroppedAsync(IReadOnlyList<string> filePaths, int folderCount)
    {
        if (filePaths.Count == 0)
        {
            if (folderCount > 0)
            {
                ShowInputMessage(Loc.Get("Input.DropFolder"), InfoBarSeverity.Warning);
            }

            return;
        }

        var readable = filePaths.Where(p => p.Length > 0).ToList();
        if (readable.Count == 0)
        {
            ReportUnreadableDrop();
            return;
        }

        await SelectSourcesAsync(readable, add: false);
        if (folderCount > 0 && Items.Count > 0)
        {
            ShowInputMessage(Loc.Get("Input.FoldersSkipped"), InfoBarSeverity.Informational);
        }
    }

    public void ReportUnreadableDrop() =>
        ShowInputMessage(Loc.Get("Input.DropUnreadable"), InfoBarSeverity.Warning);

    /// <summary>Takes the pictures among <paramref name="paths"/>, and says which were left out and why.</summary>
    /// <param name="add">At the end of the list ("Tambah gambar…"), or as a new list.</param>
    private async Task SelectSourcesAsync(IReadOnlyList<string> paths, bool add)
    {
        if (Run.IsActive)
        {
            return;
        }

        var taken = new List<CompressItem>();
        var skipped = new List<string>();
        string? onlyMessage = null;
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var extension = FileExtension.FromPath(path);
            if (!ImageCompressor.SourceExtensions.Contains(extension))
            {
                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get("Skip.Unsupported")));
                onlyMessage = extension.Length == 0 ? Loc.Get("Compress.UnsupportedNoExtension") : Loc.Format("Compress.Unsupported", extension);
                continue;
            }

            if ((add ? Items : Enumerable.Empty<CompressItem>()).Concat(taken).Any(i => string.Equals(i.File.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var (item, reason, message) = await ReadPictureAsync(path, extension);
            if (item is null)
            {
                skipped.Add(Loc.Format("Skip.Item", name, Loc.Get(reason!)));
                onlyMessage = message;
                continue;
            }

            taken.Add(item);
        }

        if (taken.Count == 0)
        {
            // One picture on its own gets the message that says exactly what is wrong with it.
            if (skipped.Count > 0)
            {
                ShowInputMessage(paths.Count == 1 && onlyMessage is not null ? onlyMessage : Loc.Format("Input.NoneUsable", string.Join(", ", skipped)), InfoBarSeverity.Warning);
            }

            return;
        }

        InputMessage = null;
        if (skipped.Count > 0)
        {
            ShowInputMessage(Loc.Format("Input.SkippedSome", string.Join(", ", skipped)), InfoBarSeverity.Warning);
        }

        if (!add)
        {
            Items.Clear();
        }

        foreach (var item in taken)
        {
            Items.Add(item);
        }

        Renumber(add ? CurrentIndex : 0);
        _log.Info($"Compress: {taken.Count} pictures added, {Items.Count} in the list");
    }

    /// <summary>Reads the size of a picture from its header; a picture that can't be taken comes with its reasons.</summary>
    private async Task<(CompressItem? Item, string? SkipReason, string? Message)> ReadPictureAsync(string path, string extension)
    {
        long size;
        ImageHeaderInfo header;
        try
        {
            size = new FileInfo(path).Length;
            header = await Task.Run(() => ImageHeader.ReadAsync(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "Skip.Unreadable", Loc.Get("Input.CannotOpen"));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Windows throws its own exception types for a file that is not a picture, or one that is damaged.
            _log.Info($"Compress source not readable: {ex.GetType().Name}");
            return (null, "Skip.Unreadable", Loc.Get("Error.Decode"));
        }

        if (header.Pixels > ImageTooLargeException.MaximumPixels)
        {
            return (null, "Skip.TooLarge", Loc.Format("Error.ImageTooLarge", (header.Pixels + 500_000) / 1_000_000));
        }

        return (new CompressItem(new SourceFile(path, extension, size), (int)header.Width, (int)header.Height, Remove), null, null);
    }

    // ---- The list ----

    /// <summary>The pictures, in the order their results are numbered.</summary>
    public ObservableCollection<CompressItem> Items { get; } = [];

    /// <summary>The picture whose result is shown, as the list's selected index.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(SourceName), nameof(SourceCaption), nameof(SourcePath))]
    public partial int CurrentIndex { get; set; } = -1;

    partial void OnCurrentIndexChanged(int value)
    {
        if (!_renumbering)
        {
            SchedulePreview();
        }
    }

    public CompressItem? Current => CurrentIndex >= 0 && CurrentIndex < Items.Count ? Items[CurrentIndex] : null;

    public bool HasSource => Items.Count > 0;

    public bool HasNoSource => Items.Count == 0;

    /// <summary>Two pictures or more: the list is shown and the results go into a folder.</summary>
    public bool HasSeveral => Items.Count > 1;

    public string SourceName => Current?.Name ?? string.Empty;

    public string PhotoGlyph => FileGlyphs.Photo;

    public string SourcePath => Current?.File.Path ?? string.Empty;

    /// <summary>"4032 × 3024 · 12.2 MP · 3.1 MB" for the picture shown.</summary>
    public string SourceCaption => Current is { } item
        ? $"{ScaleText.Dimensions(item.Width, item.Height)} · {ScaleText.Megapixels((long)item.Width * item.Height, Loc.Culture)} · {DisplayFormat.FormatFileSize(item.File.Size)}"
        : string.Empty;

    /// <summary>"3 gambar · total 12.4 MB", above the list.</summary>
    public string ListTitle => Loc.Format("Compress.ListTitle", Items.Count, DisplayFormat.FormatFileSize(Items.Sum(i => i.File.Size)));

    private void Remove(CompressItem item)
    {
        var index = Items.IndexOf(item);
        if (index < 0 || Run.IsActive)
        {
            return;
        }

        Items.RemoveAt(index);
        if (ReferenceEquals(_loadedFor, item))
        {
            (_loaded, _loadedFor) = (null, null);
        }

        Renumber(Math.Min(CurrentIndex == index ? index : CurrentIndex > index ? CurrentIndex - 1 : CurrentIndex, Items.Count - 1));
    }

    /// <summary>Numbers the list again and shows <paramref name="current"/>.</summary>
    private void Renumber(int current)
    {
        _renumbering = true;
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].Number = i + 1;
        }

        CurrentIndex = Items.Count == 0 ? -1 : Math.Clamp(current, 0, Items.Count - 1);
        _renumbering = false;

        OnPropertyChanged(nameof(HasSource));
        OnPropertyChanged(nameof(HasNoSource));
        OnPropertyChanged(nameof(HasSeveral));
        OnPropertyChanged(nameof(ListTitle));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(SourceName));
        OnPropertyChanged(nameof(SourceCaption));
        OnPropertyChanged(nameof(SourcePath));
        OnPropertyChanged(nameof(StartLabel));
        StartCommand.NotifyCanExecuteChanged();
        SchedulePreview();
    }

    // ---- The choices (one set for every picture) ----

    /// <summary>"JPG", "PNG", and "HEIC" when this PC can write it.</summary>
    public IReadOnlyList<string> FormatChoices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetExtension), nameof(TargetCode), nameof(HasQuality), nameof(HasNoQuality), nameof(FormatCaption), nameof(QualityNote), nameof(LimitHelp))]
    public partial int FormatIndex { get; set; }

    partial void OnFormatIndexChanged(int value) => SchedulePreview();

    public string TargetExtension => _targets[Math.Clamp(FormatIndex, 0, _targets.Count - 1)];

    public string TargetCode => FileExtension.ToCode(TargetExtension);

    /// <summary>JPG and HEIC have a quality; PNG is lossless.</summary>
    public bool HasQuality => ImageCompressor.HasQuality(TargetExtension);

    public bool HasNoQuality => !HasQuality;

    public string FormatCaption => Loc.Get(TargetExtension switch
    {
        ".png" => "Compress.Format.Png",
        ".heic" => "Compress.Format.Heic",
        _ => "Compress.Format.Jpg",
    });

    /// <summary>0: quality and resolution by hand; 1: a file size limit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManual), nameof(IsTargetMode), nameof(IsTargetInvalid), nameof(QualityNote))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial int ModeIndex { get; set; }

    partial void OnModeIndexChanged(int value) => SchedulePreview();

    public bool IsManual => ModeIndex == 0;

    public bool IsTargetMode => ModeIndex == 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityText))]
    public partial double Quality { get; set; } = CompressOptions.DefaultQuality;

    partial void OnQualityChanged(double value) => SchedulePreview();

    public string QualityText => Percent((int)Quality);

    /// <summary>Why the quality slider is off, for PNG.</summary>
    public string QualityNote => HasQuality ? string.Empty : Loc.Get("Compress.QualityPng");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolutionText), nameof(ResolutionSizeText))]
    public partial double ResolutionPercent { get; set; } = CompressOptions.MaximumPercent;

    partial void OnResolutionPercentChanged(double value) => SchedulePreview();

    public string ResolutionText => Percent((int)ResolutionPercent);

    /// <summary>"2016 × 1512 · 3.0 MP" for the picture shown, under the resolution slider.</summary>
    public string ResolutionSizeText
    {
        get
        {
            if (Current is not { } item)
            {
                return string.Empty;
            }

            var (width, height) = CompressOptions.ScaledSize(item.Width, item.Height, (int)ResolutionPercent);
            return $"{ScaleText.Dimensions(width, height)} · {ScaleText.Megapixels((long)width * height, Loc.Culture)}";
        }
    }

    /// <summary>The size limit as typed; NaN when the box is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetValid), nameof(IsTargetInvalid))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial double TargetValue { get; set; } = 1;

    partial void OnTargetValueChanged(double value) => SchedulePreview();

    /// <summary>0: KB, 1: MB.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTargetValid), nameof(IsTargetInvalid))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial int TargetUnitIndex { get; set; } = 1;

    partial void OnTargetUnitIndexChanged(int value) => SchedulePreview();

    /// <summary>1 KB = 1,000 bytes and 1 MB = 1,000,000 bytes: the stricter reading of a limit, so the file fits either way.</summary>
    public long? TargetBytes
    {
        get
        {
            if (double.IsNaN(TargetValue) || TargetValue <= 0 || TargetValue > 1_000_000)
            {
                return null;
            }

            // Anything under 1 KB can't hold a picture; it is taken as a typing slip.
            var bytes = (long)Math.Floor(TargetValue * (TargetUnitIndex == 0 ? 1_000 : 1_000_000));
            return bytes >= 1_000 ? bytes : null;
        }
    }

    public bool IsTargetValid => TargetBytes is not null;

    public bool IsTargetInvalid => IsTargetMode && !IsTargetValid;

    /// <summary>How a limit is met and counted, under the limit box.</summary>
    public string LimitHelp => Loc.Get(HasQuality ? "Compress.LimitHelp" : "Compress.LimitHelpPng");

    private static string Percent(int value) => value.ToString(CultureInfo.InvariantCulture) + "%";

    // ---- The result of the picture shown, really encoded ----

    /// <summary>The compressed file of the picture shown, for the preview; null while there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewCaption))]
    public partial byte[]? PreviewData { get; set; }

    /// <summary>The preview's width in pixels, so the page can decode a large result at the size it is shown.</summary>
    public int PreviewWidth { get; private set; }

    /// <summary>"Pratinjau hasil, 2016 × 1512, 412 KB" for screen readers.</summary>
    [ObservableProperty]
    public partial string PreviewSpoken { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MeasuringText), nameof(PreviewCaption))]
    public partial bool IsMeasuring { get; set; }

    /// <summary>Under the preview: what is happening, or that the numbers come from the real file.</summary>
    public string PreviewCaption => IsMeasuring ? MeasuringText : PreviewData is null ? string.Empty : Loc.Get("Compress.PreviewCaption");

    /// <summary>"Mengompres…" or, for a limit, "Mencari ukuran yang pas…".</summary>
    public string MeasuringText => Loc.Get(IsTargetMode ? "Compress.Searching" : "Compress.Measuring");

    /// <summary>The limit can't be met, or the picture can't be read: said instead of a result.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviewProblem))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string? PreviewProblem { get; set; }

    public bool HasPreviewProblem => PreviewProblem is not null;

    [ObservableProperty]
    public partial string OriginalSizeValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalSizeCaption { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultSizeValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultSizeCaption { get; set; } = string.Empty;

    /// <summary>The result is not smaller than the source: its caption is in the caution color.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSmaller))]
    public partial bool IsLarger { get; set; }

    public bool IsSmaller => !IsLarger;

    [ObservableProperty]
    public partial string ResultResolutionValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultResolutionCaption { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultQualityValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ResultQualityCaption { get; set; } = string.Empty;

    /// <summary>Starts a new measurement of the picture shown; the one before it is stopped.</summary>
    private void SchedulePreview()
    {
        _preview?.Cancel();
        _preview = null;
        OnPropertyChanged(nameof(ResolutionSizeText));
        OnPropertyChanged(nameof(MeasuringText));
        if (Current is not { } item)
        {
            (_loaded, _loadedFor) = (null, null);
            ClearResult();
            IsMeasuring = false;
            return;
        }

        OriginalSizeValue = DisplayFormat.FormatFileSize(item.File.Size);
        OriginalSizeCaption = $"{FileExtension.ToCode(item.File.Extension)} · {ScaleText.Dimensions(item.Width, item.Height)}";
        if (IsTargetMode && TargetBytes is null)
        {
            ClearResult();
            IsMeasuring = false;
            return;
        }

        var preview = new CancellationTokenSource();
        _preview = preview;
        _ = MeasureAsync(item, preview.Token);
    }

    private async Task MeasureAsync(CompressItem item, CancellationToken ct)
    {
        var target = TargetExtension;
        var quality = (int)Quality;
        var percent = (int)ResolutionPercent;
        var limit = IsTargetMode ? TargetBytes : null;
        IsMeasuring = true;
        try
        {
            await Task.Delay(PreviewDelay, ct);
            CompressSource source;
            if (ReferenceEquals(_loadedFor, item) && _loaded is { } cached)
            {
                source = cached;
            }
            else
            {
                // The picture before is let go first: two decoded photos at once is more memory than needed.
                (_loaded, _loadedFor) = (null, null);
                source = await Task.Run(() => CompressSource.LoadAsync(item.File.Path, ct), ct);
                (_loaded, _loadedFor) = (source, item);
            }

            var result = await Task.Run(
                async () => limit is { } bytes
                    ? await source.FitAsync(target, bytes, ct)
                    : await source.EncodeAsync(target, quality, percent, ct),
                ct);
            ct.ThrowIfCancellationRequested();
            ShowResult(item, result, limit);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A newer measurement has started, or the list changed.
            return;
        }
        catch (ImageTooLargeException ex)
        {
            ClearResult();
            PreviewProblem = Loc.Format("Error.ImageTooLarge", ex.Megapixels);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Info($"Compress preview failed: {ex.GetType().Name}");
            ClearResult();
            PreviewProblem = ex is IOException or UnauthorizedAccessException ? Loc.Get("Input.CannotOpen") : Loc.Get("Error.Decode");
        }

        if (!ct.IsCancellationRequested)
        {
            IsMeasuring = false;
        }
    }

    private void ShowResult(CompressItem item, CompressedImage? result, long? limit)
    {
        if (result is null)
        {
            ClearResult();
            PreviewProblem = Loc.Format("Error.CompressTooSmall", CompressText.Limit(limit ?? 0));
            return;
        }

        PreviewProblem = null;
        PreviewWidth = result.Width;
        PreviewData = result.Data;
        var length = result.Data.LongLength;
        ResultSizeValue = DisplayFormat.FormatFileSize(length);
        IsLarger = length >= item.File.Size;
        var change = (int)Math.Round(Math.Abs(1 - ((double)length / Math.Max(1, item.File.Size))) * 100);
        ResultSizeCaption = IsLarger ? Loc.Format("Compress.Larger", change) : Loc.Format("Compress.Saved", change);
        ResultResolutionValue = ScaleText.Dimensions(result.Width, result.Height);
        ResultResolutionCaption = $"{ScaleText.Megapixels((long)result.Width * result.Height, Loc.Culture)} · {Percent(result.Setting.ResolutionPercent)}";
        ResultQualityValue = HasQuality ? Percent(result.Setting.Quality) : Loc.Get("Compress.Lossless");
        ResultQualityCaption = limit is null ? TargetCode : Loc.Format("Compress.FoundFor", TargetCode);
        PreviewSpoken = Loc.Format("Compress.PreviewSpoken", ResultResolutionValue, ResultSizeValue);
    }

    private void ClearResult()
    {
        PreviewData = null;
        PreviewSpoken = string.Empty;
        PreviewProblem = null;
        ResultSizeValue = "—";
        ResultSizeCaption = string.Empty;
        IsLarger = false;
        ResultResolutionValue = "—";
        ResultResolutionCaption = string.Empty;
        ResultQualityValue = "—";
        ResultQualityCaption = string.Empty;
    }

    // ---- Saving ----

    /// <summary>"Kompres dan simpan…", or "Kompres dan simpan N gambar…" for several.</summary>
    public string StartLabel => Items.Count > 1 ? Loc.Format("Compress.StartMany", Items.Count) : Loc.Get("Compress.Start");

    private bool CanStart() =>
        Items.Count > 0
        && !Run.IsActive
        && (!IsTargetMode || IsTargetValid)

        // One picture that can't be made small enough can't be saved; in a list, that one fails on its own row.
        && !(Items.Count == 1 && HasPreviewProblem);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var extension = TargetExtension;
        var options = new CompressOptions((int)Quality, (int)ResolutionPercent, IsTargetMode ? TargetBytes : null);
        var format = $"{string.Join(", ", Items.Select(i => FileExtension.ToCode(i.File.Extension)).Distinct())} → {TargetCode}";
        var suffix = Loc.Get("Compress.NameSuffix");
        InputMessage = null;
        _log.Info($"Compress started: {format}, {Items.Count} pictures, {(IsTargetMode ? "size limit" : "quality and resolution")}");

        if (Items.Count == 1)
        {
            var only = Items[0];
            var destination = await _desktop.PickDestinationAsync(
                Path.GetFileNameWithoutExtension(only.File.Path) + suffix,
                Path.GetDirectoryName(only.File.Path),
                TargetCode,
                extension);
            if (destination is null)
            {
                return;
            }

            // A name typed with another extension still gets the real one, so the file is never mislabeled.
            if (FileExtension.FromPath(destination) != extension)
            {
                destination += extension;
            }

            await Run.RunAsync(_pipeline, new ConversionJob(only.File.Path, extension, destination, options), format, TargetCode);
            return;
        }

        var folder = await _desktop.PickFolderAsync(Path.GetDirectoryName(Items[0].File.Path));
        if (folder is null)
        {
            return;
        }

        IReadOnlyList<string> names;
        try
        {
            names = OutputNames.PlanNamed(
                [.. Items.Select(i => (i.File.Path, Path.GetFileNameWithoutExtension(i.File.Path) + suffix + extension))],
                folder,
                path => File.Exists(path) || Directory.Exists(path));
        }
        catch (IOException)
        {
            ShowInputMessage(Loc.Get("Error.Io"), InfoBarSeverity.Warning);
            return;
        }

        var entries = Items
            .Select((item, i) => new BatchEntry(new BatchJob(new ConversionJob(item.File.Path, extension, names[i], options), _pipeline), item.Name))
            .ToList();
        await Run.RunBatchAsync(entries, folder, format, TargetCode);
    }

    // ---- Starting over ----

    /// <summary>"Konversi file lain": an empty page; the choices stay until the app closes.</summary>
    private void Clear()
    {
        InputMessage = null;
        Items.Clear();
        Renumber(0);
    }
}

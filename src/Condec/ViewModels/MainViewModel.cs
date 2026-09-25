using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.History;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using Condec.Services;
using Microsoft.UI.Xaml.Controls;

namespace Condec.ViewModels;

public enum ConverterState
{
    Input,
    Processing,
    Done,
    Failed,
}

/// <summary>The converter card (input, processing, done, failed) and the history card.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConverterRegistry _registry;
    private readonly ConversionPipeline _pipeline;
    private readonly HistoryStore _history;
    private readonly IDesktopServices _desktop;
    private CancellationTokenSource? _cancellation;
    private ConversionJob? _lastJob;
    private PipelineStage _lastStage;
    private int _chunkCount;
    private bool _loadingHistory;

    public MainViewModel(ConverterRegistry registry, ConversionPipeline pipeline, HistoryStore history, IDesktopServices desktop)
    {
        _registry = registry;
        _pipeline = pipeline;
        _history = history;
        _desktop = desktop;
        IsHistoryEnabled = true;
        Pdf.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PdfOptionsViewModel.PageKind) or nameof(PdfOptionsViewModel.HasValidScale))
            {
                OnPropertyChanged(nameof(ConvertLabel));
                ConvertCommand.NotifyCanExecuteChanged();
            }
        };
    }

    // ---- State ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInput), nameof(IsProcessing), nameof(IsDone), nameof(IsFailed))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial ConverterState State { get; set; }

    public bool IsInput => State == ConverterState.Input;

    public bool IsProcessing => State == ConverterState.Processing;

    public bool IsDone => State == ConverterState.Done;

    public bool IsFailed => State == ConverterState.Failed;

    // ---- Input ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource), nameof(HasNoSource), nameof(FormatPlaceholder), nameof(FormatHelpText), nameof(ProcessingTitle), nameof(ShowsPageChoice), nameof(ShowsCadOptions), nameof(ConvertLabel))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial SourceFile? Source { get; set; }

    public bool HasSource => Source is not null;

    public bool HasNoSource => Source is null;

    public ObservableCollection<FormatOption> TargetOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConversionFormat), nameof(ShowsPageChoice), nameof(ShowsCadOptions), nameof(ConvertLabel))]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    public partial FormatOption? SelectedTarget { get; set; }

    /// <summary>Page, unit, scale and switches for a PDF source.</summary>
    public PdfOptionsViewModel Pdf { get; } = new();

    private bool IsPdfSource => Source?.Extension == ".pdf";

    /// <summary>A PDF going to a one-page target (image or CAD) needs a page.</summary>
    public bool ShowsPageChoice => IsPdfSource && SelectedTarget?.Extension is ".png" or ".jpg" or ".heic" or ".dxf" or ".dwg";

    public bool ShowsCadOptions => IsPdfSource && SelectedTarget?.Extension is ".dxf" or ".dwg";

    public string ConvertLabel => ShowsCadOptions && Pdf.IsScan ? "Tetap konversi…" : "Konversi dan simpan…";

    public string FormatPlaceholder => HasSource ? "Pilih format" : "Pilih file dulu";

    public string FormatHelpText => Source is null
        ? "Format muncul setelah file dipilih"
        : $"{TargetOptions.Count(o => o.IsEnabled)} format tersedia untuk {Source.Extension}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInputMessage))]
    public partial string? InputMessage { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity InputMessageSeverity { get; set; }

    public bool HasInputMessage => InputMessage is not null;

    // ---- Processing ----

    public string ProcessingTitle => $"Mengonversi {Source?.Name}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProcessingCaption))]
    public partial string? DestinationPath { get; set; }

    public string ProcessingCaption => $"Ke {SelectedTarget?.DisplayName} · disimpan sebagai {DestinationPath}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    public partial double ProgressPercent { get; set; }

    public string ProgressText => $"{ProgressPercent:0}%";

    public ObservableCollection<ConversionStepViewModel> Steps { get; } = [];

    // ---- Done and failed ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultIntegrity))]
    public partial ConversionResult? Result { get; set; }

    /// <summary>"PNG → JPG", like the design's "DOCX → PDF".</summary>
    public string ConversionFormat => Source is null || SelectedTarget is null
        ? string.Empty
        : $"{FileExtension.ToCode(Source.Extension)} → {SelectedTarget.DisplayName}";

    public string ResultIntegrity => $"SHA-256 cocok · {Result?.ChunkCount} chunk terverifikasi";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultMessage))]
    public partial string? ResultMessage { get; set; }

    public bool HasResultMessage => ResultMessage is not null;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // ---- History ----

    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];

    public bool HasHistory => HistoryItems.Count > 0;

    public bool HasNoHistory => HistoryItems.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HistoryEmptyText))]
    public partial bool IsHistoryEnabled { get; set; }

    public string HistoryEmptyText => IsHistoryEnabled
        ? "Belum ada riwayat konversi."
        : "Riwayat sedang tidak dicatat. Konversi berikutnya tidak akan muncul di sini.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHistoryMessage))]
    public partial string? HistoryMessage { get; set; }

    public bool HasHistoryMessage => HistoryMessage is not null;

    public async Task InitializeAsync()
    {
        try
        {
            await _history.LoadAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryMessage = "Riwayat tidak bisa dibaca. Konversi tetap bisa dilakukan.";
        }

        _loadingHistory = true;
        IsHistoryEnabled = _history.IsEnabled;
        _loadingHistory = false;
        RefreshHistory();
    }

    // ---- Choosing a file ----

    [RelayCommand]
    private async Task PickSourceAsync()
    {
        var path = await _desktop.PickSourceFileAsync(_registry.GetSourceExtensions());
        if (path is not null)
        {
            SelectSource(path);
        }
    }

    /// <summary>Drag and drop: exactly one file is accepted.</summary>
    /// <param name="filePaths">An empty path is a file with no place on disk, such as one inside a ZIP.</param>
    public void SelectDropped(IReadOnlyList<string> filePaths, int folderCount)
    {
        if (folderCount > 0 && filePaths.Count == 0)
        {
            ShowInputMessage("Seret file, bukan folder.", InfoBarSeverity.Warning);
        }
        else if (filePaths.Count + folderCount > 1)
        {
            ShowInputMessage("Seret satu file saja. Condec mengonversi file satu per satu.", InfoBarSeverity.Warning);
        }
        else if (filePaths.Count == 1 && filePaths[0].Length == 0)
        {
            ReportUnreadableDrop();
        }
        else if (filePaths.Count == 1)
        {
            SelectSource(filePaths[0]);
        }
    }

    public void ReportUnreadableDrop() =>
        ShowInputMessage("File yang diseret tidak bisa dibaca dari tempatnya. Salin dulu ke folder di komputer ini, lalu coba lagi.", InfoBarSeverity.Warning);

    public void SelectSource(string path)
    {
        if (State != ConverterState.Input)
        {
            return;
        }

        var extension = FileExtension.FromPath(path);
        var options = extension.Length == 0 ? [] : _registry.GetTargetOptions(extension);
        if (options.Count == 0)
        {
            ShowInputMessage(DescribeUnsupported(extension), InfoBarSeverity.Warning);
            return;
        }

        long size;
        try
        {
            size = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowInputMessage("File ini tidak bisa dibuka. Periksa apakah file masih ada dan bisa dibaca.", InfoBarSeverity.Warning);
            return;
        }

        int? pageCount = null;
        if (extension == ".pdf")
        {
            try
            {
                pageCount = PdfInspector.CountPages(path);
            }
            catch (LockedPdfException)
            {
                ShowInputMessage(ErrorMessages.LockedPdf, InfoBarSeverity.Warning);
                return;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // PdfPig throws its own exception types for damaged files.
                ShowInputMessage("PDF ini tidak bisa dibaca. File mungkin rusak atau belum selesai diunduh.", InfoBarSeverity.Warning);
                return;
            }

            Pdf.Load(path, pageCount.Value);
        }

        // Keep the chosen format when the new file can be converted to it too.
        var previous = SelectedTarget?.Extension;
        TargetOptions.Clear();
        foreach (var option in options)
        {
            TargetOptions.Add(new FormatOption(option));
        }

        InputMessage = null;
        Source = new SourceFile(path, extension, size, pageCount);
        SelectedTarget = TargetOptions.FirstOrDefault(o => o.Extension == previous && o.Option.IsEnabled);

        if (options.FirstOrDefault(o => !o.IsEnabled)?.DisabledReason is { } reason)
        {
            ShowInputMessage(reason, InfoBarSeverity.Informational);
        }
    }

    [RelayCommand]
    private void DismissInputMessage() => InputMessage = null;

    private string DescribeUnsupported(string extension)
    {
        var supported = string.Join(", ", _registry.GetSourceExtensions().Select(FormatCatalog.GetTargetLabel).Distinct());
        var file = extension.Length == 0 ? "File tanpa ekstensi" : $"File {extension}";
        return $"{file} belum bisa dikonversi. Format yang bisa dipilih: {supported}.";
    }

    private void ShowInputMessage(string message, InfoBarSeverity severity)
    {
        InputMessageSeverity = severity;
        InputMessage = message;
    }

    // ---- Converting ----

    private bool CanConvert() =>
        State == ConverterState.Input
        && Source is not null
        && SelectedTarget is { Option.IsEnabled: true }
        && !(ShowsCadOptions && (Pdf.PageKind is null || Pdf.IsEmpty || !Pdf.HasValidScale));

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        if (Source is null || SelectedTarget is null)
        {
            return;
        }

        var destination = await _desktop.PickDestinationAsync(
            Path.GetFileNameWithoutExtension(Source.Path),
            Path.GetDirectoryName(Source.Path),
            SelectedTarget.DisplayName,
            SelectedTarget.Extension);
        if (destination is null)
        {
            return;
        }

        // A name typed with another extension still gets the real one, so the file is never mislabeled.
        if (FileExtension.FromPath(destination) != SelectedTarget.Extension)
        {
            destination += SelectedTarget.Extension;
        }

        var options = IsPdfSource ? Pdf.BuildOptions(SelectedTarget.Extension) : null;
        await RunAsync(new ConversionJob(Source.Path, SelectedTarget.Extension, destination, options));
    }

    [RelayCommand]
    private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_lastJob is not null)
        {
            await RunAsync(_lastJob);
        }
    }

    [RelayCommand]
    private void BackToInput() => State = ConverterState.Input;

    [RelayCommand]
    private void ConvertAnother()
    {
        TargetOptions.Clear();
        SelectedTarget = null;
        Source = null;
        Result = null;
        ResultMessage = null;
        InputMessage = null;
        State = ConverterState.Input;
    }

    private async Task RunAsync(ConversionJob job)
    {
        _lastJob = job;
        _lastStage = PipelineStage.Decode;
        _chunkCount = 0;
        DestinationPath = job.DestinationPath;
        InputMessage = null;
        ResultMessage = null;
        ProgressPercent = 0;
        ResetSteps();
        State = ConverterState.Processing;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        // Created here, on the UI thread, so progress reports come back to it.
        var progress = new Progress<PipelineProgress>(OnProgress);
        try
        {
            // Hashing and verification run on a worker thread; the window stays responsive.
            var result = await Task.Run(() => _pipeline.RunAsync(job, progress, cancellation.Token), CancellationToken.None);
            Result = result;
            State = ConverterState.Done;
            await RecordHistoryAsync(job, result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            State = ConverterState.Input;
            ShowInputMessage("Konversi dibatalkan. Tidak ada file yang disimpan.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ErrorMessage = ErrorMessages.Describe(ex, _lastStage, SelectedTarget?.DisplayName ?? job.TargetExtension);
            State = ConverterState.Failed;
        }
        finally
        {
            _cancellation = null;
        }
    }

    private void ResetSteps()
    {
        Steps.Clear();
        Steps.Add(new ConversionStepViewModel("Mendekode file sumber"));
        Steps.Add(new ConversionStepViewModel($"Menulis ke format {SelectedTarget?.DisplayName}"));
        Steps.Add(new ConversionStepViewModel("Verifikasi chunk"));
        Steps.Add(new ConversionStepViewModel("Cek integritas file"));
    }

    private void OnProgress(PipelineProgress progress)
    {
        if (State != ConverterState.Processing)
        {
            return;
        }

        _lastStage = progress.Stage;
        if (progress.ChunkCount > 0)
        {
            _chunkCount = progress.ChunkCount;
        }

        // Rounded down, so 100% only shows once the file is really saved.
        ProgressPercent = Math.Floor(progress.OverallFraction * 100);

        var current = (int)progress.Stage;
        var allDone = progress.Stage == PipelineStage.VerifyIntegrity && progress.StageFraction >= 1;
        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (i < current || (i == current && allDone))
            {
                step.State = StepState.Done;
                step.Detail = DoneDetail((PipelineStage)i);
            }
            else if (i == current)
            {
                step.State = StepState.Active;
                step.Detail = ActiveDetail(progress);
            }
            else
            {
                step.State = StepState.Waiting;
                step.Detail = "Menunggu";
            }
        }
    }

    private string DoneDetail(PipelineStage stage) => stage switch
    {
        PipelineStage.VerifyChunks => $"{_chunkCount} chunk cocok",
        PipelineStage.VerifyIntegrity => "Hash cocok",
        _ => "Selesai",
    };

    private string ActiveDetail(PipelineProgress progress) => progress.Stage switch
    {
        PipelineStage.Decode => progress.Detail ?? $"Membaca {Source?.Name}",
        PipelineStage.Encode => progress.Detail ?? $"Encode ke {SelectedTarget?.Extension}",
        PipelineStage.VerifyChunks when progress.ChunkNumber > 0 => $"Chunk {progress.ChunkNumber} dari {progress.ChunkCount}",
        PipelineStage.VerifyChunks => "Membaca ulang dari disk",
        _ => progress.StageFraction < 0.5 ? "Menghitung SHA-256" : "Membuka ulang hasil",
    };

    // ---- Done actions ----

    [RelayCommand]
    private void OpenResult()
    {
        if (Result is not null)
        {
            RunShellAction(() => _desktop.OpenFile(Result.OutputPath), "File tersimpan, tetapi tidak ada aplikasi yang bisa membukanya.");
        }
    }

    [RelayCommand]
    private void ShowResultInFolder()
    {
        if (Result is not null)
        {
            RunShellAction(() => _desktop.ShowInFolder(Result.OutputPath), "File Explorer tidak bisa dibuka.");
        }
    }

    private void RunShellAction(Action action, string failureMessage)
    {
        try
        {
            ResultMessage = null;
            action();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ResultMessage = failureMessage;
        }
    }

    // ---- History ----

    partial void OnIsHistoryEnabledChanged(bool value)
    {
        if (!_loadingHistory)
        {
            _ = SaveHistoryEnabledAsync(value);
        }
    }

    private async Task SaveHistoryEnabledAsync(bool enabled)
    {
        try
        {
            await _history.SetEnabledAsync(enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryMessage = "Pengaturan riwayat tidak bisa disimpan.";
        }
    }

    private async Task RecordHistoryAsync(ConversionJob job, ConversionResult result)
    {
        try
        {
            await _history.AddAsync(new HistoryEntry(
                Path.GetFileName(job.SourcePath),
                FileExtension.FromPath(job.SourcePath),
                FileExtension.Normalize(job.TargetExtension),
                DateTimeOffset.Now,
                result.OutputPath,
                VerificationStatus.Verified));
            RefreshHistory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryMessage = "Konversi berhasil, tetapi riwayatnya tidak bisa disimpan.";
        }
    }

    [RelayCommand(CanExecute = nameof(HasHistory))]
    private async Task ClearHistoryAsync()
    {
        try
        {
            await _history.ClearAsync();
            HistoryMessage = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HistoryMessage = "Riwayat tidak bisa dihapus.";
        }

        RefreshHistory();
    }

    [RelayCommand]
    private void DismissHistoryMessage() => HistoryMessage = null;

    private void ShowHistoryItemInFolder(HistoryItemViewModel item)
    {
        var path = item.Entry.OutputPath;
        var folder = Path.GetDirectoryName(path);
        try
        {
            if (File.Exists(path))
            {
                HistoryMessage = null;
                _desktop.ShowInFolder(path);
            }
            else if (folder is not null && Directory.Exists(folder))
            {
                HistoryMessage = $"{Path.GetFileName(path)} sudah tidak ada di lokasi semula. Folder tujuannya yang dibuka.";
                _desktop.OpenFolder(folder);
            }
            else
            {
                HistoryMessage = $"{Path.GetFileName(path)} dan foldernya sudah tidak ada.";
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            HistoryMessage = "File Explorer tidak bisa dibuka.";
        }
    }

    private void RefreshHistory()
    {
        var now = DateTime.Now;
        HistoryItems.Clear();
        foreach (var entry in _history.Entries)
        {
            HistoryItems.Add(new HistoryItemViewModel(entry, now, ShowHistoryItemInFolder));
        }

        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasNoHistory));
        ClearHistoryCommand.NotifyCanExecuteChanged();
    }
}

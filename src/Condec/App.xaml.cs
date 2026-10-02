// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Documents;
using Condec.Core.History;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Media;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using Condec.Core.Platform.Windows.Devices;
using Condec.Core.Settings;
using Condec.Core.Upscale;
using Condec.Services;
using Microsoft.UI.Xaml;

namespace Condec;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    public static new App Current => (App)Application.Current;

    /// <summary>Set in <see cref="OnLaunched"/> before the first page is created.</summary>
    public AppServices Services { get; private set; } = null!;

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Before anything is created: the window and view models read their texts when they are built.
        Loc.Culture = Languages.Pick(UserLanguages.Get());

        // DXCore and the registry answer in well under a second; the limits in Settings need them from the start.
        var device = await Task.Run(() => new WindowsDeviceProbe().Detect());
        var packaged = PackageIdentity.IsPackaged();
        var settings = new AppSettings(PackageIdentity.CreateSettingsStore(CondecPaths.SettingsFile), device);
        var log = new ActivityLog(CondecPaths.LogDirectory, () => settings.LogEnabled, () => settings.LogLevel);
        var version = AppVersion.Text;
        log.Info($"Condec {version} started ({(packaged ? "MSIX" : "portable")}, language {Loc.Culture.Name})");
        log.Debug($"Device: {device.CpuName}; RAM {device.InstalledRamGb} GB; GPU {device.Gpu?.Name ?? "none"} ({device.GpuMemoryGb} GB, integrated {device.Gpu?.IsIntegrated}); NPU {device.NpuName ?? "none"}");

        var registry = new ConverterRegistry(
            [
                new ImageConverter(),
                new MediaConverter(),
                new ImageToCadConverter(new WicImageRasterizer()),
                new PdfToImageConverter(),
                new PdfToCadConverter(new PdfPageRenderer()),
                new CadFileConverter(),
                new LibreOfficeConverter(CondecPaths.LibreOfficeProfileDirectory),
            ],
            [new ImageOutputValidator(), new MediaOutputValidator(), new PdfOutputValidator(), new OfficeDocumentValidator(), new CadOutputValidator()]);
        var journal = new TempFileJournal(CondecPaths.JournalDirectory);
        var pipeline = new ConversionPipeline(registry, journal);

        // Upscale has its own registry: its converter takes the same picture formats as Convert File's, so the two can't share one.
        var upscaleRegistry = new ConverterRegistry([new UpscaleConverter(device)], [new ImageOutputValidator()]);
        var upscalePipeline = new ConversionPipeline(upscaleRegistry, journal);
        var history = new HistoryStore(CondecPaths.HistoryFile);

        // Leftovers from a conversion that was cut off by a crash or power loss. Best effort: the
        // journal keeps any entry it can't clean up and tries again on the next start.
        _ = Task.Run(journal.CleanupStale);

        _window = new MainWindow();
        Services = new AppServices(settings, log, registry, pipeline, history, new DesktopServices(_window.AppWindow.Id), upscaleRegistry, upscalePipeline, version);
        _window.Start(Services);
        _window.Activate();
        _ = Services.Upscale.InitializeAsync();
        await Services.Convert.InitializeAsync();
    }
}

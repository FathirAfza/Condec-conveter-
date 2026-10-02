// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Documents;
using Condec.Core.History;
using Condec.Core.Imaging;
using Condec.Core.Localization;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using Condec.Services;
using Condec.ViewModels;
using Microsoft.UI.Xaml;

namespace Condec;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Before anything is created: the window and view models read their texts when they are built.
        Loc.Culture = Languages.Pick(UserLanguages.Get());

        var registry = new ConverterRegistry(
            [
                new ImageConverter(),
                new ImageToCadConverter(new WicImageRasterizer()),
                new PdfToImageConverter(),
                new PdfToCadConverter(new PdfPageRenderer()),
                new CadFileConverter(),
                new LibreOfficeConverter(CondecPaths.LibreOfficeProfileDirectory),
            ],
            [new ImageOutputValidator(), new PdfOutputValidator(), new OfficeDocumentValidator(), new CadOutputValidator()]);
        var journal = new TempFileJournal(CondecPaths.JournalDirectory);
        var pipeline = new ConversionPipeline(registry, journal);
        var history = new HistoryStore(CondecPaths.HistoryFile);

        // Leftovers from a conversion that was cut off by a crash or power loss. Best effort: the
        // journal keeps any entry it can't clean up and tries again on the next start.
        _ = Task.Run(journal.CleanupStale);

        _window = new MainWindow(windowId => new MainViewModel(registry, pipeline, history, new DesktopServices(windowId)));
        _window.Activate();
        await _window.ViewModel.InitializeAsync();
    }
}

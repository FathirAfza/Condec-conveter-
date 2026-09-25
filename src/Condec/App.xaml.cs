using Condec.Core;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Documents;
using Condec.Core.History;
using Condec.Core.Imaging;
using Condec.Core.Pdf;
using Condec.Core.Pipeline;
using Condec.Services;
using Condec.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Storage;

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
        var registry = new ConverterRegistry(
            [
                new ImageConverter(),
                new PdfToImageConverter(),
                new PdfToCadConverter(new PdfPageRenderer()),
                new CadFileConverter(),
                new LibreOfficeConverter(Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "libreoffice-profile")),
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

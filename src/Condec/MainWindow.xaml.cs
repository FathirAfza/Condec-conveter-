using System.ComponentModel;
using System.Runtime.InteropServices;
using Condec.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;

namespace Condec;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1120;
    private const int DefaultHeight = 780;
    private const int MinimumWidth = 760;
    private const int MinimumHeight = 680;
    private const double ContentMaxWidth = 880;

    public MainWindow(Func<WindowId, MainViewModel> createViewModel)
    {
        InitializeComponent();
        ViewModel = createViewModel(AppWindow.Id);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        AppWindow.SetIcon("Assets/Condec.ico");

        ResizeToDefault();
        Activated += OnActivated;

        PageScroller.SizeChanged += (_, _) => UpdatePageSize();
        HeaderPanel.SizeChanged += (_, _) => UpdatePageSize();
        ConverterCard.SizeChanged += (_, _) => UpdatePageSize();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// For x:Bind, which has no "and" of two properties. Returns Visibility itself: the XAML compiler
    /// generates code that doesn't build when a bool function result is cast to Visibility.
    /// </summary>
    public Visibility VisibleWhenBoth(bool a, bool b) => a && b ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The page list sits under "Halaman" in the third column next to unit and scale, or alone in the first.</summary>
    public int PageColumn(bool showsCadOptions) => showsCadOptions ? 2 : 0;

    /// <summary>Label style for a row in the progress list, used by x:Bind.</summary>
    public static Style StepLabelStyle(StepState state) => (Style)Application.Current.Resources[state switch
    {
        StepState.Done => "StepDoneTextStyle",
        StepState.Active => "StepActiveTextStyle",
        _ => "StepWaitingTextStyle",
    }];

    // AppWindow.Resize takes physical pixels, so scale the 1120×780 design size by the window DPI.
    private void ResizeToDefault()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(DefaultWidth * scale), (int)(DefaultHeight * scale)));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
        }
    }

    // The history card normally fills the rest of the window. When there is no room for it (a small
    // window or large text), the page keeps the card at its minimum height and scrolls instead.
    // The width is set here too: with only MaxWidth, WinUI centers the content by its desired width
    // rather than the width it is drawn at, which pushed the 880 wide column off-center.
    private void UpdatePageSize()
    {
        var margin = PageContent.Margin;
        PageContent.Width = Math.Min(ContentMaxWidth, Math.Max(0, PageScroller.ActualWidth - margin.Left - margin.Right));

        var available = PageScroller.ActualHeight - margin.Top - margin.Bottom;
        var needed = HeaderPanel.ActualHeight + ConverterCard.ActualHeight + (PageContent.RowSpacing * 2) + HistoryCard.MinHeight;
        PageContent.Height = Math.Max(available, needed);
    }

    // Keep keyboard focus on the next useful control when a view is swapped out under it.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.State)
            || (e.PropertyName is nameof(MainViewModel.Source) && ViewModel is { IsInput: true, HasSource: true }))
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => FocusTarget()?.Focus(FocusState.Programmatic));
        }
    }

    private Control? FocusTarget() => ViewModel.State switch
    {
        ConverterState.Processing => CancelButton,
        ConverterState.Done => OpenResultButton,
        ConverterState.Failed => RetryButton,
        _ when !ViewModel.HasSource => PickFileButton,
        _ when ConvertButton.IsEnabled => ConvertButton,
        _ => FormatComboBox,
    };

    // A format that needs a missing program stays in the list, captioned "Tidak tersedia", but can't be chosen.
    // Disabling its ComboBoxItem instead closes the open list when that item has keyboard focus.
    private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FormatComboBox.SelectedItem is FormatOption { IsEnabled: false })
        {
            FormatComboBox.SelectedItem = e.RemovedItems.OfType<FormatOption>().FirstOrDefault();
        }
    }

    private void OnSourceDragOver(object sender, DragEventArgs e)
    {
        if (ViewModel.IsInput && e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Pilih file ini";
        }
    }

    private async void OnSourceDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            ViewModel.SelectDropped(
                [.. items.OfType<IStorageFile>().Select(file => file.Path ?? string.Empty)],
                items.OfType<IStorageFolder>().Count());
        }
        catch (Exception)
        {
            // An async void handler must not let anything escape; the source app may have gone away.
            ViewModel.ReportUnreadableDrop();
        }
        finally
        {
            deferral.Complete();
        }
    }

    // HyperlinkButton has no Flyout property of its own, so the confirmation is an attached flyout.
    private void OnClearHistoryClicked(object sender, RoutedEventArgs e) => FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);

    private void OnClearHistoryConfirmed(object sender, RoutedEventArgs e) => ClearHistoryFlyout.Hide();

    // Dim the title when the window is inactive, like the system title bar does.
    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated
            && Application.Current.Resources.TryGetValue("TextFillColorTertiaryBrush", out var brush))
        {
            AppTitleText.Foreground = (Brush)brush;
        }
        else
        {
            AppTitleText.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

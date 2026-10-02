// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices;
using Condec.Core.Localization;
using Condec.Core.Settings;
using Condec.Services;
using Condec.Views;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Condec;

public sealed partial class MainWindow : Window
{
    // DESIGN §3.1: 1280 × 820 at first. The minimum is the proposal in §13 #6 (open).
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 820;
    private const int MinimumWidth = 900;
    private const int MinimumHeight = 640;

    private AppServices? _services;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        AppWindow.SetIcon("Assets/Condec.ico");

        ResizeToDefault();
    }

    /// <summary>Applies the appearance settings and opens Convert File.</summary>
    public void Start(AppServices services)
    {
        _services = services;
        services.Settings.Changed += (_, _) => ApplyAppearance();
        services.Convert.ArchitectureRequested += (_, _) => Navigation.SelectedItem = ArchitectureItem;
        services.Upscale.SettingsRequested += (_, _) => Navigation.SelectedItem = Navigation.SettingsItem;
        ApplyAppearance();
        Navigation.SelectedItem = ConvertItem;
    }

    /// <summary>Theme on the root element (DESIGN §6.4), the caption buttons to match, and Mica or a solid background.</summary>
    private void ApplyAppearance()
    {
        var settings = _services!.Settings;
        Root.RequestedTheme = settings.Theme switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = settings.Theme switch
        {
            ThemePreference.Light => TitleBarTheme.Light,
            ThemePreference.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };

        if (settings.MicaEnabled && SystemBackdrop is not MicaBackdrop)
        {
            SystemBackdrop = new MicaBackdrop();
        }
        else if (!settings.MicaEnabled && SystemBackdrop is not null)
        {
            SystemBackdrop = null;
        }

        SolidBackground.Visibility = settings.MicaEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    // The Settings item is made by NavigationView, which names it in the Windows language; DESIGN §2 keeps "Settings".
    private void OnNavigationLoaded(object sender, RoutedEventArgs e)
    {
        if (Navigation.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Content = Loc.Get("Nav.Settings");
        }

        // DESIGN §10: the hamburger button is named. Its template part is "TogglePaneButton"; NavigationView names it
        // "Open/Close Navigation" itself, in the system language.
        if (FindByName(Navigation, "TogglePaneButton") is Button toggle)
        {
            AutomationProperties.SetName(toggle, Loc.Get("Nav.TogglePane"));
        }
    }

    private static DependencyObject? FindByName(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var childName } && childName == name)
            {
                return child;
            }

            if (FindByName(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected ? typeof(SettingsPage)
            : ReferenceEquals(args.SelectedItem, UpscaleItem) ? typeof(UpscalePage)
            : ReferenceEquals(args.SelectedItem, ArchitectureItem) ? typeof(ArchitecturePage)
            : typeof(ConvertPage);
        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page, null, args.RecommendedNavigationTransitionInfo);
        }
    }

    // AppWindow.Resize takes physical pixels, so scale the design size by the window DPI. A screen smaller than the
    // design size gets a window that fits its work area.
    private void ResizeToDefault()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double scale = GetDpiForWindow(hwnd) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(DefaultWidth * scale), work.Width);
        var height = Math.Min((int)(DefaultHeight * scale), work.Height);
        AppWindow.Resize(new SizeInt32(width, height));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)(MinimumWidth * scale), work.Width);
            presenter.PreferredMinimumHeight = Math.Min((int)(MinimumHeight * scale), work.Height);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

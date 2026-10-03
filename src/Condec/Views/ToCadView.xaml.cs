// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Collections.Specialized;
using System.ComponentModel;
using Condec.Core.Architecture;
using Condec.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Condec.Views;

/// <summary>"Gambar, PDF, DXF → DWG" (DESIGN §6.3.1).</summary>
public sealed partial class ToCadView : UserControl
{
    private readonly MultiSelectSync<ObjectRow> _objects;
    private (bool Empty, bool Reading, bool Review, bool Failed, bool Upscaling) _section;

    public ToCadView()
    {
        ViewModel = App.Current.Services.ToCad;
        InitializeComponent();
        RunView.Run = ViewModel.Run;

        _objects = new MultiSelectSync<ObjectRow>(ObjectList, ViewModel.Rows, row => row.IsChecked, (row, on) => row.IsChecked = on);
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.UpscaleChoices.CollectionChanged += OnUpscaleChoicesChanged;
        Loaded += (_, _) =>
        {
            ShowPreview();
            BuildUpscaleMenu();
        };
    }

    public ToCadViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ToCadViewModel.Preview):
                ShowPreview();
                break;
            case nameof(ToCadViewModel.Overlay):
                ShowOverlay();
                break;
            case nameof(ToCadViewModel.ShowsEmpty) or nameof(ToCadViewModel.ShowsReading) or nameof(ToCadViewModel.ShowsReview)
                or nameof(ToCadViewModel.ShowsFailedItem) or nameof(ToCadViewModel.Banner):
                MoveFocus();
                break;
        }
    }

    // ---- The preview and the boxes on it ----

    private void ShowPreview()
    {
        if (ViewModel.Preview is not { } preview)
        {
            PreviewImage.Source = null;
            ClearOverlay();
            return;
        }

        PreviewCanvas.Width = PreviewImage.Width = preview.Width;
        PreviewCanvas.Height = PreviewImage.Height = preview.Height;
        PreviewImage.Source = PreviewBitmap.Create(preview);
        ShowOverlay();
    }

    private void ClearOverlay()
    {
        for (var i = PreviewCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(PreviewCanvas.Children[i], PreviewImage))
            {
                PreviewCanvas.Children.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Draws a box for each thing found. The boxes lie in the pixels of the picture, which the preview scales to fit, so their
    /// lines are as thick as they must be to look the same on a small picture and on a large one.
    /// </summary>
    private void ShowOverlay()
    {
        ClearOverlay();
        if (ViewModel.Preview is not { } preview)
        {
            return;
        }

        var (width, height) = ViewModel.OverlaySize;
        if (width != preview.Width || height != preview.Height)
        {
            return;
        }

        var thickness = Math.Max(1.5, width / 500.0);
        var dashes = new DoubleCollection { 4, 3 };
        foreach (var box in ViewModel.Overlay)
        {
            var bounds = box.Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                continue;
            }

            var rectangle = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                StrokeThickness = thickness,
                IsHitTestVisible = false,
                Style = (Style)Resources[box.Style switch
                {
                    OverlayStyle.Included => "OverlayIncludedStyle",
                    OverlayStyle.Excluded => "OverlayExcludedStyle",
                    _ => "OverlayUnclearStyle",
                }],
            };
            if (box.Style == OverlayStyle.Excluded)
            {
                rectangle.StrokeDashArray = dashes;
            }

            Canvas.SetLeft(rectangle, bounds.Left);
            Canvas.SetTop(rectangle, bounds.Top);
            PreviewCanvas.Children.Add(rectangle);

            // Not by color alone: an unclear place carries a mark.
            if (box.Style == OverlayStyle.Unclear)
            {
                var mark = new TextBlock
                {
                    Text = "!",
                    FontSize = Math.Min(bounds.Height, Math.Max(thickness * 8, 10)),
                    Style = (Style)Resources["OverlayMarkStyle"],
                };
                Canvas.SetLeft(mark, bounds.Left + (thickness * 2));
                Canvas.SetTop(mark, bounds.Top);
                PreviewCanvas.Children.Add(mark);
            }
        }
    }

    // ---- The upscale menu ----

    private void OnUpscaleChoicesChanged(object? sender, NotifyCollectionChangedEventArgs e) => BuildUpscaleMenu();

    /// <summary>A scale beyond the limit stays in the menu, locked and captioned; it can't be chosen.</summary>
    private void BuildUpscaleMenu()
    {
        UpscaleMenu.Items.Clear();
        foreach (var choice in ViewModel.UpscaleChoices)
        {
            var item = new MenuFlyoutItem { Text = choice.Text, IsEnabled = choice.IsAllowed };
            var scale = choice.Scale;
            item.Click += async (_, _) => await ViewModel.StartUpscaleAsync(scale);
            UpscaleMenu.Items.Add(item);
        }
    }

    // ---- Calibration ----

    private async void OnCalibrateClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Preview is not { } preview)
        {
            return;
        }

        var dialog = new CalibrationDialog(PreviewBitmap.Create(preview), preview.Width, preview.Height)
        {
            XamlRoot = XamlRoot,
            RequestedTheme = (XamlRoot.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ApplyCalibration(dialog.X1, dialog.Y1, dialog.X2, dialog.Y2, dialog.LengthMillimeters);
        }
    }

    // ---- Files dropped on the drop area ----

    private void OnDragOver(object sender, DragEventArgs e) => DropFiles.OnDragOver(e, ViewModel.ShowsEmpty);

    private async void OnDrop(object sender, DragEventArgs e)
    {
        (List<string> Files, int Folders)? dropped;
        var deferral = e.GetDeferral();
        try
        {
            dropped = await DropFiles.ReadAsync(e);
        }
        catch (Exception)
        {
            // An async void handler must not let anything escape; the source app may have gone away.
            ViewModel.ReportUnreadableDrop();
            return;
        }
        finally
        {
            deferral.Complete();
        }

        if (dropped is { } items)
        {
            await ViewModel.SelectDroppedAsync(items.Files, items.Folders);
        }
    }

    // ---- Focus ----

    // Keep keyboard focus on the next useful control when a view is swapped out under it. Moving through the queue keeps it
    // where it is, so Next can be pressed again.
    private void MoveFocus()
    {
        if (!IsLoaded)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var section = (ViewModel.ShowsEmpty, ViewModel.ShowsReading, ViewModel.ShowsReview, ViewModel.ShowsFailedItem, ViewModel.IsUpscaling);
            if (section == _section)
            {
                return;
            }

            _section = section;
            if (FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused && IsInside(focused, QueueBar))
            {
                return;
            }

            Control? target = null;
            if (ViewModel.ShowsEmpty)
            {
                target = PickFileButton;
            }
            else if (ViewModel.ShowsReading)
            {
                target = CancelReadingButton;
            }
            else if (ViewModel.ShowsFailedItem)
            {
                target = RemoveFailedButton;
            }
            else if (ViewModel.ShowsReview)
            {
                target = ViewModel.IsUpscaling ? CancelUpscaleButton : ConvertButton.IsEnabled ? ConvertButton : PickAnotherButton;
            }

            target?.Focus(FocusState.Programmatic);
        });
    }

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, container))
            {
                return true;
            }
        }

        return false;
    }
}

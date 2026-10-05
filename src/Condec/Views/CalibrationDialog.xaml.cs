// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Architecture;
using Condec.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace Condec.Views;

/// <summary>
/// The user marks two points on the picture and says how far apart they really are. A point is marked by a click or tap,
/// or with the keyboard: the arrow keys move a cursor and Enter marks it. Points are in pixels of the picture shown.
/// </summary>
public sealed partial class CalibrationDialog : ContentDialog
{
    private readonly int _width;
    private readonly int _height;
    private readonly double _marker;
    private readonly double _step;
    private readonly List<(double X, double Y)> _points = [];
    private (double X, double Y) _cursor;

    public CalibrationDialog(ImageSource picture, int width, int height)
    {
        InitializeComponent();
        _width = width;
        _height = height;

        // Sizes follow the picture, which is shown scaled to fit: a marker is the same size on screen for a small and a large one.
        _marker = Math.Max(6, width / 110.0);
        _step = Math.Max(1, width / 150.0);
        _cursor = (width / 2.0, height / 2.0);

        Title = Loc.Get("Architecture.Calibrate.Title");
        PrimaryButtonText = Loc.Get("Architecture.Calibrate.Apply");
        CloseButtonText = Loc.Get("Processing.Cancel");
        Instruction.Text = Loc.Get("Architecture.Calibrate.Instruction");
        KeyboardHint.Text = Loc.Get("Architecture.Calibrate.KeyboardHint");
        ResetButton.Content = Loc.Get("Architecture.Calibrate.Reset");
        Length.Header = Loc.Get("Architecture.Calibrate.Length");

        // Empty, not 0: the user types the distance, and the placeholder says where.
        Length.NumberFormatter = new TypedNumberFormatter();
        Length.Value = double.NaN;
        Unit.Header = Loc.Get("Architecture.Calibrate.Unit");
        AutomationProperties.SetName(Focuser, Loc.Get("Architecture.Calibrate.Picture"));

        Unit.Items.Add(Loc.Get("Architecture.Unit.Millimeters"));
        Unit.Items.Add(Loc.Get("Architecture.Unit.Centimeters"));
        Unit.Items.Add(Loc.Get("Architecture.Unit.Meters"));

        // Floor plans are measured in meters.
        Unit.SelectedIndex = 2;

        Surface.Width = width;
        Surface.Height = height;
        Picture.Width = width;
        Picture.Height = height;
        Picture.Source = picture;

        var thickness = Math.Max(2, width / 450.0);
        Link.StrokeThickness = thickness;
        foreach (var ellipse in new[] { First, Second, Cursor })
        {
            ellipse.Width = ellipse.Height = _marker * 2;
            ellipse.StrokeThickness = thickness;
        }

        UpdateState();
    }

    /// <summary>The marked points, in pixels of the picture.</summary>
    public double X1 => _points[0].X;

    public double Y1 => _points[0].Y;

    public double X2 => _points[1].X;

    public double Y2 => _points[1].Y;

    /// <summary>The real distance between the points, in millimeters.</summary>
    public double LengthMillimeters => ScaleCalibration.ToMillimeters(Length.Value, SelectedUnit);

    private CalibrationUnit SelectedUnit => Unit.SelectedIndex switch
    {
        0 => CalibrationUnit.Millimeters,
        1 => CalibrationUnit.Centimeters,
        _ => CalibrationUnit.Meters,
    };

    private void Mark(double x, double y)
    {
        // A third point starts over, so the two that count are always the last two the user meant.
        if (_points.Count == 2)
        {
            _points.Clear();
        }

        _points.Add((Math.Clamp(x, 0, _width), Math.Clamp(y, 0, _height)));
        UpdateState();
    }

    private void OnSurfacePressed(object sender, PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(Surface).Position;
        Focuser.Focus(FocusState.Pointer);
        _cursor = (position.X, position.Y);
        Mark(position.X, position.Y);
    }

    private void OnSurfaceKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var step = _step * (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) ? 5 : 1);
        switch (e.Key)
        {
            case VirtualKey.Left:
                _cursor.X -= step;
                break;
            case VirtualKey.Right:
                _cursor.X += step;
                break;
            case VirtualKey.Up:
                _cursor.Y -= step;
                break;
            case VirtualKey.Down:
                _cursor.Y += step;
                break;
            case VirtualKey.Enter or VirtualKey.Space:
                Mark(_cursor.X, _cursor.Y);
                e.Handled = true;
                return;
            default:
                return;
        }

        _cursor = (Math.Clamp(_cursor.X, 0, _width), Math.Clamp(_cursor.Y, 0, _height));
        e.Handled = true;
        UpdateState();
    }

    private void OnSurfaceFocusChanged(object sender, RoutedEventArgs e) => UpdateState();

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _points.Clear();
        UpdateState();
        Focuser.Focus(FocusState.Programmatic);
    }

    private void OnValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => UpdateState();

    private void OnUnitChanged(object sender, SelectionChangedEventArgs e) => UpdateState();

    private static void Place(Ellipse ellipse, double radius, (double X, double Y) at)
    {
        Canvas.SetLeft(ellipse, at.X - radius);
        Canvas.SetTop(ellipse, at.Y - radius);
    }

    private void UpdateState()
    {
        PointsText.Text = Loc.Format("Architecture.Calibrate.Points", _points.Count);
        ResetButton.IsEnabled = _points.Count > 0;

        First.Visibility = _points.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Second.Visibility = _points.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        Link.Visibility = _points.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_points.Count > 0)
        {
            Place(First, _marker, _points[0]);
        }

        if (_points.Count > 1)
        {
            Place(Second, _marker, _points[1]);
            Link.X1 = _points[0].X;
            Link.Y1 = _points[0].Y;
            Link.X2 = _points[1].X;
            Link.Y2 = _points[1].Y;
        }

        // The keyboard cursor shows while the picture has focus.
        Cursor.Visibility = Focuser.FocusState == FocusState.Unfocused ? Visibility.Collapsed : Visibility.Visible;
        Place(Cursor, _marker, _cursor);

        IsPrimaryButtonEnabled = _points.Count == 2
            && double.IsFinite(Length.Value)
            && ScaleCalibration.MillimetersPerPixel(_points[0].X, _points[0].Y, _points[1].X, _points[1].Y, LengthMillimeters) is not null;
    }
}

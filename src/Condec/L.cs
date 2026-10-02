// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Condec;

/// <summary>
/// Attached properties that take a string-resource key instead of the text itself:
/// <c>local:L.Text="Page.Convert.Title"</c> sets the TextBlock's text to the "Page.Convert.Title" string in the app's language.
/// Every key is checked against Resources/Strings.resx by the localization tests.
/// </summary>
public static class L
{
    public static readonly DependencyProperty TextProperty = Register("Text", (target, text) =>
    {
        switch (target)
        {
            case TextBlock block:
                block.Text = text;
                break;
            case SelectorBarItem item:
                item.Text = text;
                break;
            default:
                throw Unsupported(target, "Text");
        }
    });

    public static readonly DependencyProperty OnContentProperty = Register("OnContent", (target, text) =>
    {
        if (target is ToggleSwitch toggle)
        {
            toggle.OnContent = text;
        }
        else
        {
            throw Unsupported(target, "OnContent");
        }
    });

    public static readonly DependencyProperty OffContentProperty = Register("OffContent", (target, text) =>
    {
        if (target is ToggleSwitch toggle)
        {
            toggle.OffContent = text;
        }
        else
        {
            throw Unsupported(target, "OffContent");
        }
    });

    public static readonly DependencyProperty ContentProperty = Register("Content", (target, text) =>
    {
        if (target is ContentControl control)
        {
            control.Content = text;
        }
        else
        {
            throw Unsupported(target, "Content");
        }
    });

    public static readonly DependencyProperty HeaderProperty = Register("Header", (target, text) =>
    {
        switch (target)
        {
            case ComboBox comboBox:
                comboBox.Header = text;
                break;
            case NumberBox numberBox:
                numberBox.Header = text;
                break;
            case RadioButtons radioButtons:
                radioButtons.Header = text;
                break;
            default:
                throw Unsupported(target, "Header");
        }
    });

    public static readonly DependencyProperty PlaceholderTextProperty = Register("PlaceholderText", (target, text) =>
    {
        switch (target)
        {
            case ComboBox comboBox:
                comboBox.PlaceholderText = text;
                break;
            case NumberBox numberBox:
                numberBox.PlaceholderText = text;
                break;
            default:
                throw Unsupported(target, "PlaceholderText");
        }
    });

    public static readonly DependencyProperty TitleProperty = Register("Title", (target, text) =>
    {
        if (target is InfoBar infoBar)
        {
            infoBar.Title = text;
        }
        else
        {
            throw Unsupported(target, "Title");
        }
    });

    public static readonly DependencyProperty MessageProperty = Register("Message", (target, text) =>
    {
        if (target is InfoBar infoBar)
        {
            infoBar.Message = text;
        }
        else
        {
            throw Unsupported(target, "Message");
        }
    });

    public static readonly DependencyProperty AutomationNameProperty = Register("AutomationName", (target, text) => AutomationProperties.SetName(target, text));

    public static readonly DependencyProperty ToolTipProperty = Register("ToolTip", (target, text) => ToolTipService.SetToolTip(target, text));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string key) => element.SetValue(TextProperty, key);

    public static string GetOnContent(DependencyObject element) => (string)element.GetValue(OnContentProperty);

    public static void SetOnContent(DependencyObject element, string key) => element.SetValue(OnContentProperty, key);

    public static string GetOffContent(DependencyObject element) => (string)element.GetValue(OffContentProperty);

    public static void SetOffContent(DependencyObject element, string key) => element.SetValue(OffContentProperty, key);

    public static string GetContent(DependencyObject element) => (string)element.GetValue(ContentProperty);

    public static void SetContent(DependencyObject element, string key) => element.SetValue(ContentProperty, key);

    public static string GetHeader(DependencyObject element) => (string)element.GetValue(HeaderProperty);

    public static void SetHeader(DependencyObject element, string key) => element.SetValue(HeaderProperty, key);

    public static string GetPlaceholderText(DependencyObject element) => (string)element.GetValue(PlaceholderTextProperty);

    public static void SetPlaceholderText(DependencyObject element, string key) => element.SetValue(PlaceholderTextProperty, key);

    public static string GetTitle(DependencyObject element) => (string)element.GetValue(TitleProperty);

    public static void SetTitle(DependencyObject element, string key) => element.SetValue(TitleProperty, key);

    public static string GetMessage(DependencyObject element) => (string)element.GetValue(MessageProperty);

    public static void SetMessage(DependencyObject element, string key) => element.SetValue(MessageProperty, key);

    public static string GetAutomationName(DependencyObject element) => (string)element.GetValue(AutomationNameProperty);

    public static void SetAutomationName(DependencyObject element, string key) => element.SetValue(AutomationNameProperty, key);

    public static string GetToolTip(DependencyObject element) => (string)element.GetValue(ToolTipProperty);

    public static void SetToolTip(DependencyObject element, string key) => element.SetValue(ToolTipProperty, key);

    private static DependencyProperty Register(string name, Action<DependencyObject, string> apply) =>
        DependencyProperty.RegisterAttached(
            name,
            typeof(string),
            typeof(L),
            new PropertyMetadata(null, (target, e) =>
            {
                if (e.NewValue is string key)
                {
                    apply(target, Loc.Get(key));
                }
            }));

    private static ArgumentException Unsupported(DependencyObject target, string property) =>
        new($"L.{property} isn't supported on {target.GetType().Name}.");
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Diagnostics;
using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace Condec.Services;

/// <summary>What the view model needs from the desktop: file dialogs and File Explorer.</summary>
public interface IDesktopServices
{
    /// <summary>Null when the user cancels.</summary>
    Task<string?> PickSourceFileAsync(IReadOnlyList<string> extensions);

    /// <summary>Null when the user cancels. The dialog asks before overwriting an existing file.</summary>
    Task<string?> PickDestinationAsync(string suggestedName, string? folder, string typeLabel, string extension);

    void OpenFile(string path);

    void ShowInFolder(string path);

    void OpenFolder(string path);
}

public sealed class DesktopServices(WindowId windowId) : IDesktopServices
{
    public async Task<string?> PickSourceFileAsync(IReadOnlyList<string> extensions)
    {
        var picker = new FileOpenPicker(windowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var result = await picker.PickSingleFileAsync();
        return string.IsNullOrEmpty(result?.Path) ? null : result.Path;
    }

    public async Task<string?> PickDestinationAsync(string suggestedName, string? folder, string typeLabel, string extension)
    {
        // This picker doesn't create the file, so nothing exists at the path until the pipeline has verified the output.
        var picker = new FileSavePicker(windowId)
        {
            SuggestedFileName = suggestedName,
            DefaultFileExtension = extension,
            ShowOverwritePrompt = true,
        };
        if (folder is not null)
        {
            picker.SuggestedFolder = folder;
        }

        picker.FileTypeChoices.Add(typeLabel, [extension]);

        var result = await picker.PickSaveFileAsync();
        return string.IsNullOrEmpty(result?.Path) ? null : result.Path;
    }

    public void OpenFile(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void ShowInFolder(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

    public void OpenFolder(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}

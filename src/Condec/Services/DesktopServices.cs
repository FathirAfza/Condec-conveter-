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

    /// <summary>One or more files; empty when the user cancels. Opens in <paramref name="suggestedFolder"/> when it exists.</summary>
    Task<IReadOnlyList<string>> PickSourceFilesAsync(IReadOnlyList<string> extensions, string? suggestedFolder = null);

    /// <summary>The folder a batch saves into; null when the user cancels.</summary>
    Task<string?> PickFolderAsync(string? suggestedFolder);

    /// <summary>Null when the user cancels. The dialog asks before overwriting an existing file.</summary>
    Task<string?> PickDestinationAsync(string suggestedName, string? folder, string typeLabel, string extension);

    void OpenFile(string path);

    void ShowInFolder(string path);

    void OpenFolder(string path);

    /// <summary>Opens a text file in Notepad (the license notes are Markdown, which Windows has no app for by default).</summary>
    void OpenTextFile(string path);

    /// <summary>Opens the folder in File Explorer through the Windows launcher, creating it first. False when that failed.</summary>
    Task<bool> LaunchFolderAsync(string path);
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

    public async Task<IReadOnlyList<string>> PickSourceFilesAsync(IReadOnlyList<string> extensions, string? suggestedFolder = null)
    {
        var picker = new FileOpenPicker(windowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        if (suggestedFolder is not null && Directory.Exists(suggestedFolder))
        {
            picker.SuggestedFolder = suggestedFolder;
        }

        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var results = await picker.PickMultipleFilesAsync();
        return results is null ? [] : [.. results.Select(r => r.Path).Where(p => !string.IsNullOrEmpty(p))];
    }

    public async Task<string?> PickFolderAsync(string? suggestedFolder)
    {
        var picker = new FolderPicker(windowId)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        if (suggestedFolder is not null && Directory.Exists(suggestedFolder))
        {
            picker.SuggestedFolder = suggestedFolder;
        }

        var result = await picker.PickSingleFolderAsync();
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

    public void OpenTextFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The file isn't there.", path);
        }

        // Notepad always: with no app set for .md, the shell opens an "Open with" dialog instead of failing.
        Process.Start("notepad.exe", $"\"{path}\"");
    }

    public async Task<bool> LaunchFolderAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
            return await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }
}

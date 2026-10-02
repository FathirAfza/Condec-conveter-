// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core;

/// <summary>
/// Where Condec keeps its own data: <c>%LOCALAPPDATA%\Condec</c>. In the MSIX package, Windows redirects
/// new files there to a private per-package location and removes them when the app is uninstalled.
/// </summary>
public static class CondecPaths
{
    public static string AppDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Condec");

    public static string HistoryFile => Path.Combine(AppDataDirectory, "history.json");

    public static string JournalDirectory => Path.Combine(AppDataDirectory, "journal");

    /// <summary>The settings of the portable build. The MSIX build keeps them in <c>ApplicationData.LocalSettings</c>.</summary>
    public static string SettingsFile => Path.Combine(AppDataDirectory, "settings.json");

    /// <summary>The activity log (Settings, Log), one file per day.</summary>
    public static string LogDirectory => Path.Combine(AppDataDirectory, "logs");

    /// <summary>The render cache (tiles and other intermediate data) that "Clear cache" in Settings empties.</summary>
    public static string RenderCacheDirectory => Path.Combine(AppDataDirectory, "cache");

    /// <summary>LibreOffice's user profile for headless conversions. Plain %LOCALAPPDATA%, not
    /// <c>ApplicationData.Current</c>, so the portable (unpackaged) build works the same way.</summary>
    public static string LibreOfficeProfileDirectory => Path.Combine(AppDataDirectory, "libreoffice-profile");
}

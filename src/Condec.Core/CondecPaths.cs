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
}

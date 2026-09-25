using Microsoft.Win32;

namespace Condec.Core.Documents;

/// <summary>Finds soffice.exe: an installed LibreOffice first, then the copy packaged with Condec.</summary>
public static class LibreOfficeLocator
{
    /// <summary>Where the MSIX package puts LibreOffice (see tools\fetch-libreoffice.ps1).</summary>
    public static string BundledSoffice { get; } = Path.Combine(AppContext.BaseDirectory, "LibreOffice", "program", "soffice.exe");

    /// <summary>
    /// An installed LibreOffice wins because the user keeps it up to date; the packaged copy is only as new
    /// as the Condec release that brought it.
    /// </summary>
    public static string? FindSoffice() => FindInstalledSoffice() ?? Existing(BundledSoffice);

    /// <summary>
    /// The installer writes the program folder to the default value of HKLM\SOFTWARE\LibreOffice\UNO\InstallPath
    /// (checked on LibreOffice 26.8). The standard install folders are the fallback for installs without that key.
    /// </summary>
    public static string? FindInstalledSoffice()
    {
        foreach (var view in (RegistryView[])[RegistryView.Registry64, RegistryView.Registry32])
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = hive.OpenSubKey(@"SOFTWARE\LibreOffice\UNO\InstallPath");
            if (key?.GetValue(null) is string folder && Existing(Path.Combine(folder, "soffice.exe")) is { } found)
            {
                return found;
            }
        }

        return Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"))
            ?? Existing(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe"));
    }

    private static string? Existing(string path) => File.Exists(path) ? path : null;
}

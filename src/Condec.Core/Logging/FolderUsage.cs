// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Logging;

/// <summary>Size and clean-up of Condec's own folders (the render cache, the logs).</summary>
public static class FolderUsage
{
    /// <summary>The bytes of every file below the folder; 0 when it doesn't exist. Files that can't be read are skipped.</summary>
    public static long Size(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
        {
            try
            {
                total += file.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    /// <summary>
    /// Deletes what is inside the folder (the folder itself stays). With a pattern, only matching files at the top.
    /// Returns false when something couldn't be deleted; the rest is deleted anyway.
    /// </summary>
    public static bool Clear(string directory, string? pattern = null)
    {
        if (!Directory.Exists(directory))
        {
            return true;
        }

        var complete = true;
        var folder = new DirectoryInfo(directory);
        foreach (var entry in pattern is null ? folder.EnumerateFileSystemInfos() : folder.EnumerateFiles(pattern))
        {
            try
            {
                if (entry is DirectoryInfo subfolder)
                {
                    subfolder.Delete(recursive: true);
                }
                else
                {
                    entry.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
            }
        }

        return complete;
    }
}

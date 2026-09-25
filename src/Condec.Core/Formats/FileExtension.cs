// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Formats;

public static class FileExtension
{
    /// <summary>Lower case with a leading dot: "PNG", ".Png" and "png" all become ".png".</summary>
    public static string Normalize(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var trimmed = extension.Trim();
        if (trimmed.Length == 0 || trimmed == ".")
        {
            throw new ArgumentException("The extension is empty.", nameof(extension));
        }

        var lower = trimmed.ToLowerInvariant();
        return lower[0] == '.' ? lower : "." + lower;
    }

    /// <summary>The normalized extension of <paramref name="path"/>, or an empty string when it has none.</summary>
    public static string FromPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Length <= 1 ? string.Empty : Normalize(extension);
    }

    /// <summary>The short code used in "DOCX → PDF": the extension in upper case, without the dot.</summary>
    public static string ToCode(string extension) => Normalize(extension)[1..].ToUpperInvariant();
}

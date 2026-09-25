// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Conversion;

/// <summary>
/// Reopens a finished output with a real decoder for its format, as the last step of the
/// integrity check. A file that can't be decoded is never kept.
/// </summary>
public interface IOutputValidator
{
    bool CanValidate(string targetExtension);

    /// <summary>
    /// Throws when <paramref name="path"/> can't be read as <paramref name="targetExtension"/>.
    /// The file still has its temporary name, so don't rely on the extension in the path.
    /// </summary>
    Task ValidateAsync(string path, string targetExtension, CancellationToken ct);
}

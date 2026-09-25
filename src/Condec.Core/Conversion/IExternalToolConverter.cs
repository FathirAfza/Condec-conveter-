// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Conversion;

/// <summary>
/// A converter that depends on a separately installed program (LibreOffice). Unlike a missing codec,
/// a missing program doesn't hide the formats: they are listed disabled, with the reason.
/// </summary>
public interface IExternalToolConverter : IConverter
{
    ExternalToolStatus GetToolStatus();
}

/// <param name="UnavailableReason">Shown to the user when the program is missing.</param>
public sealed record ExternalToolStatus(bool IsAvailable, string? UnavailableReason = null)
{
    public static ExternalToolStatus Available { get; } = new(true);

    public static ExternalToolStatus Unavailable(string reason) => new(false, reason);
}

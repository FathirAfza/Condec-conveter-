// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Conversion;

public enum NoteSeverity
{
    /// <summary>Something the user may want to know about the result, such as a dropped frame.</summary>
    Informational,

    /// <summary>The source looked damaged, so the result may not be what the user expects.</summary>
    Warning,
}

/// <summary>
/// A remark from a converter about a conversion that still succeeded (DESIGN §6.1, "Selesai"). The text is already
/// in the app's language.
/// </summary>
public sealed record ConversionNote(NoteSeverity Severity, string Message);

/// <summary>Where a converter puts its <see cref="ConversionNote"/>s while it works.</summary>
public sealed class ConversionNotes
{
    private readonly List<ConversionNote> _notes = [];

    public IReadOnlyList<ConversionNote> Items => _notes;

    public void Add(NoteSeverity severity, string message) => _notes.Add(new ConversionNote(severity, message));
}

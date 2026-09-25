// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Conversion;

/// <summary>A separately installed program (LibreOffice) ran but didn't produce the output.</summary>
public sealed class ExternalToolException(string toolName, string message) : Exception(message)
{
    public string ToolName { get; } = toolName;
}

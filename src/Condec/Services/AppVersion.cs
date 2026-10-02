// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Services;

/// <summary>
/// "0.1.0": the version in Package.appxmanifest, which the build also writes into the assembly (Condec.csproj), so
/// the portable build without a package reports the same number.
/// </summary>
internal static class AppVersion
{
    public static string Text { get; } = typeof(AppVersion).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
}

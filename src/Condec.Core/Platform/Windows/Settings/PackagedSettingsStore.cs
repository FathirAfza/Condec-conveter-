// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices;
using Windows.Storage;

namespace Condec.Core.Settings;

/// <summary>The settings of the MSIX build, in <c>ApplicationData.Current.LocalSettings</c> (needs package identity).</summary>
public sealed class PackagedSettingsStore : ISettingsStore
{
    private readonly ApplicationDataContainer _container = ApplicationData.Current.LocalSettings;

    public string? Get(string key) => _container.Values.TryGetValue(key, out var value) ? value as string : null;

    public void Set(string key, string? value)
    {
        if (value is null)
        {
            _container.Values.Remove(key);
        }
        else
        {
            _container.Values[key] = value;
        }
    }
}

/// <summary>Whether this process runs from an installed package (MSIX) or unpackaged (the portable build).</summary>
public static partial class PackageIdentity
{
    private const int AppModelErrorNoPackage = 15700;

    /// <summary>
    /// GetCurrentPackageFullName (appmodel.h) answers APPMODEL_ERROR_NO_PACKAGE when the process has no package
    /// identity. Asked with an empty buffer, a packaged process gets ERROR_INSUFFICIENT_BUFFER instead.
    /// </summary>
    public static bool IsPackaged()
    {
        uint length = 0;
        var result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
        return result != AppModelErrorNoPackage;
    }

    /// <summary>The MSIX build keeps settings in LocalSettings; the portable build in settings.json.</summary>
    public static ISettingsStore CreateSettingsStore(string portableSettingsFile) =>
        IsPackaged() ? new PackagedSettingsStore() : new JsonFileSettingsStore(portableSettingsFile);

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}

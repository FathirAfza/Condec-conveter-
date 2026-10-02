// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Settings;

namespace Condec.Tests.Devices;

/// <summary>The unpackaged path of the settings store choice (DESIGN §6.4). The MSIX path needs an installed package.</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void TheTestHost_HasNoPackageIdentity() => Assert.False(PackageIdentity.IsPackaged());

    [Fact]
    public void WithoutPackageIdentity_TheSettingsLiveInSettingsJson()
    {
        var file = _dir.File("settings.json");

        var store = PackageIdentity.CreateSettingsStore(file);
        store.Set(AppSettings.ThemeKey, "Dark");

        Assert.IsType<JsonFileSettingsStore>(store);
        Assert.Contains("Dark", File.ReadAllText(file));
        Assert.Equal("Dark", new JsonFileSettingsStore(file).Get(AppSettings.ThemeKey));
    }
}

// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Settings;

/// <summary>
/// Where the setting values are kept, as text under a key. The app uses <c>ApplicationData.LocalSettings</c> when it is
/// installed as MSIX and <see cref="JsonFileSettingsStore"/> when it runs unpackaged (the portable build).
/// </summary>
public interface ISettingsStore
{
    string? Get(string key);

    /// <summary>Stores the value, or forgets the key when the value is null.</summary>
    void Set(string key, string? value);
}

/// <summary>Keeps the settings in <c>settings.json</c>. Every change is written atomically (temporary file, then rename).</summary>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _values;

    public JsonFileSettingsStore(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        _values = Read();
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            if (value is null ? !_values.Remove(key) : _values.TryGetValue(key, out var old) && old == value)
            {
                return;
            }

            if (value is not null)
            {
                _values[key] = value;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var temp = _filePath + ".tmp";
            File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(_values, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, _filePath, overwrite: true);
        }
    }

    /// <summary>A missing or damaged file means defaults: a broken settings file must never stop the app from starting.</summary>
    private Dictionary<string, string> Read()
    {
        try
        {
            if (File.Exists(_filePath)
                && System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath)) is { } values)
            {
                return values;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }

        return [];
    }
}

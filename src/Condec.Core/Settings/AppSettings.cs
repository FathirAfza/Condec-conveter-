// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Devices;

namespace Condec.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark,
}

public enum LogLevel
{
    Error,
    Info,
    Debug,
}

/// <summary>
/// The values on the Settings page (DESIGN §6.4). What is stored is what the user chose; what is read back is
/// fitted to this device, so a stored GPU mode on a machine that lost its GPU reads as CPU and a stored 16× limit on a
/// 2× device reads as 2×.
/// </summary>
public sealed class AppSettings
{
    public const string RenderModeKey = "render.mode";
    public const string ScaleLimitKey = "limit.scale";
    public const string MemoryGbKey = "limit.memoryGb";
    public const string ThemeKey = "ui.theme";
    public const string MicaKey = "ui.mica";
    public const string LogEnabledKey = "log.enabled";
    public const string LogLevelKey = "log.level";
    private const string ThroughputKeyPrefix = "bench.";

    /// <summary>The smallest memory limit the slider offers.</summary>
    public const int MinimumMemoryGb = 4;

    private readonly ISettingsStore _store;

    public AppSettings(ISettingsStore store, DeviceProfile device)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public DeviceProfile Device { get; }

    /// <summary>Raised after any value changes, so open screens refresh their limits without a restart.</summary>
    public event EventHandler? Changed;

    public RenderEngine RenderMode
    {
        get => Enum.TryParse<RenderEngine>(_store.Get(RenderModeKey), out var engine) && Device.IsAvailable(engine)
            ? engine
            : Device.DefaultEngine;
        set => Write(RenderModeKey, value.ToString());
    }

    /// <summary>The largest scale the user allows: 2, 4, 8 or 16, never above what the device can do.</summary>
    public int ScaleLimit
    {
        get
        {
            var device = CapabilityPolicy.GpuScaleLimit(Device);
            return int.TryParse(_store.Get(ScaleLimitKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var stored)
                ? Math.Clamp(stored, 2, device)
                : device;
        }
        set => Write(ScaleLimitKey, value.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The most RAM Condec may use, in gigabytes: from 4 up to the installed RAM; 75% of it at first.</summary>
    public int MemoryLimitGb
    {
        get
        {
            var installed = Device.InstalledRamGb;
            var minimum = Math.Min(MinimumMemoryGb, installed);
            var stored = int.TryParse(_store.Get(MemoryGbKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : (int)Math.Round(installed * 0.75, MidpointRounding.AwayFromZero);
            return Math.Clamp(stored, minimum, Math.Max(minimum, installed));
        }
        set => Write(MemoryGbKey, value.ToString(CultureInfo.InvariantCulture));
    }

    public ThemePreference Theme
    {
        get => Enum.TryParse<ThemePreference>(_store.Get(ThemeKey), out var theme) ? theme : ThemePreference.System;
        set => Write(ThemeKey, value.ToString());
    }

    public bool MicaEnabled
    {
        get => !bool.TryParse(_store.Get(MicaKey), out var mica) || mica;
        set => Write(MicaKey, value.ToString());
    }

    public bool LogEnabled
    {
        get => !bool.TryParse(_store.Get(LogEnabledKey), out var enabled) || enabled;
        set => Write(LogEnabledKey, value.ToString());
    }

    public LogLevel LogLevel
    {
        get => Enum.TryParse<LogLevel>(_store.Get(LogLevelKey), out var level) ? level : LogLevel.Info;
        set => Write(LogLevelKey, value.ToString());
    }

    /// <summary>
    /// The measured speed of an engine in megapixels per second, or null when it hasn't been measured on this device.
    /// A speed measured on another device name (a new GPU, a different NPU) is ignored.
    /// </summary>
    public double? GetThroughput(RenderEngine engine)
    {
        var parts = _store.Get(ThroughputKeyPrefix + engine)?.Split('|', 2);
        return parts is [var speed, var device]
            && device == Device.DeviceName(engine)
            && double.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && value > 0
            ? value
            : null;
    }

    public void SetThroughput(RenderEngine engine, double megapixelsPerSecond)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(megapixelsPerSecond, 0);
        Write(ThroughputKeyPrefix + engine, $"{megapixelsPerSecond.ToString("R", CultureInfo.InvariantCulture)}|{Device.DeviceName(engine)}");
    }

    private void Write(string key, string value)
    {
        _store.Set(key, value);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

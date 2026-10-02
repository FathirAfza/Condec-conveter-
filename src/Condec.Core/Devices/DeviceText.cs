// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Formats;
using Condec.Core.Localization;

namespace Condec.Core.Devices;

/// <summary>How a <see cref="DeviceProfile"/> reads on the Settings page (DESIGN §6.4, "Perangkat ini" and "Render mode").</summary>
public static class DeviceText
{
    /// <summary>"8 GB": installed RAM in whole gigabytes, as the limits count it.</summary>
    public static string Ram(DeviceProfile device, CultureInfo? culture = null) =>
        Loc.Format(culture, "Device.Gigabytes", device.InstalledRamGb);

    public static string Gpu(DeviceProfile device, CultureInfo? culture = null) =>
        device.Gpu?.Name ?? Loc.Get("Device.NotDetected", culture);

    /// <summary>"8 GB VRAM" for a graphics card, "Integrated · 496 MB" for an integrated GPU, null without a GPU.</summary>
    public static string? GpuDetail(DeviceProfile device, CultureInfo? culture = null) => device.Gpu switch
    {
        null => null,
        { IsIntegrated: true } gpu => Loc.Format(culture, "Device.GpuIntegrated", DisplayFormat.FormatFileSize(gpu.DedicatedMemoryBytes, culture)),
        _ => Loc.Format(culture, "Device.GpuDedicated", device.GpuMemoryGb),
    };

    public static string Npu(DeviceProfile device, CultureInfo? culture = null) =>
        device.NpuName ?? Loc.Get("Device.NotDetected", culture);

    /// <summary>"GPU · AMD Radeon(TM) Graphics"; only the engine's short name when the device isn't there.</summary>
    public static string Engine(DeviceProfile device, RenderEngine engine, CultureInfo? culture = null) =>
        device.DeviceName(engine) is { } name ? $"{ShortName(engine)} · {name}" : ShortName(engine);

    /// <summary>The status under each choice in Render mode.</summary>
    public static string EngineStatus(DeviceProfile device, RenderEngine engine, CultureInfo? culture = null) => engine switch
    {
        RenderEngine.Cpu => Loc.Get("Device.CpuStatus", culture),
        RenderEngine.Gpu when device.HasGpu => Loc.Get("Device.GpuStatus", culture),
        RenderEngine.Npu when device.HasNpu => Loc.Get("Device.Detected", culture),
        _ => Loc.Get("Device.NotDetected", culture),
    };

    public static string ShortName(RenderEngine engine) => engine switch
    {
        RenderEngine.Gpu => "GPU",
        RenderEngine.Npu => "NPU",
        _ => "CPU",
    };
}

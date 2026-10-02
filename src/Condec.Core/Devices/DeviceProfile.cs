// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

namespace Condec.Core.Devices;

/// <summary>The engines that can run upscaling and tracing (DESIGN §6.4, Render mode).</summary>
public enum RenderEngine
{
    Gpu,
    Cpu,
    Npu,
}

/// <summary>The graphics adapter chosen for rendering. <see cref="DedicatedMemoryBytes"/> is the adapter's own memory (VRAM).</summary>
public sealed record GpuInfo(string Name, long DedicatedMemoryBytes, bool IsIntegrated);

/// <summary>What this computer can do, as read once at start. All values are plain data so tests can build any machine.</summary>
public sealed record DeviceProfile(string CpuName, long InstalledRamBytes, GpuInfo? Gpu, string? NpuName)
{
    private const double Gib = 1024d * 1024 * 1024;

    /// <summary>
    /// Installed RAM in whole gigabytes, rounded to the nearest. Windows reports an 8 GB laptop as about 7.7 GB
    /// once hardware has reserved its share, and the limits in DESIGN §7 speak of the RAM that was bought.
    /// </summary>
    public int InstalledRamGb => ToWholeGb(InstalledRamBytes);

    /// <summary>The adapter's own memory in whole gigabytes, rounded to the nearest (a 12 GB card reports 11.99).</summary>
    public int GpuMemoryGb => Gpu is null ? 0 : ToWholeGb(Gpu.DedicatedMemoryBytes);

    public bool HasGpu => Gpu is not null;

    public bool HasNpu => NpuName is not null;

    public bool IsAvailable(RenderEngine engine) => engine switch
    {
        RenderEngine.Gpu => HasGpu,
        RenderEngine.Npu => HasNpu,
        _ => true,
    };

    /// <summary>The engine used when nothing is chosen, or when the chosen one is gone: the GPU if there is one, else the CPU.</summary>
    public RenderEngine DefaultEngine => HasGpu ? RenderEngine.Gpu : RenderEngine.Cpu;

    /// <summary>The device's name for an engine, or null when the engine isn't there.</summary>
    public string? DeviceName(RenderEngine engine) => engine switch
    {
        RenderEngine.Gpu => Gpu?.Name,
        RenderEngine.Npu => NpuName,
        _ => CpuName,
    };

    private static int ToWholeGb(long bytes) => (int)Math.Round(bytes / Gib, MidpointRounding.AwayFromZero);
}

/// <summary>Reads the <see cref="DeviceProfile"/> of this computer. The Windows implementation lives in Platform/Windows.</summary>
public interface IDeviceProbe
{
    DeviceProfile Detect();
}

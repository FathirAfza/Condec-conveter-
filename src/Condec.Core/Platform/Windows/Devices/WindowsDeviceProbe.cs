// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices;
using Condec.Core.Devices;
using Microsoft.Win32;

namespace Condec.Core.Platform.Windows.Devices;

/// <summary>
/// Reads this computer: the processor name from the registry, the installed RAM from Windows, and the graphics
/// adapter and the NPU from DXCore. Every part is read on its own, so a machine that can't answer one question (an old
/// Windows without DXCore, a virtual machine without SMBIOS memory data) still gives the others.
/// </summary>
public sealed partial class WindowsDeviceProbe : IDeviceProbe
{
    public DeviceProfile Detect()
    {
        var (gpu, npu) = ReadAccelerators();
        return new DeviceProfile(ReadCpuName(), ReadInstalledRamBytes(), gpu, npu);
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name && !string.IsNullOrWhiteSpace(name))
            {
                return string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return RuntimeInformation.ProcessArchitecture.ToString();
    }

    /// <summary>
    /// The RAM that was installed (GetPhysicallyInstalledSystemMemory, from the firmware's tables). When Windows can't
    /// say, the memory it manages (GlobalMemoryStatusEx), which is a little less than what was installed.
    /// </summary>
    private static long ReadInstalledRamBytes()
    {
        if (GetPhysicallyInstalledSystemMemory(out var kilobytes) && kilobytes > 0)
        {
            return checked((long)kilobytes * 1024);
        }

        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.TotalPhysical : 0;
    }

    private static (GpuInfo? Gpu, string? Npu) ReadAccelerators()
    {
        try
        {
            var factory = DxCore.CreateFactory();
            return factory is null ? (null, null) : (ReadGpu(factory), ReadNpu(factory));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException or InvalidCastException or MarshalDirectiveException)
        {
            return (null, null);
        }
    }

    /// <summary>The real graphics card with the most memory; an integrated one when there is nothing else. The software renderer is skipped.</summary>
    private static GpuInfo? ReadGpu(IDXCoreAdapterFactory factory)
    {
        var found = new List<GpuInfo>();
        foreach (var adapter in DxCore.List(factory, DxCore.GpuAttribute))
        {
            if (DxCore.ReadBool(adapter, DxCore.IsHardware) == false)
            {
                continue;
            }

            var memory = DxCore.ReadSize(adapter, DxCore.DedicatedAdapterMemory) ?? 0;
            var integrated = DxCore.ReadBool(adapter, DxCore.IsIntegrated) ?? memory == 0;
            found.Add(new GpuInfo(DxCore.ReadString(adapter, DxCore.DriverDescription) ?? "GPU", memory, integrated));
        }

        return found.OrderBy(g => g.IsIntegrated).ThenByDescending(g => g.DedicatedMemoryBytes).FirstOrDefault();
    }

    private static string? ReadNpu(IDXCoreAdapterFactory factory)
    {
        var npus = DxCore.List(factory, DxCore.NpuAttribute);
        return npus.Count == 0 ? null : DxCore.ReadString(npus[0], DxCore.DriverDescription) ?? "NPU";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

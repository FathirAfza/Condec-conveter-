// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices;

namespace Condec.Core.Upscale;

/// <summary>
/// Reads how busy the device is for Adaptive (DESIGN §7.6): the GPU's 3D engine use by every other process, from the
/// "GPU Engine" performance counters (PDH, the same numbers Task Manager shows), and the memory load from
/// GlobalMemoryStatusEx. Nothing leaves the device. A counter Windows doesn't have (an old driver, a virtual machine) reads as null.
/// </summary>
public sealed partial class WindowsLoadMonitor : ILoadMonitor, IDisposable
{
    private const string GpuCounterPath = @"\GPU Engine(*)\Utilization Percentage";
    private const uint PdhFormatDouble = 0x00000200;
    private const uint PdhFormatNoCap100 = 0x00008000;
    private const int PdhMoreData = unchecked((int)0x800007D2);
    private const uint PdhValidData = 0;
    private const uint PdhNewData = 1;

    private readonly string _ownPid = $"pid_{Environment.ProcessId}_";
    private nint _query;
    private nint _counter;

    private WindowsLoadMonitor()
    {
    }

    /// <summary>Opens the counters. The GPU part is left out when Windows doesn't have it; the memory part always works.</summary>
    public static WindowsLoadMonitor Open()
    {
        var monitor = new WindowsLoadMonitor();
        if (PdhOpenQueryW(null, 0, out var query) == 0)
        {
            monitor._query = query;
            if (PdhAddEnglishCounterW(query, GpuCounterPath, 0, out var counter) == 0 && PdhCollectQueryData(query) == 0)
            {
                // A utilization counter is a rate: it has a value from the second collection on, which Read makes.
                monitor._counter = counter;
            }
        }

        return monitor;
    }

    public SystemLoad Read() => new(ReadOtherGpuPercent(), ReadMemoryLoadPercent());

    private double? ReadOtherGpuPercent()
    {
        if (_counter == 0 || PdhCollectQueryData(_query) != 0)
        {
            return null;
        }

        uint size = 0;
        if (PdhGetFormattedCounterArrayW(_counter, PdhFormatDouble | PdhFormatNoCap100, ref size, out _, 0) != PdhMoreData || size == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(_counter, PdhFormatDouble | PdhFormatNoCap100, ref size, out var count, buffer) != 0)
            {
                return null;
            }

            var total = 0d;
            var itemSize = Marshal.SizeOf<CounterValueItem>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<CounterValueItem>(buffer + (i * itemSize));
                var name = Marshal.PtrToStringUni(item.Name) ?? string.Empty;

                // Instances read "pid_1234_luid_…_phys_0_eng_0_engtype_3D"; Condec's own work is what Adaptive makes room for.
                if (item.Status is PdhValidData or PdhNewData
                    && name.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith(_ownPid, StringComparison.Ordinal))
                {
                    total += item.Value;
                }
            }

            return Math.Clamp(total, 0, 100);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static double? ReadMemoryLoadPercent()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? status.MemoryLoad : null;
    }

    public void Dispose()
    {
        if (_query != 0)
        {
            // Closing the query closes its counters too (PDH documentation).
            _ = PdhCloseQuery(_query);
            _query = 0;
            _counter = 0;
        }
    }

    /// <summary>PDH_FMT_COUNTERVALUE_ITEM_W with a PDH_FMT_COUNTERVALUE read as a double.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValueItem
    {
        public nint Name;
        public uint Status;
        public double Value;
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

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhOpenQueryW(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhAddEnglishCounterW(nint query, string counterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    private static partial int PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, out uint itemCount, nint itemBuffer);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCloseQuery(nint query);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

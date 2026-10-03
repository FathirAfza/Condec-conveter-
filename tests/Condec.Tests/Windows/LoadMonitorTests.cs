// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Upscale;

namespace Condec.Tests.Devices;

/// <summary>Reads the real device for Adaptive. A CI runner may have no GPU counters, so the GPU part may be null.</summary>
public class LoadMonitorTests
{
    [Fact]
    public void Read_GivesTheMemoryLoad_AndAGpuShareWithinRange()
    {
        using var monitor = WindowsLoadMonitor.Open();
        Thread.Sleep(200);

        var first = monitor.Read();
        var second = monitor.Read();

        Assert.NotNull(first.MemoryLoadPercent);
        Assert.InRange(first.MemoryLoadPercent!.Value, 1, 100);
        foreach (var load in new[] { first, second })
        {
            if (load.OtherGpuPercent is { } gpu)
            {
                Assert.InRange(gpu, 0, 100);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"memory {first.MemoryLoadPercent}%, other GPU {first.OtherGpuPercent?.ToString() ?? "unknown"}%");
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var monitor = WindowsLoadMonitor.Open();
        monitor.Dispose();
        monitor.Dispose();
    }
}

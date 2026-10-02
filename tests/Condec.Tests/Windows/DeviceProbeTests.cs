// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Devices;
using Condec.Core.Platform.Windows.Devices;

namespace Condec.Tests.Devices;

/// <summary>Runs the real probe. A CI runner has no GPU or NPU, so these only check what every Windows machine has.</summary>
public class DeviceProbeTests
{
    [Fact]
    public void Detect_ReadsTheProcessorAndTheRam()
    {
        var device = new WindowsDeviceProbe().Detect();

        Assert.False(string.IsNullOrWhiteSpace(device.CpuName));
        Assert.True(device.InstalledRamGb >= 1, $"RAM read as {device.InstalledRamBytes} bytes");
    }

    [Fact]
    public void Detect_GivesAUsableProfileWhateverTheMachineHas()
    {
        var device = new WindowsDeviceProbe().Detect();

        Assert.True(device.IsAvailable(RenderEngine.Cpu));
        Assert.Equal(device.HasGpu, device.IsAvailable(RenderEngine.Gpu));
        Assert.InRange(CapabilityPolicy.GpuScaleLimit(device), 2, 16);
        if (device.Gpu is { } gpu)
        {
            Assert.False(string.IsNullOrWhiteSpace(gpu.Name));
            Assert.True(gpu.DedicatedMemoryBytes >= 0);
        }
    }
}

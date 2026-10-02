// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Globalization;
using Condec.Core.Devices;
using Condec.Core.Logging;
using Condec.Core.Settings;

namespace Condec.Tests;

/// <summary>The Core side of the shell and Settings (DESIGN §6.4): the activity log, folder size and the device texts.</summary>
public class ActivityLogTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private bool _enabled = true;
    private LogLevel _level = LogLevel.Info;
    private DateTime _now = new(2026, 10, 2, 9, 30, 15, 123);

    public void Dispose() => _dir.Dispose();

    private ActivityLog Create() => new(_dir.Path, () => _enabled, () => _level, () => _now);

    private string Today => Path.Combine(_dir.Path, "condec-2026-10-02.log");

    [Fact]
    public void Info_IsWrittenToTheFileOfTheDay_WithTimeAndLevel()
    {
        Create().Info("Conversion started");

        Assert.Equal("2026-10-02 09:30:15.123 INFO  Conversion started" + Environment.NewLine, File.ReadAllText(Today));
    }

    [Fact]
    public void Debug_IsLeftOut_UntilTheLevelIsDebug()
    {
        var log = Create();

        log.Debug("detail");
        Assert.False(File.Exists(Today));

        _level = LogLevel.Debug;
        log.Debug("detail");
        Assert.Contains("DEBUG detail", File.ReadAllText(Today));
    }

    [Fact]
    public void Error_IsWrittenAtEveryLevel()
    {
        _level = LogLevel.Error;
        var log = Create();

        log.Info("not this");
        log.Error("this");

        var text = File.ReadAllText(Today);
        Assert.DoesNotContain("not this", text);
        Assert.Contains("ERROR this", text);
    }

    [Fact]
    public void SwitchingTheLogOff_StopsWritingAtOnce()
    {
        var log = Create();
        log.Info("first");

        _enabled = false;
        log.Error("second");

        Assert.DoesNotContain("second", File.ReadAllText(Today));
    }

    [Fact]
    public void ALineBreakInTheMessage_DoesNotSplitTheEntry()
    {
        Create().Info("a\r\nb");

        Assert.Single(File.ReadAllLines(Today));
    }

    [Fact]
    public void Clear_DeletesTheLogs()
    {
        var log = Create();
        log.Info("x");

        Assert.True(log.Clear());
        Assert.Empty(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public void AFolderThatCannotBeWritten_NeverStopsTheCaller()
    {
        // A file where the folder should be.
        var blocked = _dir.File("blocked");
        File.WriteAllText(blocked, "x");

        new ActivityLog(Path.Combine(blocked, "logs"), () => true, () => LogLevel.Debug).Info("x");
    }
}

public class FolderUsageTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Size_IsZeroForAFolderThatDoesNotExist() => Assert.Equal(0, FolderUsage.Size(_dir.File("none")));

    [Fact]
    public void Size_CountsFilesInSubfolders()
    {
        Directory.CreateDirectory(_dir.File("a"));
        File.WriteAllBytes(_dir.File("one.bin"), new byte[100]);
        File.WriteAllBytes(_dir.File(Path.Combine("a", "two.bin")), new byte[50]);

        Assert.Equal(150, FolderUsage.Size(_dir.Path));
    }

    [Fact]
    public void Clear_EmptiesTheFolderButKeepsIt()
    {
        Directory.CreateDirectory(_dir.File("a"));
        File.WriteAllBytes(_dir.File(Path.Combine("a", "two.bin")), new byte[50]);
        File.WriteAllBytes(_dir.File("one.bin"), new byte[10]);

        Assert.True(FolderUsage.Clear(_dir.Path));

        Assert.True(Directory.Exists(_dir.Path));
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path));
        Assert.Equal(0, FolderUsage.Size(_dir.Path));
    }

    [Fact]
    public void Clear_WithAPattern_LeavesOtherFilesAlone()
    {
        File.WriteAllText(_dir.File("a.log"), "x");
        File.WriteAllText(_dir.File("keep.txt"), "x");

        Assert.True(FolderUsage.Clear(_dir.Path, "*.log"));

        Assert.False(File.Exists(_dir.File("a.log")));
        Assert.True(File.Exists(_dir.File("keep.txt")));
    }

    [Fact]
    public void Clear_OfAFolderThatDoesNotExist_IsFine() => Assert.True(FolderUsage.Clear(_dir.File("none")));
}

public class DeviceTextTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id");
    private const long Gib = 1024L * 1024 * 1024;

    private static readonly DeviceProfile Medium = new("Intel Core Ultra 7 155H", 32 * Gib, new GpuInfo("NVIDIA GeForce RTX 4060 Laptop GPU", 8 * Gib, false), "Intel AI Boost");
    private static readonly DeviceProfile Basic = new("Intel Core i5-1135G7", 8 * Gib, new GpuInfo("Intel Iris Xe", 512L * 1024 * 1024, true), null);
    private static readonly DeviceProfile NoGpu = new("CPU", 8 * Gib, null, null);

    [Fact]
    public void Ram_IsWholeGigabytes() => Assert.Equal("32 GB", DeviceText.Ram(Medium, English));

    [Fact]
    public void AnEngineThatIsNotThere_ReadsNotDetected_InBothLanguages()
    {
        Assert.Equal("Not detected", DeviceText.Npu(Basic, English));
        Assert.Equal("Tidak terdeteksi", DeviceText.Npu(Basic, Indonesian));
        Assert.Equal("Tidak terdeteksi", DeviceText.Gpu(NoGpu, Indonesian));
    }

    [Fact]
    public void GpuDetail_SeparatesAGraphicsCardFromAnIntegratedGpu()
    {
        Assert.Equal("8 GB VRAM", DeviceText.GpuDetail(Medium, English));
        Assert.Equal("VRAM 8 GB", DeviceText.GpuDetail(Medium, Indonesian));
        Assert.Equal("Integrated · 512 MB", DeviceText.GpuDetail(Basic, English));
        Assert.Equal("Terintegrasi · 512 MB", DeviceText.GpuDetail(Basic, Indonesian));
        Assert.Null(DeviceText.GpuDetail(NoGpu, English));
    }

    [Fact]
    public void Engine_NamesTheDevice_OrOnlyTheEngineWhenThereIsNone()
    {
        Assert.Equal("GPU · NVIDIA GeForce RTX 4060 Laptop GPU", DeviceText.Engine(Medium, RenderEngine.Gpu));
        Assert.Equal("NPU · Intel AI Boost", DeviceText.Engine(Medium, RenderEngine.Npu));
        Assert.Equal("CPU · Intel Core Ultra 7 155H", DeviceText.Engine(Medium, RenderEngine.Cpu));
        Assert.Equal("NPU", DeviceText.Engine(Basic, RenderEngine.Npu));
    }

    [Fact]
    public void EngineStatus_FollowsDESIGN_6_4()
    {
        Assert.Equal("Terdeteksi · tercepat", DeviceText.EngineStatus(Medium, RenderEngine.Gpu, Indonesian));
        Assert.Equal("Selalu tersedia · paling lambat", DeviceText.EngineStatus(Medium, RenderEngine.Cpu, Indonesian));
        Assert.Equal("Terdeteksi", DeviceText.EngineStatus(Medium, RenderEngine.Npu, Indonesian));
        Assert.Equal("Tidak terdeteksi", DeviceText.EngineStatus(Basic, RenderEngine.Npu, Indonesian));
        Assert.Equal("Tidak terdeteksi", DeviceText.EngineStatus(NoGpu, RenderEngine.Gpu, Indonesian));
    }
}

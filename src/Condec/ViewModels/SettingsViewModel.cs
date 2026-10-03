// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Condec.Core;
using Condec.Core.Devices;
using Condec.Core.Formats;
using Condec.Core.Localization;
using Condec.Core.Logging;
using Condec.Core.Settings;
using Condec.Core.Upscale;
using Condec.Services;

namespace Condec.ViewModels;

/// <summary>One choice in the "Batas upscale" list; a choice above the device stays in the list, disabled, with the reason.</summary>
public sealed record ScaleLimitOption(int Scale, bool IsEnabled)
{
    public string Text => ScaleText.Format(Scale);

    public string Caption => IsEnabled ? string.Empty : Loc.Get("Settings.ScaleLimitAboveDevice");

    /// <summary>A locked choice reads dimmed in the list; the ComboBoxItem itself is not disabled (see SettingsPage.OnScaleLimitChanged).</summary>
    public double DisplayOpacity => IsEnabled ? 1 : 0.6;

    /// <summary>What a screen reader says for the item: "4×, above the device limit".</summary>
    public override string ToString() => IsEnabled ? Text : $"{Text}, {Caption}";
}

/// <summary>The Settings page (DESIGN §6.4). Every value goes through <see cref="AppSettings"/>, which fits it to this device.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private static readonly RenderEngine[] Engines = [RenderEngine.Gpu, RenderEngine.Cpu, RenderEngine.Npu];
    private static readonly ThemePreference[] Themes = [ThemePreference.Light, ThemePreference.Dark, ThemePreference.System];
    private static readonly LogLevel[] LogLevels = [LogLevel.Error, LogLevel.Info, LogLevel.Debug];

    private readonly AppSettings _settings;
    private readonly ActivityLog _log;
    private readonly IDesktopServices _desktop;

    public SettingsViewModel(AppSettings settings, ActivityLog log, IDesktopServices desktop, string version)
    {
        _settings = settings;
        _log = log;
        _desktop = desktop;
        VersionText = $"Condec {version}";
        DeviceScaleLimit = CapabilityPolicy.GpuScaleLimit(Device);
        ScaleLimitOptions = [.. CapabilityPolicy.Presets.Select(p => new ScaleLimitOption(p, p <= DeviceScaleLimit))];
    }

    public DeviceProfile Device => _settings.Device;

    // ---- This device ----

    public string CpuText => Device.CpuName;

    public string RamText => DeviceText.Ram(Device);

    public string GpuText => DeviceText.Gpu(Device);

    public string GpuDetail => DeviceText.GpuDetail(Device) ?? string.Empty;

    public bool HasGpuDetail => Device.HasGpu;

    public string NpuText => DeviceText.Npu(Device);

    public int DeviceScaleLimit { get; }

    public string DeviceScaleLimitText => ScaleText.Format(DeviceScaleLimit);

    // ---- Render mode ----

    public string GpuEngineText => DeviceText.Engine(Device, RenderEngine.Gpu);

    public string GpuEngineStatus => DeviceText.EngineStatus(Device, RenderEngine.Gpu);

    public string CpuEngineText => DeviceText.Engine(Device, RenderEngine.Cpu);

    public string CpuEngineStatus => DeviceText.EngineStatus(Device, RenderEngine.Cpu);

    public string NpuEngineText => DeviceText.Engine(Device, RenderEngine.Npu);

    public string NpuEngineStatus => DeviceText.EngineStatus(Device, RenderEngine.Npu);

    // What a screen reader says for each choice (its content is two lines of text, which has no name of its own).
    public string GpuEngineSpoken => $"{GpuEngineText}, {GpuEngineStatus}";

    public string CpuEngineSpoken => $"{CpuEngineText}, {CpuEngineStatus}";

    public string NpuEngineSpoken => $"{NpuEngineText}, {NpuEngineStatus}";

    public bool HasGpu => Device.HasGpu;

    public bool HasNpu => Device.HasNpu;

    /// <summary>0 GPU, 1 CPU, 2 NPU: the order of the RadioButtons.</summary>
    public int RenderModeIndex
    {
        get => Array.IndexOf(Engines, _settings.RenderMode);
        set
        {
            if (value >= 0 && value < Engines.Length && Device.IsAvailable(Engines[value]) && Engines[value] != _settings.RenderMode)
            {
                _settings.RenderMode = Engines[value];
                OnPropertyChanged();
                OnPropertyChanged(nameof(RenderModeText));
            }
        }
    }

    public string RenderModeText => DeviceText.ShortName(_settings.RenderMode);

    // ---- Performance mode and Memory saver (DESIGN §7.6) ----

    /// <summary>"Extra high (80%)", "High (60%)", …: the order of the ComboBox.</summary>
    public IReadOnlyList<string> PerformanceModeOptions { get; } = [.. RenderPace.Modes.Select(mode => PerformanceText.Mode(mode))];

    /// <summary>
    /// The ComboBox's choice. Memory saver locks it on Low (the closest mode to what Memory saver does); the chosen mode is kept
    /// and comes back when Memory saver is turned off.
    /// </summary>
    public int PerformanceModeIndex
    {
        get => _settings.MemorySaver ? RenderPace.Modes.Count - 1 : RenderPace.Modes.ToList().IndexOf(_settings.PerformanceMode);
        set
        {
            if (!_settings.MemorySaver && value >= 0 && value < RenderPace.Modes.Count && RenderPace.Modes[value] != _settings.PerformanceMode)
            {
                _settings.PerformanceMode = RenderPace.Modes[value];
                OnPropertyChanged();
            }
        }
    }

    public bool CanChoosePerformanceMode => !_settings.MemorySaver;

    public string PerformanceModeDescription => Loc.Get(_settings.MemorySaver ? "Settings.PerformanceModeLocked" : "Settings.PerformanceModeDescription");

    public bool MemorySaver
    {
        get => _settings.MemorySaver;
        set
        {
            if (value != _settings.MemorySaver)
            {
                _settings.MemorySaver = value;
                foreach (var name in new[]
                {
                    nameof(MemorySaver), nameof(PerformanceModeIndex), nameof(CanChoosePerformanceMode), nameof(PerformanceModeDescription),
                    nameof(CanChooseMemoryLimit), nameof(MemoryLimitText), nameof(MemoryLimitDescription),
                })
                {
                    OnPropertyChanged(name);
                }
            }
        }
    }

    // ---- Upscale limit ----

    public IReadOnlyList<ScaleLimitOption> ScaleLimitOptions { get; }

    /// <summary>The ComboBox's choice. Items above the device stay in the list, disabled, and can't be chosen.</summary>
    public ScaleLimitOption? SelectedScaleLimit
    {
        get => ScaleLimitOptions.FirstOrDefault(o => o.Scale == _settings.ScaleLimit);
        set
        {
            if (value is { IsEnabled: true } && value.Scale != _settings.ScaleLimit)
            {
                _settings.ScaleLimit = value.Scale;
                OnPropertyChanged();
            }
        }
    }

    public string ScaleLimitDescription => Loc.Format("Settings.ScaleLimitDescription", DeviceScaleLimitText);

    // ---- Memory limit ----

    public double MemoryMinimum => Math.Min(AppSettings.MinimumMemoryGb, Device.InstalledRamGb);

    public double MemoryMaximum => Math.Max(MemoryMinimum, Device.InstalledRamGb);

    public double MemoryLimitGb
    {
        get => _settings.MemoryLimitGb;
        set
        {
            var gb = (int)Math.Round(value);
            if (gb != _settings.MemoryLimitGb)
            {
                _settings.MemoryLimitGb = gb;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MemoryLimitText));
            }
        }
    }

    /// <summary>The memory a render may use: the slider's value, or 2 GB while Memory saver is on.</summary>
    public string MemoryLimitText => Loc.Format("Device.Gigabytes", _settings.EffectiveMemoryLimitGb);

    public bool CanChooseMemoryLimit => !_settings.MemorySaver;

    public bool Adaptive
    {
        get => _settings.Adaptive;
        set
        {
            if (value != _settings.Adaptive)
            {
                _settings.Adaptive = value;
                OnPropertyChanged();
            }
        }
    }

    public string MemoryLimitDescription => _settings.MemorySaver
        ? Loc.Format("Settings.MemoryLimitLocked", RenderPace.MemorySaverGb)
        : Loc.Get("Settings.MemoryLimitDescription");

    /// <summary>"HD–2K 8 GB · 2K–4K 16 GB · …", read from the same table that sets the limits (DESIGN §7.2).</summary>
    public string RamRequirementText => Loc.Format(
        "Settings.RamRequirement",
        string.Join(" · ", CapabilityPolicy.RamTiers.Select(t => $"{Loc.Get(t.LabelKey)} {t.Gb} GB")));

    // ---- Render dump ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CacheDescription), nameof(CanClearCache))]
    [NotifyCanExecuteChangedFor(nameof(ClearCacheCommand))]
    public partial long CacheBytes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CacheDescription))]
    public partial bool CacheClearFailed { get; set; }

    public bool CanClearCache => CacheBytes > 0;

    public string CacheDescription => Loc.Format(
        CacheClearFailed ? "Settings.CacheClearFailed" : CacheBytes == 0 ? "Settings.CacheEmpty" : "Settings.CacheDescription",
        DisplayFormat.FormatFileSize(CacheBytes));

    /// <summary>Measures the cache again; called when the page opens.</summary>
    public async Task RefreshCacheAsync() => CacheBytes = await Task.Run(() => FolderUsage.Size(CondecPaths.RenderCacheDirectory));

    [RelayCommand(CanExecute = nameof(CanClearCache))]
    private async Task ClearCacheAsync()
    {
        var complete = await Task.Run(() => FolderUsage.Clear(CondecPaths.RenderCacheDirectory));
        CacheClearFailed = !complete;
        await RefreshCacheAsync();
        _log.Info(complete ? "Render cache cleared" : "Render cache partly cleared: some files are in use");
    }

    // ---- Appearance ----

    /// <summary>0 light, 1 dark, 2 follow the system: the order of the ComboBox.</summary>
    public int ThemeIndex
    {
        get => Array.IndexOf(Themes, _settings.Theme);
        set
        {
            if (value >= 0 && value < Themes.Length && Themes[value] != _settings.Theme)
            {
                _settings.Theme = Themes[value];
                OnPropertyChanged();
            }
        }
    }

    public bool MicaEnabled
    {
        get => _settings.MicaEnabled;
        set
        {
            if (value != _settings.MicaEnabled)
            {
                _settings.MicaEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    // ---- Log ----

    public bool LogEnabled
    {
        get => _settings.LogEnabled;
        set
        {
            if (value != _settings.LogEnabled)
            {
                // Written before and after the switch; only the line written while the log is on reaches the file.
                _log.Info(value ? "Log switched on" : "Log switched off");
                _settings.LogEnabled = value;
                _log.Info(value ? "Log switched on" : "Log switched off");
                OnPropertyChanged();
            }
        }
    }

    public int LogLevelIndex
    {
        get => Array.IndexOf(LogLevels, _settings.LogLevel);
        set
        {
            if (value >= 0 && value < LogLevels.Length && LogLevels[value] != _settings.LogLevel)
            {
                _settings.LogLevel = LogLevels[value];
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The result of the last log or license action, shown at the top of the General tab.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGeneralMessage))]
    public partial string? GeneralMessage { get; set; }

    public bool HasGeneralMessage => GeneralMessage is not null;

    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        GeneralMessage = await _desktop.LaunchFolderAsync(_log.Directory) ? null : Loc.Get("Shell.ExplorerFailed");
    }

    [RelayCommand]
    private void ClearLogs() => GeneralMessage = Loc.Get(_log.Clear() ? "Settings.LogCleared" : "Settings.LogClearFailed");

    // ---- About ----

    public string VersionText { get; }

    [RelayCommand]
    private void OpenLicenses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        try
        {
            GeneralMessage = null;
            _desktop.OpenTextFile(path);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            GeneralMessage = Loc.Get("Settings.LicensesFailed");
        }
    }
}

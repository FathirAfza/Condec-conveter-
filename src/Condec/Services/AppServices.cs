// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using Condec.Core.Conversion;
using Condec.Core.History;
using Condec.Core.Logging;
using Condec.Core.Pipeline;
using Condec.Core.Settings;
using Condec.ViewModels;

namespace Condec.Services;

/// <summary>
/// What the pages share, built once at start. The view models live here rather than in the pages, so a page that is
/// opened again finds its conversion, its choices and its progress as it left them.
/// </summary>
public sealed class AppServices
{
    public AppServices(
        AppSettings settings,
        ActivityLog log,
        ConverterRegistry registry,
        ConversionPipeline pipeline,
        HistoryStore history,
        IDesktopServices desktop,
        ConverterRegistry upscaleRegistry,
        ConversionPipeline upscalePipeline,
        string version)
    {
        Settings = settings;
        Log = log;
        Desktop = desktop;
        Convert = new ConverterViewModel(ConverterScope.Files, registry, pipeline, history, desktop, log);
        Architecture = new ConverterViewModel(ConverterScope.Cad, registry, pipeline, history, desktop, log);
        Upscale = new UpscaleViewModel(settings, upscaleRegistry, upscalePipeline, desktop, log);
        SettingsPage = new SettingsViewModel(settings, log, desktop, version);
    }

    public AppSettings Settings { get; }

    public ActivityLog Log { get; }

    public IDesktopServices Desktop { get; }

    public ConverterViewModel Convert { get; }

    public ConverterViewModel Architecture { get; }

    public UpscaleViewModel Upscale { get; }

    public SettingsViewModel SettingsPage { get; }
}

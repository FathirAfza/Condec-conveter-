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
        ConversionPipeline architecturePipeline,
        ConversionPipeline compressPipeline,
        string version)
    {
        Settings = settings;
        Log = log;
        Desktop = desktop;
        Convert = new ConverterViewModel(registry, pipeline, history, desktop, log);
        ToCad = new ToCadViewModel(settings, pipeline, architecturePipeline, upscalePipeline, new ConversionRun(history, desktop, log), desktop, log);
        FromCad = new FromCadViewModel(architecturePipeline, new ConversionRun(history, desktop, log), desktop, log);
        Upscale = new UpscaleViewModel(settings, upscaleRegistry, upscalePipeline, history, desktop, log);
        Compress = new CompressViewModel(compressPipeline, new ConversionRun(history, desktop, log) { RecordsCompression = true }, desktop, log);
        SettingsPage = new SettingsViewModel(settings, log, desktop, version);
    }

    public AppSettings Settings { get; }

    public ActivityLog Log { get; }

    public IDesktopServices Desktop { get; }

    public ConverterViewModel Convert { get; }

    /// <summary>Architecture, "Gambar, PDF, DXF → DWG" (DESIGN §6.3.1).</summary>
    public ToCadViewModel ToCad { get; }

    /// <summary>Architecture, "DWG, DXF → Gambar, PDF" (DESIGN §6.3.2).</summary>
    public FromCadViewModel FromCad { get; }

    public UpscaleViewModel Upscale { get; }

    /// <summary>Compress Image (DESIGN §6.5).</summary>
    public CompressViewModel Compress { get; }

    public SettingsViewModel SettingsPage { get; }
}

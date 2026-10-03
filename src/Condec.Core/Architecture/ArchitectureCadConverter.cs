// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using Condec.Core.Cad;
using Condec.Core.Conversion;
using Condec.Core.Formats;
using Condec.Core.Localization;
using CSMath;

namespace Condec.Core.Architecture;

/// <summary>What to make of an analyzed picture: which kinds of object, and how big a pixel is.</summary>
/// <param name="Analysis">What the analysis found, with the shapes in analysis pixels.</param>
/// <param name="Include">The kinds of object that become CAD; the rest are left out.</param>
/// <param name="MillimetersPerPixel">Size of one analysis pixel on the real drawing: from the picture's resolution, or from a calibration.</param>
public sealed record ArchitectureCadOptions(DrawingAnalysis Analysis, IReadOnlySet<DrawingObjectKind> Include, double MillimetersPerPixel) : ConversionOptions;

/// <summary>Builds the CAD drawing of an analyzed picture (DESIGN §6.3.1): one layer per kind of object, in millimeters.</summary>
public static class ArchitectureCadBuilder
{
    public const string WallsLayer = "WALLS";
    public const string OpeningsLayer = "OPENINGS";
    public const string TextLayer = "TEXT";
    public const string LogoLayer = "LOGO";
    public const string TableLayer = "TABLE";

    /// <summary>The resolution assumed when the file doesn't state a usable one: the Windows default, 96 pixels per inch.</summary>
    public const double DefaultDpi = 96;

    public const double MillimetersPerInch = 25.4;

    /// <summary>The layer a kind of object is drawn on.</summary>
    public static string LayerName(DrawingObjectKind kind) => kind switch
    {
        DrawingObjectKind.Walls => WallsLayer,
        DrawingObjectKind.Openings => OpeningsLayer,
        DrawingObjectKind.Text => TextLayer,
        DrawingObjectKind.Logo => LogoLayer,
        _ => TableLayer,
    };

    /// <summary>Size of one analysis pixel from the picture's own resolution (96 dpi when it states none).</summary>
    public static double MillimetersPerPixelFromDpi(DrawingAnalysis analysis)
    {
        var dpi = (double.IsFinite(analysis.DpiX) && analysis.DpiX > 0) ? analysis.DpiX : DefaultDpi / analysis.Reduction;
        return MillimetersPerInch / Math.Clamp(dpi, 1, 2400);
    }

    /// <summary>The drawing, and how many entities it holds.</summary>
    public static (CadDocument Cad, int EntityCount) Build(ArchitectureCadOptions options)
    {
        var analysis = options.Analysis;
        var scale = options.MillimetersPerPixel;
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "The pixel size must be a positive number.");
        }

        var cad = new CadDocument(PdfToCadConverter.OutputVersion);
        cad.Header.InsUnits = UnitsType.Millimeters;

        var count = 0;
        foreach (var group in analysis.Groups.Where(g => options.Include.Contains(g.Kind)))
        {
            var layer = EnsureLayer(cad, LayerName(group.Kind), group.Kind);
            foreach (var item in group.Items)
            {
                foreach (var primitive in item.Primitives)
                {
                    foreach (var entity in ToEntities(primitive, scale))
                    {
                        entity.Layer = layer;
                        cad.Entities.Add(entity);
                        count++;
                    }
                }

                foreach (var line in item.Texts)
                {
                    if (line.Text is null)
                    {
                        continue;
                    }

                    var height = line.Bounds.Height * scale;
                    cad.Entities.Add(new TextEntity(line.Text)
                    {
                        // Bounds are y down; the drawing's y runs up from the bottom of the picture.
                        InsertPoint = new XYZ(line.Bounds.Left * scale, (analysis.Height - line.Bounds.Bottom) * scale, 0),
                        Height = height,
                        Layer = layer,
                    });
                    count++;
                }
            }
        }

        if (count == 0)
        {
            throw new NothingToTraceException("There is nothing selected to convert.");
        }

        PdfToCadConverter.SetExtents(cad);
        return (cad, count);
    }

    private static Layer EnsureLayer(CadDocument cad, string name, DrawingObjectKind kind)
    {
        if (cad.Layers.TryGetValue(name, out var existing))
        {
            return existing;
        }

        // AutoCAD colors: white/black for walls, then green, yellow, magenta and blue so the layers read apart.
        var layer = new Layer(name)
        {
            Color = new Color(kind switch
            {
                DrawingObjectKind.Walls => (short)7,
                DrawingObjectKind.Openings => (short)3,
                DrawingObjectKind.Text => (short)2,
                DrawingObjectKind.Logo => (short)6,
                _ => (short)5,
            }),
        };
        cad.Layers.Add(layer);
        return layer;
    }

    private static IEnumerable<Entity> ToEntities(DrawingPrimitive primitive, double scale)
    {
        switch (primitive)
        {
            case PolylinePrimitive { Points.Count: < 2 }:
                yield break;
            case PolylinePrimitive { IsLine: true } line:
                yield return new Line(
                    new XYZ(line.Points[0].X * scale, line.Points[0].Y * scale, 0),
                    new XYZ(line.Points[1].X * scale, line.Points[1].Y * scale, 0));
                break;
            case PolylinePrimitive polyline:
                yield return new LwPolyline(polyline.Points.Select(p => (IVector)new XY(p.X * scale, p.Y * scale))) { IsClosed = polyline.IsClosed };
                break;
            case ArcPrimitive arc:
                yield return new Arc
                {
                    Center = new XYZ(arc.CenterX * scale, arc.CenterY * scale, 0),
                    Radius = arc.Radius * scale,
                    StartAngle = arc.StartAngle,
                    EndAngle = arc.EndAngle,
                };
                break;
            case CirclePrimitive circle:
                yield return new Circle
                {
                    Center = new XYZ(circle.CenterX * scale, circle.CenterY * scale, 0),
                    Radius = circle.Radius * scale,
                };
                break;
        }
    }
}

/// <summary>
/// An analyzed picture (PNG, JPG, HEIC, HEIF, or a scanned PDF page) to DXF or DWG: lines, polylines, arcs and text on the
/// layers WALLS, OPENINGS and TEXT (DESIGN §6.3.1). The analysis happens before, on the Architecture page, because the user
/// looks at what was found and chooses what becomes CAD; this converter turns that choice into the file.
/// </summary>
/// <remarks>
/// A DWG goes through a DXF first: the drawing is written as DXF, read back, and that DXF is converted to DWG, the same
/// steps as converting the DXF file yourself.
/// </remarks>
public sealed class ArchitectureToCadConverter : IConverter
{
    private static readonly HashSet<string> Sources = [".png", ".jpg", ".jpeg", ".heic", ".heif", ".pdf"];

    public IReadOnlyList<string> GetTargets(string sourceExtension) =>
        Sources.Contains(FileExtension.Normalize(sourceExtension)) ? [".dwg", ".dxf"] : [];

    public async Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var options = request.Options as ArchitectureCadOptions
            ?? throw new ArgumentException("Converting a picture to CAD needs the analysis and the chosen objects.", nameof(request));
        var target = FileExtension.Normalize(request.TargetExtension);

        progress.Report(new ConversionProgress(ConversionStage.Decode, 0, Loc.Get("Progress.BuildingCadLayers")));
        var (cad, entityCount) = await Task.Run(() => ArchitectureCadBuilder.Build(options), ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Decode, 1));
        ct.ThrowIfCancellationRequested();

        progress.Report(new ConversionProgress(ConversionStage.Encode, 0, Loc.Format("Progress.WritingCadObjects", entityCount)));
        var bytes = CadFiles.Write(cad, ".dxf");
        if (target == ".dwg")
        {
            progress.Report(new ConversionProgress(ConversionStage.Encode, 0.5, Loc.Get("Progress.ConvertingDxfToDwg")));
            bytes = CadFiles.Write(CadFiles.ReadDxf(bytes), ".dwg");
        }

        await request.Output.WriteAsync(bytes, ct).ConfigureAwait(false);
        progress.Report(new ConversionProgress(ConversionStage.Encode, 1));
    }
}

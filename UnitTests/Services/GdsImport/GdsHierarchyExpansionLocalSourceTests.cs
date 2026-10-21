using CAP.Avalonia.Commands;
using System.Diagnostics;
using System.Globalization;
using CAP_Core.Components.Core;
using CAP_DataAccess.Import.Gds;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Services.GdsImport;

/// <summary>Opt-in private-file probe; no source data or absolute path is checked in.</summary>
public class GdsHierarchyExpansionLocalSourceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DefaultImportCanExposeLargeAssemblyFromEmbeddedSource()
    {
        var source = Environment.GetEnvironmentVariable("LUNIMA_GDS_EXPANSION_SOURCE");
        if (string.IsNullOrWhiteSpace(source)) return;
        using var host = new GdsDesignScopeTestHost();
        var analysis = await GdsImportService.AnalyzeAsync(source);
        var outcome = await host.CreateService().ImportAsync(source, analysis.TopCellCandidates.First(),
            preParsedLibrary: analysis.Library, sourceBytes: analysis.SourceBytes);
        var plan = GdsPlacementPlan.FromOutcome(outcome);
        var requested = Environment.GetEnvironmentVariable("LUNIMA_GDS_EXPANSION_CELL");
        var instruction = requested is not null ? plan.Placements.First(p => p.CellName == requested)
            : plan.Placements.OrderByDescending(p => analysis.Library!.Cells[p.CellName].Elements.OfType<GdsReference>().Count()).First();
        var template = host.Templates.Single(t => t.Name == instruction.ComponentIdentifier && t.PdkSource == instruction.PdkSource);
        var canvas = new DesignCanvasViewModel();
        var placement = PlaceComponentCommand.CreateExact(canvas, template, instruction.XUm, instruction.YUm,
            exactRotationDegrees: instruction.RotationDegrees, mirrorPinsHorizontally: instruction.Reflected);
        placement.Execute();
        output.WriteLine($"Selected {instruction.CellName}; {template.PinDefinitions.Length} ports.");
        var original = placement.CreatedViewModel!;
        var originalPolygons = Fingerprints([original.Component]);
        var originalPorts = original.Component.PhysicalPins.Select(Port).ToList();
        var service = new GdsHierarchyExpansionService(host.Scope);
        var metrics = new List<string>();
        var clock = Stopwatch.StartNew();
        var command = await service.PrepareAsync(canvas, original);
        metrics.Add($"Prepare: {clock.ElapsedMilliseconds} ms");
        output.WriteLine(metrics[^1]);
        clock.Restart();
        command.Execute();
        metrics.Add($"Execute: {clock.ElapsedMilliseconds} ms; exposed {command.Children.Count} source/own-geometry components.");
        output.WriteLine(metrics[^1]);
        Fingerprints(canvas.Components.Select(c => c.Component)).ShouldBe(originalPolygons);
        var exposedPorts = canvas.Components.SelectMany(c => c.Component.PhysicalPins).Select(Port).ToHashSet();
        foreach (var port in originalPorts) exposedPorts.ShouldContain(port);
        clock.Restart();
        canvas.Components.Count.ShouldBeGreaterThan(1);
        command.Undo();
        metrics.Add($"Undo: {clock.ElapsedMilliseconds} ms");
        output.WriteLine(metrics[^1]);
        if (Environment.GetEnvironmentVariable("LUNIMA_GDS_EXPANSION_REPORT") is { Length: > 0 } report)
            await File.WriteAllLinesAsync(report, metrics);
        canvas.Components.ShouldHaveSingleItem().ShouldBeSameAs(placement.CreatedViewModel);
        Fingerprints([original.Component]).ShouldBe(originalPolygons);
    }

    private static string Port(PhysicalPin pin)
    {
        var point = pin.GetAbsolutePosition();
        return FormattableString.Invariant($"{pin.Name}:{point.x:F6}:{point.y:F6}:{pin.GetAbsoluteAngle():F6}:{pin.MatterType}:{pin.Layer}:{pin.WaveguideWidthMicrometers}");
    }

    private static List<string> Fingerprints(IEnumerable<Component> components) => components.SelectMany(component =>
    {
        var width = component.UnrotatedWidthMicrometers > 0 ? component.UnrotatedWidthMicrometers : component.WidthMicrometers;
        var height = component.UnrotatedHeightMicrometers > 0 ? component.UnrotatedHeightMicrometers : component.HeightMicrometers;
        var radians = component.RotationDegrees * Math.PI / 180;
        return component.OutlinePolygons!.Select(polygon => polygon.Layer + ":" + polygon.DataType + ":" +
            string.Join(";", polygon.Points.Select(point =>
            {
                var x = point.X - width / 2;
                var y = (point.Y - height / 2) * (component.IsMirroredHorizontally ? -1 : 1);
                var worldX = component.PhysicalX + component.WidthMicrometers / 2 + x * Math.Cos(radians) - y * Math.Sin(radians);
                var worldY = component.PhysicalY + component.HeightMicrometers / 2 + x * Math.Sin(radians) + y * Math.Cos(radians);
                return worldX.ToString("F6", CultureInfo.InvariantCulture) + "," + worldY.ToString("F6", CultureInfo.InvariantCulture);
            }).Order(StringComparer.Ordinal)));
    }).Order(StringComparer.Ordinal).ToList();
}

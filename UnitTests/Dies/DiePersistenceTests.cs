using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Commands.Dies;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Integration;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>
/// .lun round trip of dies, and loading of files saved before dies existed: shipped
/// multi-chiplet examples become die assemblies, single-chip designs stay untouched.
/// </summary>
public class DiePersistenceTests
{
    [Fact]
    public async Task DieAssembly_RoundTripsThroughTheLunFile()
    {
        var path = TempLun();
        try
        {
            var canvas = new DesignCanvasViewModel();
            canvas.SetRootChipSize(3000, 2000);
            AddDemoPart(canvas, "Straight Waveguide 100µm", 100, 100);
            new CommandManager().ExecuteCommand(new CreateDieCommand(canvas,
                new DieSpec("Gain die", null, 1000, 2000, new[] { "Demo PDK" }),
                new DieSpec("Die 1", null, 3000, 2000)));
            (await NewFileOps(canvas).SaveDesignToPathAsync(path)).ShouldBeTrue();

            var reloaded = new DesignCanvasViewModel();
            var fileOps = NewFileOps(reloaded);
            (await fileOps.LoadDesignFromPathAsync(path)).ShouldBeTrue();
            await fileOps.PostLoadRouting;

            reloaded.Dies.IsAssemblyRoot.ShouldBeTrue();
            var gain = reloaded.Dies.RootDies.Single(d => d.GroupName == "Gain die");
            gain.Die!.EnabledPdkNames.ShouldBe(new[] { "Demo PDK" });
            DieAssembly.OutlineOf(gain).ShouldBe(new DieOutline(3500, 0, 1000, 2000));
            reloaded.Dies.RootDies.Single(d => d.GroupName == "Die 1").ChildComponents.Count.ShouldBe(1);
            reloaded.Dies.RootChipWidthMicrometers.ShouldBe(3000);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SingleDieDesign_SavesNoDieData()
    {
        var path = TempLun();
        try
        {
            var canvas = new DesignCanvasViewModel();
            canvas.SetRootChipSize(3000, 2000);
            AddDemoPart(canvas, "Straight Waveguide 100µm", 100, 100);
            var group = new ComponentGroup("cell");
            group.AddChild(ComponentTemplates.CreateFromTemplate(Template("Edge Coupler"), 500, 100));
            canvas.AddComponent(group);

            (await NewFileOps(canvas).SaveDesignToPathAsync(path)).ShouldBeTrue();

            var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
            json.ToJsonString().ShouldNotContain("\"Die\"");
            json["ChipWidthMicrometers"]!.GetValue<double>().ShouldBe(3000);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ShippedTwoChipletExample_LoadsAsTwoDies()
    {
        var canvas = new DesignCanvasViewModel();

        await TwoChipletsExampleLoader.LoadAsync(canvas, "Two Chiplets - Edge-Coupler Link.lun");

        canvas.Dies.IsAssemblyRoot.ShouldBeTrue();
        canvas.Dies.RootDies.Count.ShouldBe(2);
        new CAP_Core.Analysis.DieAssemblyChecker()
            .Check(canvas.Components.Select(c => c.Component), canvas.Connections.Select(c => c.Connection))
            .ShouldBeEmpty("the upgraded example is a clean assembly");
        canvas.Connections.Count(c => c.Connection.IsCrossChipletFacetLink).ShouldBe(1);
        canvas.Connections.Where(c => !c.Connection.IsCrossChipletFacetLink)
            .ShouldAllBe(c => CAP.Avalonia.ViewModels.Canvas.DieSurface.DieWireStyle.IsOnOneDie(c.Connection),
                "every other wire lives on its die, editable in place");
        canvas.Dies.RootDies.ShouldAllBe(d => d.InternalPaths.All(p => p.StartPin == null));
    }

    private static readonly List<ComponentTemplate> DemoTemplates = new[]
    {
        "Grating Coupler", "Straight Waveguide 100µm", "Edge Coupler",
    }.Select(Template).ToList();

    private static ComponentTemplate Template(string name) =>
        UnitTests.Components.MultiProcessChipletJourneyDesign.TemplateFor(
            UnitTests.Components.MultiProcessChipletJourneyDesign.LoadPdk(ChipletEdgeCouplerJourneyDesign.DemoPdkFile), name);

    private static void AddDemoPart(DesignCanvasViewModel canvas, string templateName, double x, double y)
    {
        var template = Template(templateName);
        canvas.AddComponent(ComponentTemplates.CreateFromTemplate(template, x, y), template.Name, template.PdkSource);
    }

    private static string TempLun() => Path.Combine(Path.GetTempPath(), $"die-test-{Guid.NewGuid():N}.lun");

    private static FileOperationsViewModel NewFileOps(DesignCanvasViewModel canvas) =>
        new(canvas, new CommandManager(), new SimpleNazcaExporter(), new SaxExporter(),
            new ObservableCollection<ComponentTemplate>(DemoTemplates),
            new GdsExportViewModel(new GdsExportService()),
            new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas),
            null!, errorConsole: new ErrorConsoleService());
}

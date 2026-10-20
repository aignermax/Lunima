using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

public class GdsInterpretationPlacementTests
{
    [Fact]
    public async Task ReviewedGroupCollectsOnlyItsSourceMembers()
    {
        var canvas = new DesignCanvasViewModel();
        var template = Template();
        var executor = new GdsPlacementExecutor(canvas, new CommandManager(), () => [template]);
        await executor.ExecuteAsync(new()
        {
            Placements = [Placement("a", 0), Placement("a", 20), Placement("b", 40)],
            Groups = [new() { Name = "stage", CellNames = ["a"] }]
        }, rerouteImportedConnections: false, groupImport: false);
        canvas.Components.Count.ShouldBe(2);
        var group = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();
        group.GroupName.ShouldBe("stage");
        group.ChildComponents.Count.ShouldBe(2);
        canvas.Components.Single(c => c.Component is not ComponentGroup).Component.PhysicalX.ShouldBe(40);
    }

    [Fact]
    public async Task SimulationRejectsUnmodeledImportedPorts()
    {
        var canvas = new DesignCanvasViewModel();
        var template = PdkTemplateConverter.ConvertToTemplate(new()
        {
            Name = "unmodeled",
            ImportedModelStatus = "geometry-only",
            WidthMicrometers = 10,
            HeightMicrometers = 2,
            Pins = [new() { Name = "input" }, new() { Name = "output", OffsetXMicrometers = 10 }]
        }, "review", null);
        var executor = new GdsPlacementExecutor(canvas, null, () => [template]);
        await executor.ExecuteAsync(new()
        {
            Placements = [new() { ComponentIdentifier = template.Name, PdkSource = "review" }]
        }, groupImport: false);
        var result = await new SimulationService().RunAsync(canvas);
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("geometry-only");
    }

    private static GdsPlacementInstruction Placement(string cell, double x) => new()
    {
        CellName = cell,
        InstanceName = cell + x,
        ComponentIdentifier = "shape",
        PdkSource = "test",
        XUm = x
    };

    private static ComponentTemplate Template() => new()
    {
        Name = "shape",
        PdkSource = "test",
        WidthMicrometers = 10,
        HeightMicrometers = 2,
        PinDefinitions = [],
        CreateSMatrix = pins => new CAP_Core.LightCalculation.SMatrix([], []),
    };
}

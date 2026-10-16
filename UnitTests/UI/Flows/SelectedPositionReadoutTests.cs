using Avalonia;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI.Flows;

[Trait("Category", "UiFlows")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class SelectedPositionReadoutTests
{
    [AvaloniaFact]
    public async Task ReadOnlyPositionLabels_DoNotRoundSelectedCoordinates()
    {
        using var host = new UiFlowTestHost();
        var part = await SelectGate(host);
        part.X = 400.123456789;
        part.Y = 100.987654321;
        UiInput.RunJobs();
        part.X.ShouldBe(400.123456789);
        part.Y.ShouldBe(100.987654321);
    }

    [AvaloniaFact]
    public async Task SelectedGateDrag_UndoRestoresExactModelAndViewModelCoordinates()
    {
        using var host = new UiFlowTestHost();
        var part = await SelectGate(host);
        var before = (part.Component.PhysicalX, part.Component.PhysicalY);
        var node = host.Vm.GateView.Graph.Nodes.Single(n => n.Group == part.Component);
        var world = GateSymbolGeometry.Place(node, ComponentGroupRenderer.CalculateGroupBounds(node.Group)).Body.Center;
        var canvas = UiInput.Descendants<DesignCanvas>(host.Window).First();
        var point = canvas.TranslatePoint(new Point(world.X * canvas.Zoom + host.Vm.Canvas.PanX,
            world.Y * canvas.Zoom + host.Vm.Canvas.PanY), host.Window)!.Value;
        UiInput.DragMouse(host.Window, point, point + new Vector(30, 0));
        part.Component.PhysicalX.ShouldNotBe(before.PhysicalX);
        host.Vm.CommandManager.Undo();
        (part.Component.PhysicalX, part.Component.PhysicalY).ShouldBe(before);
        (part.X, part.Y).ShouldBe(before);
    }

    private static async Task<ComponentViewModel> SelectGate(UiFlowTestHost host)
    {
        (await host.Vm.FileOperations.OpenDesignAsCopyAsync(Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Half Adder.lun"))).ShouldBeTrue();
        await host.Vm.FileOperations.PostLoadRouting;
        await host.Vm.GateView.RebuildNowAsync();
        UiInput.PressKey(host.Window, Avalonia.Input.Key.F);
        var part = host.Vm.Canvas.Components.Single(c => c.Component is ComponentGroup { GroupName: "NAND1A" });
        host.Vm.Canvas.Selection.SelectSingle(part);
        UiInput.RunJobs();
        return part;
    }
}

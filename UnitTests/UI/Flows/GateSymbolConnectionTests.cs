using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.Rendering;
using CAP.Avalonia.ViewModels.Logic.GateView.Layout;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI.Flows;

[Trait("Category", "UiFlows")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class GateSymbolConnectionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectShortcut_JoinsDisplayedTerminals_AndUndoRestoresWires(bool reverse)
    {
        using var host = new UiFlowTestHost();
        await Open(host);
        var vm = host.Vm;
        var source = Symbol(host, "NOT1");
        var target = Symbol(host, "NAND4");
        var before = vm.Canvas.Connections.Select(c => c.Connection.Id).Order().ToArray();
        var start = Pin(source, true);
        var end = Pin(target, false);
        UiInput.PressKey(host.Window, Key.C);
        vm.GateView.IsShown.ShouldBeTrue("C must edit the displayed gate terminals");

        var from = Screen(host, source.OutputPins[0]);
        var to = Screen(host, target.InputPins[0]);
        UiInput.DragMouse(host.Window, reverse ? to : from, reverse ? from : to);

        vm.GateView.IsShown.ShouldBeTrue();
        vm.Canvas.Connections.ShouldContain(c => c.Connection.StartPin == start && c.Connection.EndPin == end);
        vm.CommandManager.Undo();
        vm.Canvas.Connections.Select(c => c.Connection.Id).Order().ShouldBe(before);
    }

    [AvaloniaFact]
    public async Task TwoInputTerminals_AreRefusedWithoutChangingWiresOrUndo()
    {
        using var host = new UiFlowTestHost();
        await Open(host);
        var vm = host.Vm;
        var before = vm.Canvas.Connections.Select(c => c.Connection.Id).ToArray();
        var undo = vm.CommandManager.UndoCount;
        UiInput.PressKey(host.Window, Key.C);
        vm.GateView.IsShown.ShouldBeTrue();
        UiInput.DragMouse(host.Window, Screen(host, Symbol(host, "NOT1").InputPins[0]),
            Screen(host, Symbol(host, "NAND4").InputPins[0]));
        vm.Canvas.Connections.Select(c => c.Connection.Id).ShouldBe(before);
        vm.CommandManager.UndoCount.ShouldBe(undo);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompatiblePorts_AreRefusedWithoutChangingWires(bool electrical)
    {
        using var host = new UiFlowTestHost();
        await Open(host);
        var vm = host.Vm;
        var source = Symbol(host, "NOT1");
        var target = Symbol(host, "NAND4");
        if (electrical) Pin(target, false).LogicalPin.MatterType = MatterType.Electricity;
        else Pin(target, false).LogicalPin.Polarization = PolarizationKind.TM;
        var before = vm.Canvas.Connections.Select(c => c.Connection.Id).ToArray();
        var undo = vm.CommandManager.UndoCount;
        UiInput.PressKey(host.Window, Key.C);
        vm.GateView.IsShown.ShouldBeTrue();
        UiInput.DragMouse(host.Window, Screen(host, source.OutputPins[0]), Screen(host, target.InputPins[0]));
        vm.Canvas.Connections.Select(c => c.Connection.Id).ShouldBe(before);
        vm.CommandManager.UndoCount.ShouldBe(undo);
    }

    [AvaloniaFact]
    public async Task CancelledWire_LeavesConnectionsIntact_ThenGateCanStillBeDragged()
    {
        using var host = new UiFlowTestHost();
        await Open(host);
        var vm = host.Vm;
        var source = Symbol(host, "NAND1A");
        var before = vm.Canvas.Connections.Select(c => c.Connection.Id).ToArray();
        UiInput.PressKey(host.Window, Key.C);
        vm.Canvas.Connections.ShouldContain(c => c.Connection.StartPin == Pin(source, true) || c.Connection.EndPin == Pin(source, true));
        var from = Screen(host, source.OutputPins[0]);
        var to = Screen(host, source.Body.Center + new Vector(0, 600));
        host.Window.MouseMove(from);
        host.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        host.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        UiInput.RunJobs();
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)host.Window.Bounds.Width,
                   (int)host.Window.Bounds.Height), new Vector(96, 96)))
        {
            bitmap.Render(host.Window);
            ScreenshotArtifacts.SavePng(bitmap, Path.Combine(Path.GetTempPath(), "lunima-gate-connect-preview.png"));
        }
        host.Window.MouseUp(to, MouseButton.Left);
        UiInput.RunJobs();
        vm.Canvas.Connections.Select(c => c.Connection.Id).ShouldBe(before);
        UiInput.PressKey(host.Window, Key.S);
        var group = source.Node.Group;
        var x = group.PhysicalX;
        var center = Screen(host, source.Body.Center);
        UiInput.DragMouse(host.Window, center, center + new Vector(30, 0));
        group.PhysicalX.ShouldNotBe(x);
        vm.GateView.IsShown.ShouldBeTrue();

    }

    [AvaloniaFact]
    public async Task GatesOnDifferentDies_RefuseWire_KeepingExistingConnections()
    {
        using var host = new UiFlowTestHost();
        await Open(host);
        var vm = host.Vm;
        foreach (var id in new[] { "NOT1", "NAND4" })
        {
            var gate = vm.Canvas.Components.Single(c => c.Component == Symbol(host, id).Node.Group);
            vm.Canvas.Components.Remove(gate);
            var die = new ComponentGroup(id + " die") { Die = new CAP_Core.Components.Dies.DieDefinition(3000, 3000) };
            die.AddChild(gate.Component);
            vm.Canvas.Components.Add(new CAP.Avalonia.ViewModels.Canvas.ComponentViewModel(die));
        }
        await vm.GateView.RebuildNowAsync();
        UiInput.PressKey(host.Window, Key.F);
        var source = Symbol(host, "NOT1");
        var target = Symbol(host, "NAND4");
        var before = vm.Canvas.Connections.Select(c => c.Connection.Id).ToArray();
        var undo = vm.CommandManager.UndoCount;
        UiInput.PressKey(host.Window, Key.C);
        UiInput.DragMouse(host.Window, Screen(host, source.OutputPins[0]), Screen(host, target.InputPins[0]));
        vm.Canvas.Connections.Select(c => c.Connection.Id).ShouldBe(before);
        vm.CommandManager.UndoCount.ShouldBe(undo);
    }

    [AvaloniaFact]
    public async Task LostPointerCapture_CancelsDragWithoutCommittingOnLaterRelease()
    {
        using var host = new UiFlowTestHost();
        await Open(host);
        var vm = host.Vm;
        var canvas = UiInput.Descendants<DesignCanvas>(host.Window).First();
        IPointer? pointer = null;
        canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        var before = vm.Canvas.Connections.Select(c => c.Connection.Id).ToArray();
        var from = Screen(host, Symbol(host, "NAND1A").OutputPins[0]);
        var to = Screen(host, Symbol(host, "NAND4").InputPins[0]);
        UiInput.PressKey(host.Window, Key.C);
        host.Window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        pointer.ShouldNotBeNull().Captured.ShouldBe(canvas);
        host.Window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        pointer.Capture(null);
        host.Window.MouseUp(to, MouseButton.Left);
        UiInput.RunJobs();
        vm.Canvas.Connections.Select(c => c.Connection.Id).ShouldBe(before);
    }

    private static async Task Open(UiFlowTestHost host)
    {
        (await host.Vm.FileOperations.OpenDesignAsCopyAsync(Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate Half Adder.lun"))).ShouldBeTrue();
        await host.Vm.FileOperations.PostLoadRouting;
        await host.Vm.GateView.RebuildNowAsync();
        UiInput.PressKey(host.Window, Key.F);
        UiInput.RunJobs();
    }

    private static GateSymbolPlacement Symbol(UiFlowTestHost host, string id)
    {
        var node = host.Vm.GateView.Graph.Nodes.Single(n => n.Group.GroupName == id);
        return GateSymbolGeometry.Place(node, ComponentGroupRenderer.CalculateGroupBounds(node.Group));
    }

    private static PhysicalPin Pin(GateSymbolPlacement symbol, bool output)
    {
        var name = (output ? symbol.Node.Outputs : symbol.Node.Inputs)[0].Name;
        return symbol.Node.Group.ExternalPins.First(p => p.Name == name).InternalPin!;
    }

    private static Point Screen(UiFlowTestHost host, Point world)
    {
        var canvas = UiInput.Descendants<DesignCanvas>(host.Window).First();
        return canvas.TranslatePoint(new Point(world.X * canvas.Zoom + host.Vm.Canvas.PanX,
            world.Y * canvas.Zoom + host.Vm.Canvas.PanY), host.Window)!.Value;
    }
}

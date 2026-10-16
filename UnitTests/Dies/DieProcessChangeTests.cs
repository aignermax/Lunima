using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Dies;
using CAP_Core.Components.Process;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Components.Dies;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

public class DieProcessChangeTests
{
    private static readonly ProcessGroup Target = new("InP", new ProcessFingerprint("InP", 500, "InP", 1550, "InP"), new[] { "inp" });

    [Fact]
    public void ProcessChange_RemovesIncidentFacetLink_AndUndoRestoresIt()
    {
        var canvas = new DesignCanvasViewModel();
        var commands = new CommandManager();
        var left = EdgeCoupler("left", 900, 200);
        DieEdgeSnapper.Apply(left, DieEdgeSnapper.Plan(left, new DieOutline(0, 0, 1000, 600), 50)!);
        var right = EdgeCoupler("right", 1002, 200);
        var die = Die("left die", 0, 0, 1000, 600, Process("SiN", "SiN", "sin"), left);
        var other = Die("right die", 1002, 0, 1000, 600, null, right);
        canvas.AddComponent(die);
        canvas.AddComponent(other);
        canvas.RefreshDieInterfaces();
        var wire = canvas.ConnectPins(left.PhysicalPins[0], right.PhysicalPins[0])!;
        wire.Connection.IsCrossChipletFacetLink.ShouldBeTrue();
        var props = new DiePropertiesViewModel(canvas, commands, () => new[] { Target }) { ResolvePdkSource = _ => "sin" };
        props.Show(die);
        props.SelectedProcess = props.ProcessOptions.Single(p => p.Name == "InP");
        props.ProcessPreviewText.ShouldContain("left.fiber");
        props.ConfirmProcessChangeCommand.Execute(null);
        canvas.Connections.ShouldBeEmpty();
        other.ChildComponents.ShouldContain(right);
        commands.Undo();
        canvas.Connections.ShouldHaveSingleItem().Connection.ShouldBe(wire.Connection);
        wire.Connection.IsCrossChipletFacetLink.ShouldBeTrue();
        die.ChildComponents.ShouldContain(left);
    }

    [Fact]
    public void OverlappingPopulatedDie_ReportsRefusalWithoutMutation()
    {
        var canvas = new DesignCanvasViewModel();
        var commands = new CommandManager();
        var before = Process("SiN", "SiN", "sin");
        var die = Die("Die 1", 0, 0, 1000, 1000, before, Part("bad", 50, 50));
        canvas.AddComponent(die);
        canvas.AddComponent(Die("Die 2", 500, 0, 1000, 1000));
        var props = new DiePropertiesViewModel(canvas, commands, () => new[] { Target });
        props.Show(die);
        props.SelectedProcess = props.ProcessOptions.Single(p => p.Name == "InP");
        props.StatusText.ShouldNotBeNullOrEmpty();
        props.HasProcessPreview.ShouldBeFalse();
        die.ProcessBinding.ShouldBe(before);
        commands.UndoCount.ShouldBe(0);
    }

    [Fact]
    public void ExternalReload_CancelsStalePreview()
    {
        var canvas = new DesignCanvasViewModel();
        var commands = new CommandManager();
        var before = Process("SiN", "SiN", "sin");
        var die = Die("Die 1", 0, 0, 1000, 1000, before, Part("bad", 50, 50));
        canvas.AddComponent(die);
        var props = new DiePropertiesViewModel(canvas, commands, () => new[] { Target });
        props.Show(die);
        props.SelectedProcess = props.ProcessOptions.Single(p => p.Name == "InP");
        props.Reload();
        props.ConfirmProcessChangeCommand.Execute(null);
        die.ProcessBinding.ShouldBe(before);
        commands.UndoCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Confirm_RemovesOnlyIncompatibleLeavesAndWires_OneUndoRestores(bool proxy)
    {
        var canvas = new DesignCanvasViewModel();
        canvas.SetRootChipSize(2000, 2000);
        var commands = new CommandManager();
        var bad = Part("bad", 50, 50);
        var good = Part("good", 150, 50);
        var unknown = Part("unknown", 250, 50);
        var group = new ComponentGroup("nested");
        group.AddChildren(new[] { bad, good, unknown });
        var exposed = new GroupPin { Name = "bad-port", InternalPin = bad.PhysicalPins[0] };
        group.AddExternalPin(exposed);
        group.SyncPhysicalPinsFromExternalPins();
        var boundaryPin = group.PhysicalPins.Single();
        var outer = new ComponentGroup("outer");
        outer.AddChild(group);
        var ancestorPort = new GroupPin { Name = "outer-port", InternalPin = boundaryPin };
        outer.AddExternalPin(ancestorPort);
        outer.SyncPhysicalPinsFromExternalPins();
        var outerPin = outer.PhysicalPins.Single();
        var parentPath = new FrozenWaveguidePath { StartPin = boundaryPin, EndPin = unknown.PhysicalPins[0], Path = new RoutedPath() };
        outer.AddInternalPath(parentPath);
        var frozen = new FrozenWaveguidePath { StartPin = bad.PhysicalPins[1], EndPin = good.PhysicalPins[0], Path = new RoutedPath() };
        group.AddInternalPath(frozen);
        var die = Die("Die 1", 0, 0, 1000, 1000, Process("SiN", "SiN", "sin"), outer);
        canvas.AddComponent(proxy ? outer : die);
        if (proxy) die = canvas.ChipDie.Die;
        var outside = Part("outside", 1100, 50);
        canvas.AddComponent(outside);
        var wire = canvas.ConnectPins(outerPin, outside.PhysicalPins[0])!;
        var props = new DiePropertiesViewModel(canvas, commands, () => new[] { Target })
        {
            ResolvePdkSource = c => c == bad ? "sin" : c == good ? "fork" : null,
            ResolveAllowedPdkNames = _ => new[] { "inp", "fork" }
        };
        props.Show(die);
        props.SelectedProcess = props.ProcessOptions.Single(p => p.Name == "InP");
        props.HasProcessPreview.ShouldBeTrue();
        props.ProcessPreviewText.ShouldContain("bad");
        props.CancelProcessChangeCommand.Execute(null);
        group.ChildComponents.Count.ShouldBe(3);
        commands.UndoCount.ShouldBe(0);
        props.SelectedProcess = props.ProcessOptions.Single(p => p.Name == "InP");
        props.ConfirmProcessChangeCommand.Execute(null);
        group.ChildComponents.ShouldBe(new[] { good, unknown });
        group.InternalPaths.ShouldBeEmpty();
        group.ExternalPins.ShouldNotContain(exposed);
        group.PhysicalPins.ShouldBeEmpty();
        outer.ExternalPins.ShouldBeEmpty();
        outer.PhysicalPins.ShouldBeEmpty();
        outer.InternalPaths.ShouldBeEmpty();
        canvas.Connections.ShouldBeEmpty();
        props.Die!.ProcessBinding!.DisplayName.ShouldBe("InP");
        commands.UndoCount.ShouldBe(1);
        commands.Undo();
        group.ChildComponents.ShouldBe(new[] { bad, good, unknown });
        group.ExternalPins.ShouldContain(exposed);
        group.InternalPaths.ShouldContain(frozen);
        group.PhysicalPins.ShouldContain(boundaryPin);
        outer.ExternalPins.ShouldContain(ancestorPort);
        outer.PhysicalPins.ShouldContain(outerPin);
        outer.InternalPaths.ShouldContain(parentPath);
        canvas.Connections.ShouldHaveSingleItem().Connection.ShouldBe(wire.Connection);
        if (proxy) canvas.Dies.RootDies.ShouldBeEmpty();
        commands.Redo();
        group.ChildComponents.ShouldBe(new[] { good, unknown });
        canvas.Connections.ShouldBeEmpty();
    }

    [Fact]
    public void PopulatedDie_ProcessSelectionWaitsForConfirmation()
    {
        var canvas = new DesignCanvasViewModel();
        var before = Process("SiN", "SiN", "sin");
        var after = new ProcessGroup("InP", new ProcessFingerprint("InP", 500, "InP", 1550, "InP"), new[] { "inp" });
        var part = Part("existing", 50, 50);
        var die = Die("Die 1", 0, 0, 1000, 1000, before, part);
        canvas.AddComponent(die);
        var props = new DiePropertiesViewModel(canvas, new CommandManager(), () => new[] { after });
        props.Show(die);

        props.SelectedProcess = props.ProcessOptions.Single(p => p.Name == "InP");

        die.ProcessBinding.ShouldBe(before);
        die.ChildComponents.ShouldContain(part);
    }
}

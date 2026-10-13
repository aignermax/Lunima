using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Integration;

namespace UnitTests.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// Shared setup for the Make-gate tests: loads shipped examples through the real load
/// path and rebuilds a gate from loose parts the way a user does — ungroup, select
/// everything, Ctrl+G.
/// </summary>
internal static class GateKitFixture
{
    /// <summary>File name of the shipped NOT/NAND gate example.</summary>
    public const string NotNandExample = "Logic Gate NOT-NAND.lun";

    /// <summary>File name of the shipped AND-from-NAND gate example.</summary>
    public const string AndExample = "Logic Gate AND-from-NAND.lun";

    /// <summary>Loads a shipped example onto a fresh canvas.</summary>
    public static Task<DesignCanvasViewModel> LoadAsync(string fileName) =>
        LogicGateHalfAdderExampleTests.LoadCanvas(
            Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), fileName));

    /// <summary>Loads a shipped gate, ungroups it and groups the loose parts again (Ctrl+G).</summary>
    public static async Task<(DesignCanvasViewModel Canvas, ComponentViewModel Group)> RegroupedAsync(string fileName)
    {
        var canvas = await LoadAsync(fileName);
        var shipped = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().Single();
        new UngroupCommand(canvas, shipped).Execute();
        return (canvas, GroupEverything(canvas));
    }

    /// <summary>Loads the gate kit the "Your first gate" tour opens and groups all its parts.</summary>
    public static async Task<(DesignCanvasViewModel Canvas, ComponentViewModel Group)> GroupedKitAsync()
    {
        var canvas = await LoadAsync(FirstGateTourViewModel.GateKitExampleFileName);
        return (canvas, GroupEverything(canvas));
    }

    /// <summary>Selects every component and runs the real grouping command.</summary>
    public static ComponentViewModel GroupEverything(DesignCanvasViewModel canvas)
    {
        canvas.Components.Any(c => c.Component is ComponentGroup).ShouldBeFalse("only loose parts are grouped");
        var command = new CreateGroupCommand(canvas, canvas.Components.ToList());
        command.Execute();
        command.CreatedGroup.ShouldNotBeNull("grouping the loose parts must create a group");
        return canvas.Components.Single(c => ReferenceEquals(c.Component, command.CreatedGroup));
    }

    /// <summary>The external pin exposing <paramref name="componentId"/>.<paramref name="pinName"/>.</summary>
    public static GroupPin PinExposing(ComponentGroup group, string componentId, string pinName) =>
        group.ExternalPins.Single(p => p.InternalPin.ParentComponent.Identifier == componentId
                                       && p.InternalPin.Name == pinName);
}

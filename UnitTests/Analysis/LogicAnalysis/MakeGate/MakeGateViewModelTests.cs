using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// End-to-end tests of the Make-gate steps on a headless canvas built from the
/// shipped NOT/NAND parts (ungroup → Ctrl+G → Make gate → NAND) and the gate kit:
/// suggested roles, real simulation, automatic NAND threshold, save (pins renamed,
/// assignment persisted, undoable), and the Logic panel building the new gate.
/// </summary>
public class MakeGateViewModelTests
{
    [Fact]
    public async Task RegroupedNandParts_MakeGate_ReadsNand_AndBuildsInLogic()
    {
        var (canvas, groupVm) = await GateKitFixture.RegroupedAsync(GateKitFixture.NotNandExample);
        var makeGate = OpenFor(canvas, groupVm);

        await makeGate.SimulateCommand.ExecuteAsync(null);
        makeGate.HasResult.ShouldBeTrue(makeGate.StatusText);
        makeGate.Rows.Count.ShouldBe(4, "two inputs, one output");

        makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.Nand);
        makeGate.Threshold.ShouldBeInRange(0.1, 0.15, "the automatic NAND threshold sits in the 0.125 region");
        makeGate.HasOverlap.ShouldBeFalse();
        makeGate.MatchesFunction.ShouldBeTrue(makeGate.MatchText);
        makeGate.SuggestionText.ShouldContain("dB");

        makeGate.SaveCommand.Execute(null);
        makeGate.IsSaved.ShouldBeTrue(makeGate.StatusText);

        var group = (ComponentGroup)groupVm.Component;
        var assignment = group.TruthTablePinAssignment.ShouldNotBeNull();
        assignment.InputPinNames.ShouldBe(new[] { "A", "B" });
        assignment.OutputPinNames.ShouldBe(new[] { "Y" });
        assignment.BiasPinNames.ShouldBe(new[] { "BIAS" });
        group.GroupName.ShouldBe("NAND");
        GateKitFixture.PinExposing(group, "input_a", "a0").Name.ShouldBe("A");

        await AssertLogicPanelReadsNand(canvas);
    }

    [Fact]
    public async Task GateKit_MakeGate_ReadsNand()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = OpenFor(canvas, groupVm);

        await makeGate.SimulateCommand.ExecuteAsync(null);
        makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.Nand);

        makeGate.MatchesFunction.ShouldBeTrue(makeGate.MatchText);
        makeGate.Rows.Select(r => r.BitText).ShouldBe(new[] { "1", "1", "1", "0" });
        makeGate.SaveCommand.Execute(null);
        await AssertLogicPanelReadsNand(canvas);
    }

    [Fact]
    public async Task Open_PrefillsSuggestedRolesAndShortNames()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = OpenFor(canvas, groupVm);

        makeGate.Pins.Where(p => p.Role != GatePinRole.Unused).Select(p => (p.Role, p.Name))
            .ShouldBe(new[]
            {
                (GatePinRole.Input, "A"), (GatePinRole.Input, "B"),
                (GatePinRole.Bias, "BIAS"), (GatePinRole.Output, "Y"),
            }, ignoreOrder: true);
        makeGate.Pins.ShouldAllBe(p => p.IsSuggestedRole);
    }

    [Fact]
    public async Task ManualThresholdOverride_ReclassifiesRows_AndUseSuggestedRestores()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = OpenFor(canvas, groupVm);
        await makeGate.SimulateCommand.ExecuteAsync(null);
        makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.Nand);
        var suggested = makeGate.Threshold;

        makeGate.Threshold = 0.9;
        makeGate.Rows.ShouldAllBe(r => !r.IsOne, "no row reaches 0.9");
        makeGate.MatchesFunction.ShouldBeFalse();

        makeGate.UseSuggestedThresholdCommand.Execute(null);
        makeGate.Threshold.ShouldBe(suggested);
        makeGate.MatchesFunction.ShouldBeTrue();
    }

    [Fact]
    public async Task WrongFunction_WarnsAboutOverlap()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = OpenFor(canvas, groupVm);
        await makeGate.SimulateCommand.ExecuteAsync(null);

        makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.And);

        makeGate.HasOverlap.ShouldBeTrue("NAND levels cannot be read as AND");
        makeGate.MatchesFunction.ShouldBeFalse();
    }

    [Fact]
    public async Task Simulate_WithoutOutput_ShowsHint_AndRunsNothing()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = OpenFor(canvas, groupVm);
        foreach (var pin in makeGate.Pins.Where(p => p.Role == GatePinRole.Output))
            pin.IsUnused = true;

        await makeGate.SimulateCommand.ExecuteAsync(null);

        makeGate.HasResult.ShouldBeFalse();
        makeGate.StatusText.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Save_DuplicateShortNames_IsRejected()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = OpenFor(canvas, groupVm);
        makeGate.Pins.First(p => p.Role == GatePinRole.Input).Name = "Y";

        await makeGate.SimulateCommand.ExecuteAsync(null);

        makeGate.HasResult.ShouldBeFalse("two pins named Y cannot both exist");
        ((ComponentGroup)groupVm.Component).TruthTablePinAssignment.ShouldBeNull();
    }

    [Fact]
    public async Task Save_ThroughCommandManager_IsUndoable()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var commands = new CommandManager();
        var makeGate = OpenFor(canvas, groupVm);
        makeGate.ExecuteCommand = commands.ExecuteCommand;
        var group = (ComponentGroup)groupVm.Component;
        var namesBefore = group.ExternalPins.Select(p => p.Name).ToList();
        var groupNameBefore = group.GroupName;
        await makeGate.SimulateCommand.ExecuteAsync(null);
        makeGate.SaveCommand.Execute(null);

        commands.Undo();

        group.TruthTablePinAssignment.ShouldBeNull();
        group.ExternalPins.Select(p => p.Name).ShouldBe(namesBefore);
        group.GroupName.ShouldBe(groupNameBefore);
    }

    [Fact]
    public async Task SaveAfterUndo_UsesRestoredPhysicalPinNames_AndBuildsValidGate()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var commands = new CommandManager();
        var makeGate = OpenFor(canvas, groupVm);
        makeGate.ExecuteCommand = commands.ExecuteCommand;
        await makeGate.SimulateCommand.ExecuteAsync(null);
        makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.Nand);
        makeGate.SaveCommand.Execute(null);
        commands.Undo().ShouldBeTrue();
        makeGate.SaveCommand.Execute(null);
        await AssertLogicPanelReadsNand(canvas);
        commands.Undo().ShouldBeTrue();
        commands.Redo().ShouldBeTrue();
        await AssertLogicPanelReadsNand(canvas);
    }

    [Fact]
    public async Task UndoEditOfDefinedGate_SaveToLibraryPersistsCurrentEditorChoices()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var commands = new CommandManager();
        var makeGate = OpenFor(canvas, groupVm);
        makeGate.ExecuteCommand = commands.ExecuteCommand;
        await makeGate.SimulateCommand.ExecuteAsync(null);
        makeGate.SaveCommand.Execute(null);
        makeGate.GateName = "EditedGate";
        makeGate.SaveCommand.Execute(null);
        commands.Undo().ShouldBeTrue();
        string? libraryName = null;
        makeGate.SaveToLibraryRequested = (group, _) => { libraryName = group.GroupName; return true; };
        makeGate.SaveToLibraryCommand.Execute(null);
        libraryName.ShouldBe("EditedGate");
    }

    [Fact]
    public async Task SaveToLibrary_SavesTheGateAndCallsTheLibrary()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = OpenFor(canvas, groupVm);
        string? savedName = null;
        makeGate.SaveToLibraryRequested = (_, name) => { savedName = name; return true; };
        await makeGate.SimulateCommand.ExecuteAsync(null);
        makeGate.SelectedFunction = makeGate.Functions.Single(f => f.Function == GateFunction.Nand);

        makeGate.SaveToLibraryCommand.Execute(null);

        makeGate.IsSaved.ShouldBeTrue();
        savedName.ShouldBe("NAND");
    }

    [Fact]
    public async Task OpenInLogic_InvokesTheDockHook()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = OpenFor(canvas, groupVm);
        var opened = 0;
        makeGate.OpenLogicRequested = () => opened++;

        makeGate.OpenInLogicCommand.Execute(null);

        opened.ShouldBe(1);
    }

    [Fact]
    public async Task Selection_OnlyExactlyOneGroupEnablesMakeGate()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var makeGate = new MakeGateViewModel();

        makeGate.ConfigureForSelection(null, canvas);
        makeGate.CanMakeGate.ShouldBeFalse();

        canvas.Selection.SelectSingle(groupVm);
        makeGate.ConfigureForSelection(groupVm, canvas);
        makeGate.CanMakeGate.ShouldBeTrue();
        makeGate.OpenCommand.Execute(null);
        makeGate.IsActive.ShouldBeTrue();

        makeGate.ConfigureForSelection(null, canvas);
        makeGate.IsActive.ShouldBeFalse("deselecting closes the steps");
    }

    private static MakeGateViewModel OpenFor(DesignCanvasViewModel canvas, ComponentViewModel groupVm)
    {
        var makeGate = new MakeGateViewModel();
        canvas.Selection.SelectSingle(groupVm);
        makeGate.ConfigureForSelection(groupVm, canvas);
        makeGate.OpenCommand.Execute(null);
        makeGate.IsActive.ShouldBeTrue();
        return makeGate;
    }

    private static async Task AssertLogicPanelReadsNand(DesignCanvasViewModel canvas)
    {
        var logic = new LogicPanelViewModel();
        logic.Configure(canvas);
        await logic.BuildNetworkCommand.ExecuteAsync(null);
        logic.HasNetwork.ShouldBeTrue(logic.StatusText);
        logic.Inputs.Count.ShouldBe(2);
        bool Evaluate(bool a, bool b)
        {
            logic.Inputs.Single(i => i.PinName.EndsWith(".A", StringComparison.Ordinal)).IsOn = a;
            logic.Inputs.Single(i => i.PinName.EndsWith(".B", StringComparison.Ordinal)).IsOn = b;
            return logic.Outputs.Single().IsOne;
        }
        Evaluate(false, false).ShouldBeTrue();
        Evaluate(true, false).ShouldBeTrue();
        Evaluate(false, true).ShouldBeTrue();
        Evaluate(true, true).ShouldBeFalse("NAND: both inputs on reads 0");
    }
}

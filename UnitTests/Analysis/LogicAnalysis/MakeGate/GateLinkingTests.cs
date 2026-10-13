using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// The links between the right-panel gate section and the Logic tab: the Logic panel's
/// empty state lists groups that are not gates yet (each with "Make gate…"), and the
/// plain truth-table extraction suggests its threshold automatically and offers
/// "Open in Logic".
/// </summary>
public class GateLinkingTests
{
    [Fact]
    public async Task LogicPanel_ListsGroupsThatAreNotGatesYet()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var logic = new LogicPanelViewModel();
        logic.Configure(canvas);
        ComponentGroup? requested = null;
        logic.MakeGateRequested = group => requested = group;

        logic.HasGateCandidates.ShouldBeTrue();
        logic.GateCandidates.Single().Group.ShouldBeSameAs(groupVm.Component);
        logic.GateCandidates.Single().MakeGateCommand.Execute(null);

        requested.ShouldBeSameAs(groupVm.Component);
    }

    [Fact]
    public async Task LogicPanel_ShippedGate_IsNoCandidate()
    {
        var canvas = await GateKitFixture.LoadAsync(GateKitFixture.NotNandExample);
        var logic = new LogicPanelViewModel();
        logic.Configure(canvas);

        logic.HasGateCandidates.ShouldBeFalse("a group with a persisted assignment already is a gate");
    }

    [Fact]
    public async Task PlainExtraction_SuggestsTheThreshold_AndUseReExtractsAtIt()
    {
        var (canvas, groupVm) = await GateKitFixture.GroupedKitAsync();
        var group = (ComponentGroup)groupVm.Component;
        canvas.Selection.SelectSingle(groupVm);
        var truthTable = new TruthTableViewModel();
        truthTable.ConfigureForSelection(groupVm, canvas);
        Check(truthTable.InputPins, GateKitFixture.PinExposing(group, "combine", "in1").Name);
        Check(truthTable.InputPins, GateKitFixture.PinExposing(group, "combine", "in2").Name);
        Check(truthTable.OutputPins, GateKitFixture.PinExposing(group, "output", "b0").Name);
        Check(truthTable.BiasPins, GateKitFixture.PinExposing(group, "phase", "in").Name);

        await truthTable.ExtractCommand.ExecuteAsync(null);

        truthTable.SuggestedThreshold.ShouldNotBeNull(truthTable.StatusText);
        truthTable.SuggestedThreshold!.Value.ShouldBeInRange(0.05, 0.2, "the largest power step is the dark NAND row");
        truthTable.ThresholdSuggestionText.ShouldContain("dB");
        truthTable.CanUseSuggestedThreshold.ShouldBeTrue("the default 0.5 differs from the suggestion");

        await truthTable.UseSuggestedThresholdCommand.ExecuteAsync(null);

        truthTable.Threshold.ShouldBe(truthTable.SuggestedThreshold!.Value);
        truthTable.CanUseSuggestedThreshold.ShouldBeFalse();
        truthTable.Rows.Select(r => r.OutputCells[0].BitText).ShouldBe(new[] { "1", "1", "1", "0" });
        group.TruthTablePinAssignment!.Threshold.ShouldBe(truthTable.Threshold);
    }

    [Fact]
    public void PlainExtraction_OpenInLogic_InvokesTheDockHook()
    {
        var truthTable = new TruthTableViewModel();
        var opened = 0;
        truthTable.OpenLogicRequested = () => opened++;

        truthTable.OpenInLogicCommand.Execute(null);

        opened.ShouldBe(1);
    }

    private static void Check(IEnumerable<PinSelectionViewModel> pins, string name) =>
        pins.Single(p => p.PinName == name).IsChecked = true;
}

using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Editing E2E on the shipped <c>examples/Logic Gate Full Adder.lun</c> (issue #1499):
/// every logic journey so far evaluates a pristine example, so nothing proved that
/// editing hands back a working design. This journey pokes at the adder the way a
/// student would — delete the gate group driving <c>Cout</c>, delete one chain
/// crossing — then undoes both and demands the design back whole: same components,
/// same wires, canvas mirroring the connection manager, and the identical 8-row
/// truth table, once more after a save → load round trip. Built through the real
/// paths: the example loader with post-load routing, <see cref="DeleteComponentCommand"/>
/// through a <see cref="CommandManager"/>, and the merged
/// <see cref="LogicNetworkAssembler"/> the logic panel uses.
/// </summary>
public class FullAdderEditUndoJourneyTests
{
    private const string ExampleFileName = "Logic Gate Full Adder.lun";
    private const int TruthTableRowCount = 8;

    private static readonly bool[] Bits = { false, true };

    [Fact]
    public async Task DeleteGateAndCrossing_ThenUndo_RestoresIdenticalTruthTable_ThroughSaveLoad()
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(ExampleFileName);
        await fileOps.PostLoadRouting;
        int componentsBefore = canvas.Components.Count;
        int connectionsBefore = canvas.ConnectionManager.Connections.Count;
        var baseline = await LogicGateFullAdderExampleTests.AssembleNetwork(canvas);
        AssertComputesFullAdder(baseline);
        var baselineTable = TruthTableRows(baseline);

        var commands = new CommandManager();
        DeleteCoutGate(canvas, baseline, commands);
        DeleteOneChainCrossing(canvas, commands);

        commands.Undo();
        commands.Undo();

        canvas.Components.Count.ShouldBe(componentsBefore,
            "undoing both deletes must restore every component — gates and crossings alike");
        canvas.ConnectionManager.Connections.Count.ShouldBe(connectionsBefore,
            "undoing both deletes must restore every wire");
        canvas.Connections.Select(vm => vm.Connection)
            .ShouldBe(canvas.ConnectionManager.Connections, ignoreOrder: true,
                customMessage: "the canvas connection list must mirror the connection manager exactly");

        var restored = await LogicGateFullAdderExampleTests.AssembleNetwork(canvas);
        TruthTableRows(restored).ShouldBe(baselineTable,
            "delete → undo must hand back a design that still computes the full adder");

        await AssertTruthTableSurvivesSaveLoad(fileOps, baselineTable);
    }

    /// <summary>
    /// Deletes the gate group whose output the network taps as <c>Cout</c> and asserts
    /// no remaining wire — manager or canvas — touches any pin of the group or of the
    /// components nested in it.
    /// </summary>
    private static void DeleteCoutGate(
        DesignCanvasViewModel canvas, LogicNetworkEvaluator baseline, CommandManager commands)
    {
        var gateId = baseline.OutputTaps["Cout"].GateId;
        var group = canvas.Components.Select(vm => vm.Component).OfType<ComponentGroup>()
            .Single(g => g.GroupName == gateId);
        var scope = new ComponentWireScope(group);

        commands.ExecuteCommand(new DeleteComponentCommand(
            canvas, canvas.Components.Single(vm => ReferenceEquals(vm.Component, group))));

        canvas.Components.ShouldNotContain(vm => ReferenceEquals(vm.Component, group));
        canvas.ConnectionManager.Connections.ShouldAllBe(c => !scope.Touches(c),
            "deleting a gate group must take every wire to its own and its nested pins with it");
        canvas.Connections.ShouldAllBe(vm => !scope.Touches(vm.Connection),
            "the canvas connection list must not keep a wire into the deleted gate group");
    }

    /// <summary>
    /// Deletes one chain crossing adopted as inserted on load; the two wires through it
    /// merge, so the four chain pieces become two connections (−2).
    /// </summary>
    private static void DeleteOneChainCrossing(DesignCanvasViewModel canvas, CommandManager commands)
    {
        var crossing = canvas.Components.Select(vm => vm.Component)
            .FirstOrDefault(c => CrossingComponentCatalog.IsCrossing(c) && c.IsInsertedCrossing)
            .ShouldNotBeNull("the example ships crossings placed by crossing-aware routing");
        int wiresBefore = canvas.ConnectionManager.Connections.Count;

        commands.ExecuteCommand(new DeleteComponentCommand(
            canvas, canvas.Components.Single(vm => ReferenceEquals(vm.Component, crossing))));

        canvas.ConnectionManager.Connections.Count.ShouldBe(wiresBefore - 2,
            "the two wires through a deleted chain crossing merge, dropping two pieces");
    }

    /// <summary>Saves the restored design, reloads it through the real load path and re-assembles.</summary>
    private static async Task AssertTruthTableSurvivesSaveLoad(
        FileOperationsViewModel fileOps, List<string> baselineTable)
    {
        var savedPath = Path.Combine(Path.GetTempPath(), $"full-adder-edit-undo-{Guid.NewGuid():N}.lun");
        try
        {
            await MziFringeAnalysis.SaveToFileAsync(fileOps, savedPath);
            var (reloadedCanvas, reloadedOps, _) = await MziFringeAnalysis.LoadDesignFromPath(savedPath);
            await reloadedOps.PostLoadRouting;
            var reloaded = await LogicGateFullAdderExampleTests.AssembleNetwork(reloadedCanvas);
            TruthTableRows(reloaded).ShouldBe(baselineTable,
                "the save → load round trip must keep the restored design's truth table identical");
        }
        finally
        {
            if (File.Exists(savedPath)) File.Delete(savedPath);
        }
    }

    /// <summary>
    /// Guards against a vacuous pass: the pristine baseline must compute the full adder
    /// (S = A⊕B⊕Cin, Cout = majority) — identity with a broken table proves nothing.
    /// </summary>
    private static void AssertComputesFullAdder(LogicNetworkEvaluator network)
    {
        foreach (var a in Bits)
        foreach (var b in Bits)
        foreach (var cin in Bits)
        {
            var result = network.Evaluate(new Dictionary<string, bool>
                { ["A"] = a, ["B"] = b, ["Cin"] = cin });
            result["S"].ShouldBe(a ^ b ^ cin,
                $"S = A⊕B⊕Cin for A={a}, B={b}, Cin={cin}");
            result["Cout"].ShouldBe((a && b) || (a && cin) || (b && cin),
                $"Cout = majority(A, B, Cin) for A={a}, B={b}, Cin={cin}");
        }
    }

    /// <summary>
    /// Evaluates all eight operand combinations; each row lists every tap (S, Cout and
    /// all gate outputs) sorted by name, so a diff pinpoints the first diverging tap.
    /// </summary>
    private static List<string> TruthTableRows(LogicNetworkEvaluator network)
    {
        var rows = new List<string>(TruthTableRowCount);
        foreach (var a in Bits)
        foreach (var b in Bits)
        foreach (var cin in Bits)
        {
            var result = network.Evaluate(new Dictionary<string, bool>
                { ["A"] = a, ["B"] = b, ["Cin"] = cin });
            var taps = string.Join(",", result.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
            rows.Add($"A={a},B={b},Cin={cin}|{taps}");
        }
        return rows;
    }
}

using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Prefab-library persistence of logic roles (#1392): a group template saved to the
/// library must carry its <see cref="TruthTablePinAssignment"/> — top level and on
/// every nested gate group — or a placed prefab silently stops being a logic gate.
/// Templates written before the field existed load with a null assignment.
/// </summary>
public class GroupTemplateTruthTablePersistenceTests
{
    [Fact]
    public void SerializeAndDeserialize_GateGroupWithAssignment_RoundTripsDeepEqual()
    {
        var gate = LogicGateFixtureFactory.CreateCombinerGroup();
        gate.GroupName = "REG";
        gate.EnsureSMatrixComputed();
        gate.TruthTablePinAssignment = new TruthTablePinAssignment
        {
            InputPinNames = new List<string> { "a", "b" },
            OutputPinNames = new List<string> { "y" },
            BiasPinNames = new List<string> { "bias" },
            Threshold = 0.25,
            IsRegister = true,
            InputSignalNames = new Dictionary<string, string> { { "a", "A" }, { "b", "B" } },
            OutputSignalNames = new Dictionary<string, string> { { "y", "Q" } },
        };

        var json = GroupTemplateSerializer.Serialize(gate);
        var result = GroupTemplateSerializer.Deserialize(json);

        result.ShouldNotBeNull();
        var loaded = result!.TruthTablePinAssignment;
        loaded.ShouldNotBeNull();
        loaded!.InputPinNames.ShouldBe(new[] { "a", "b" });
        loaded.OutputPinNames.ShouldBe(new[] { "y" });
        loaded.BiasPinNames.ShouldBe(new[] { "bias" });
        loaded.Threshold.ShouldBe(0.25);
        loaded.IsRegister.ShouldBeTrue();
        loaded.InputSignalNames.ShouldBe(new Dictionary<string, string> { { "a", "A" }, { "b", "B" } });
        loaded.OutputSignalNames.ShouldBe(new Dictionary<string, string> { { "y", "Q" } });
    }

    [Fact]
    public async Task SerializeAndDeserialize_CellWithNestedGates_KeepsBothGatesAssemblable()
    {
        var cell = CellWithNestedOrGates();

        var result = GroupTemplateSerializer.Deserialize(GroupTemplateSerializer.Serialize(cell));

        result.ShouldNotBeNull();
        var nested = result!.ChildComponents.OfType<ComponentGroup>().ToList();
        nested.Count.ShouldBe(2);
        nested.ShouldAllBe(g => g.TruthTablePinAssignment != null);

        var network = await new LogicNetworkAssembler().AssembleAsync(
            new Component[] { result },
            Array.Empty<WaveguideConnection>(),
            LogicGateFixtureFactory.WavelengthNm);
        network.Gates.Keys.ShouldBe(new[] { "CELL/OR1", "CELL/OR2" },
            "the assembler must find both gates after the prefab-library round-trip");
    }

    [Fact]
    public void Deserialize_TemplateWithoutAssignmentField_LoadsWithNullAssignment()
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        var json = GroupTemplateSerializer.Serialize(group);

        // A template saved before the field existed has no TruthTablePinAssignment
        // entry at all — stripping it simulates exactly that legacy shape.
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node.AsObject().Remove(nameof(GroupTemplateDto.TruthTablePinAssignment));
        var legacyJson = node.ToJsonString();

        var result = GroupTemplateSerializer.Deserialize(legacyJson);

        result.ShouldNotBeNull();
        result!.TruthTablePinAssignment.ShouldBeNull();
        result.ChildComponents.Count.ShouldBe(1);
    }

    /// <summary>Two OR gates nested in a role-less cell, OR1.y → OR2.a frozen inside.</summary>
    private static ComponentGroup CellWithNestedOrGates()
    {
        var first = OrGate("OR1");
        var second = OrGate("OR2");
        var cell = new ComponentGroup("CELL");
        cell.AddChild(first);
        cell.AddChild(second);
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 10, 0, 0));
        cell.AddInternalPath(new FrozenWaveguidePath
        {
            Path = path,
            StartPin = InternalPin(first, "y"),
            EndPin = InternalPin(second, "a"),
        });
        return cell;
    }

    /// <summary>A combiner group with the OR-reading assignment persisted at threshold 0.25.</summary>
    private static ComponentGroup OrGate(string groupName)
    {
        var group = LogicGateFixtureFactory.CreateCombinerGroup();
        group.GroupName = groupName;
        group.EnsureSMatrixComputed();
        group.TruthTablePinAssignment = new TruthTablePinAssignment
        {
            InputPinNames = new List<string> { "a", "b" },
            OutputPinNames = new List<string> { "y" },
            BiasPinNames = new List<string>(),
            Threshold = 0.25,
        };
        return group;
    }

    /// <summary>The internal component pin behind one of a group's external pins.</summary>
    private static PhysicalPin InternalPin(ComponentGroup group, string name) =>
        group.ExternalPins.Single(p => p.Name == name).InternalPin!;
}

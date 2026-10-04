using System.Text.Json;
using System.Text.Json.Nodes;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// Test-side assembly adapter for the hierarchical-RAM spike (issue #1366). The shipped
/// logic pipeline gates only TOP-LEVEL groups whose truth-table assignment survived the
/// load: the loader restores <see cref="TruthTablePinAssignment"/> solely on top-level
/// groups, and a cell's frozen internal paths are not connections, so a hierarchical
/// design assembles to nothing as-is. The adapter re-attaches the persisted assignments
/// to the nested gate groups straight from the design document and turns every
/// cell-internal frozen path into a virtual connection, then runs the real
/// <see cref="LogicNetworkAssembler"/> over the flattened gates — exactly what the
/// product change (loader restore + assembler recursion + frozen paths as wires) would
/// do, kept test-side so the spike touches no product code. Endpoint binding is the
/// shipped one (<see cref="LogicNetworkBuilder"/> resolves a leaf pin to its gate through
/// the external pins of every candidate group, nesting-transparently).
/// </summary>
internal static class RamHierarchicalNetwork
{
    private const int WavelengthNm = 1550;

    /// <summary>Assembles the logic network of a loaded hierarchical design and times nothing.</summary>
    public static async Task<LogicNetworkEvaluator> Assemble(DesignCanvasViewModel canvas, string designJson)
    {
        var assignments = AssignmentsByIdentifier(designJson);
        var gates = new List<ComponentGroup>();
        var virtualWires = new List<WaveguideConnection>();
        foreach (var group in canvas.Components.Select(c => c.Component).OfType<ComponentGroup>())
        {
            if (group.TruthTablePinAssignment != null)
            {
                gates.Add(group);
                continue;
            }
            CollectNestedGates(group, assignments, gates);
            virtualWires.AddRange(group.InternalPaths.Select(ToVirtualConnection));
        }
        var connections = canvas.Connections.Select(c => c.Connection).Concat(virtualWires).ToList();
        return await new LogicNetworkAssembler().AssembleAsync(gates, connections, WavelengthNm);
    }

    /// <summary>Flattens a cell: every descendant group with a persisted assignment is a gate.</summary>
    private static void CollectNestedGates(
        ComponentGroup parent,
        IReadOnlyDictionary<string, TruthTablePinAssignment> assignments,
        List<ComponentGroup> gates)
    {
        foreach (var child in parent.ChildComponents.OfType<ComponentGroup>())
        {
            if (assignments.TryGetValue(child.Identifier, out var assignment))
            {
                child.TruthTablePinAssignment = assignment;
                gates.Add(child);
            }
            else
            {
                CollectNestedGates(child, assignments, gates);
            }
        }
    }

    /// <summary>Wraps one frozen intra-cell path as a connection between its leaf endpoint pins.</summary>
    private static WaveguideConnection ToVirtualConnection(FrozenWaveguidePath path)
    {
        var connection = new WaveguideConnection { StartPin = path.StartPin!, EndPin = path.EndPin! };
        connection.RestoreCachedPath(path.Path);
        return connection;
    }

    /// <summary>The persisted truth-table assignments of every group in the document, by group identifier.</summary>
    private static Dictionary<string, TruthTablePinAssignment> AssignmentsByIdentifier(string designJson)
    {
        var document = JsonNode.Parse(designJson)!.AsObject();
        var map = new Dictionary<string, TruthTablePinAssignment>();
        foreach (var entry in document["Groups"]!.AsArray())
        {
            var assignmentNode = entry!["TruthTablePinAssignment"];
            if (assignmentNode == null)
                continue;
            var identifier = entry["GroupDto"]!["Identifier"]!.GetValue<string>();
            map[identifier] = assignmentNode.Deserialize<TruthTablePinAssignment>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        return map;
    }
}

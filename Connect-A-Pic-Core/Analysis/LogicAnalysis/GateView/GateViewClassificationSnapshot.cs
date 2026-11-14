using System.Collections.Immutable;

namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>All classification inputs captured together; no live gate, pin or graph crosses the worker boundary.</summary>
public sealed class GateViewClassificationSnapshot
{
    internal sealed record Gate(string Id, GateSymbolKind Fallback, TruthTableSnapshot? Physics);
    internal ImmutableArray<Gate> Gates { get; }

    private GateViewClassificationSnapshot(ImmutableArray<Gate> gates) => Gates = gates;

    /// <summary>
    /// Nodes captured per owner-thread slice by <see cref="CaptureAsync"/>: a node copies one
    /// gate's matrices, the same work as one gate of the Logic build's capture.
    /// </summary>
    internal const int NodesPerSlice = LogicNetworkSnapshot.GatesPerSlice;

    /// <summary>Captures compact definitions on the document owner, keeping incomplete gates drawable.</summary>
    public static GateViewClassificationSnapshot Capture(GateViewGraph graph, int wavelengthNm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var gates = ImmutableArray.CreateBuilder<Gate>(graph.Nodes.Count);
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            gates.Add(CaptureGate(node, wavelengthNm));
        }
        return new(gates.ToImmutable());
    }

    /// <summary>
    /// Same capture, cut into owner-thread slices of <see cref="NodesPerSlice"/> nodes with
    /// <paramref name="slices"/> yielding between them; it rejects a resumption after the design
    /// changed, so every gate of one snapshot describes the same design.
    /// </summary>
    public static async Task<GateViewClassificationSnapshot> CaptureAsync(GateViewGraph graph, int wavelengthNm,
        ICaptureYield slices, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(slices);
        var gates = ImmutableArray.CreateBuilder<Gate>(graph.Nodes.Count);
        for (var index = 0; index < graph.Nodes.Count; index++)
        {
            // No ConfigureAwait(false): the next slice must read live state on the owner thread.
            if (index > 0 && index % NodesPerSlice == 0)
                await slices.YieldAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            gates.Add(CaptureGate(graph.Nodes[index], wavelengthNm));
        }
        return new(gates.ToImmutable());
    }

    private static Gate CaptureGate(GateViewNode node, int wavelengthNm)
    {
        TruthTableSnapshot? physics = null;
        if (!node.IsRegister)
        {
            try
            {
                var assignment = node.Group.TruthTablePinAssignment!;
                if (assignment.InputPinNames.Count is > 0 and <= TruthTableExtractor.MaxLogicInputs)
                    physics = TruthTableSnapshot.CaptureForClassification(node.Group, assignment.InputPinNames,
                        assignment.OutputPinNames, assignment.BiasPinNames, assignment.Threshold, wavelengthNm);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // A missing/unfinished optical definition keeps the structural symbol.
                // Invalid captures are not cached, so completing the model can recover.
            }
        }
        return new(node.GateId,
            GateSymbolClassifier.FromStructure(node.Inputs.Count, node.Outputs.Count, node.IsRegister), physics);
    }
}

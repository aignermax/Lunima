using System.Collections.Immutable;

namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>All classification inputs captured together; no live gate, pin or graph crosses the worker boundary.</summary>
public sealed class GateViewClassificationSnapshot
{
    internal sealed record Gate(string Id, GateSymbolKind Fallback, TruthTableSnapshot? Physics);
    internal ImmutableArray<Gate> Gates { get; }

    private GateViewClassificationSnapshot(ImmutableArray<Gate> gates) => Gates = gates;

    /// <summary>Captures compact definitions on the document owner, keeping incomplete gates drawable.</summary>
    public static GateViewClassificationSnapshot Capture(GateViewGraph graph, int wavelengthNm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var gates = ImmutableArray.CreateBuilder<Gate>(graph.Nodes.Count);
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            gates.Add(new(node.GateId,
                GateSymbolClassifier.FromStructure(node.Inputs.Count, node.Outputs.Count, node.IsRegister), physics));
        }
        return new(gates.ToImmutable());
    }
}

using System.Collections.Concurrent;

namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>
/// Gives every gate of a <see cref="GateViewGraph"/> its schematic symbol by extracting
/// its truth table — the same extraction the logic network runs, with the persisted
/// roles and threshold — and classifying it. Gates with equal
/// <see cref="GateStructureSignature"/>s share one extraction, and results stay cached
/// across rebuilds, so a design of hundreds of copies of a few cells classifies in a
/// handful of simulations. The extraction runs on a deep copy of the group: the view
/// never touches the design it shows.
/// </summary>
public sealed class GateFunctionClassifier
{
    private readonly TruthTableExtractor _extractor;
    private readonly ConcurrentDictionary<string, GateSymbolKind> _cache = new();

    /// <summary>Creates a classifier; tests may inject the extractor.</summary>
    public GateFunctionClassifier(TruthTableExtractor? extractor = null)
    {
        _extractor = extractor ?? new TruthTableExtractor();
    }

    /// <summary>Number of distinct gate structures classified so far (test seam).</summary>
    public int CachedStructureCount => _cache.Count;

    /// <summary>
    /// Sets <see cref="GateViewNode.Kind"/> on every gate. Registers and gates whose
    /// extraction fails keep their structural symbol; a failure never aborts the view.
    /// </summary>
    /// <param name="graph">The graph whose nodes are classified in place.</param>
    /// <param name="wavelengthNm">Laser wavelength the tables are extracted at.</param>
    /// <param name="cancellationToken">Cancels between gates.</param>
    public async Task ClassifyAsync(GateViewGraph graph, int wavelengthNm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        foreach (var node in graph.Nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.IsRegister)
                continue;
            var signature = GateStructureSignature.Of(node.Group, wavelengthNm);
            if (!_cache.TryGetValue(signature, out var kind))
            {
                kind = await ExtractKindAsync(node, wavelengthNm, cancellationToken);
                _cache[signature] = kind;
            }
            if (kind != GateSymbolKind.Unknown)
                node.Kind = kind;
        }
    }

    private async Task<GateSymbolKind> ExtractKindAsync(GateViewNode node, int wavelengthNm, CancellationToken cancellationToken)
    {
        var assignment = node.Group.TruthTablePinAssignment!;
        if (assignment.InputPinNames.Count is 0 or > TruthTableExtractor.MaxLogicInputs)
            return node.Kind;
        try
        {
            var copy = node.Group.DeepCopy();
            var table = await _extractor.ExtractAsync(
                copy,
                assignment.InputPinNames,
                assignment.OutputPinNames,
                assignment.BiasPinNames,
                assignment.Threshold,
                wavelengthNm,
                cancellationToken);
            return GateSymbolClassifier.FromTruthTable(table, isRegister: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A gate that cannot simulate (incomplete edit, missing model) keeps its
            // structural symbol — the Logic tab reports the actual error on build.
            return node.Kind;
        }
    }
}

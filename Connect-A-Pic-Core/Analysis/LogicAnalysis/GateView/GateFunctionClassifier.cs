namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>
/// Classifies detached gate physics and reuses equivalent captured definitions across rebuilds.
/// Live graph bindings are updated only by the caller after the worker has returned.
/// </summary>
public sealed class GateFunctionClassifier
{
    private readonly TruthTableExtractor _extractor;
    private readonly GateClassificationCache _cache = new();

    /// <summary>Creates a classifier with an optional optical extraction worker.</summary>
    public GateFunctionClassifier(TruthTableExtractor? extractor = null) =>
        _extractor = extractor ?? new TruthTableExtractor();

    /// <summary>Number of captured physics identities currently retained.</summary>
    public int CachedStructureCount => _cache.Count;

    /// <summary>Captures every gate before dispatch and applies classification on the caller continuation.</summary>
    public async Task ClassifyAsync(GateViewGraph graph, int wavelengthNm,
        CancellationToken cancellationToken = default)
    {
        var snapshot = GateViewClassificationSnapshot.Capture(graph, wavelengthNm, cancellationToken);
        var kinds = await Task.Run(() => ClassifyAsync(snapshot, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Apply(graph, kinds);
    }

    /// <summary>Worker-only classification: inputs and results contain no live group or pin bindings.</summary>
    public async Task<IReadOnlyDictionary<string, GateSymbolKind>> ClassifyAsync(
        GateViewClassificationSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var result = new Dictionary<string, GateSymbolKind>(snapshot.Gates.Length);
        foreach (var gate in snapshot.Gates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kind = gate.Fallback;
            if (gate.Physics is { } physics)
            {
                try
                {
                    var signature = physics.ClassificationIdentity();
                    if (!_cache.TryGet(signature, out kind))
                    {
                        var table = await _extractor.ExtractCapturedAsync(physics, cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        kind = GateSymbolClassifier.FromTruthTable(table, isRegister: false);
                        _cache.Set(signature, kind);
                    }
                    if (kind == GateSymbolKind.Unknown) kind = gate.Fallback;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { kind = gate.Fallback; }
            }
            result.Add(gate.Id, kind);
        }
        return result;
    }

    /// <summary>Attaches worker values to the caller's already-admitted graph.</summary>
    public static void Apply(GateViewGraph graph, IReadOnlyDictionary<string, GateSymbolKind> kinds)
    {
        foreach (var node in graph.Nodes)
            if (kinds.TryGetValue(node.GateId, out var kind)) node.Kind = kind;
    }
}

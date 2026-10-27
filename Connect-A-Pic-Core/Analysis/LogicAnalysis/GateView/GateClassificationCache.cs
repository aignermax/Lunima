namespace CAP_Core.Analysis.LogicAnalysis.GateView;

/// <summary>Bounded symbols and fixed-size physics hashes; never owns optical definitions or live gates.</summary>
internal sealed class GateClassificationCache
{
    private const int Capacity = 256;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, GateSymbolKind Kind)>> _entries = new();
    private readonly LinkedList<(string Key, GateSymbolKind Kind)> _recent = new();

    internal int Count { get { lock (_gate) return _entries.Count; } }

    internal bool TryGet(string key, out GateSymbolKind kind)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                kind = node.Value.Kind;
                return true;
            }
            kind = default;
            return false;
        }
    }

    internal void Set(string key, GateSymbolKind kind)
    {
        lock (_gate)
        {
            if (_entries.Remove(key, out var existing)) _recent.Remove(existing);
            _entries.Add(key, _recent.AddFirst((key, kind)));
            if (_entries.Count <= Capacity) return;
            var oldest = _recent.Last!;
            _entries.Remove(oldest.Value.Key);
            _recent.RemoveLast();
        }
    }
}

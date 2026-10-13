namespace CAP_Core.Components.Connections;

/// <summary>
/// Remembers wires the router proved it cannot place with their current pin positions — a
/// wire that failed again with unchanged endpoints, or in every ordering of a cascade. Such a
/// failure comes from the pin geometry (e.g. a 4 µm splitter fan-out into an 8 µm MMI input
/// pair), so no wire ordering can fix it, and retrying it on every routing pass only costs
/// time and churns the routes of its neighbours. An entry is valid only while both endpoints
/// stay where they were: moving either pin, or an explicit re-route, drops it.
/// Thread-safe: read on the routing thread, cleared from UI commands.
/// </summary>
public sealed class UnroutableWireMemory
{
    /// <summary>
    /// Pin positions are compared on this grid so sub-quantum float noise in the pin math
    /// never makes an unchanged wire look moved.
    /// </summary>
    private const double PositionQuantumMicrometers = 0.5;

    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly object _sync = new();

    /// <summary>Number of wires currently remembered.</summary>
    public int Count
    {
        get { lock (_sync) return _entries.Count; }
    }

    /// <summary>Remembers <paramref name="connection"/> as unroutable at its current pin positions.</summary>
    public void Remember(WaveguideConnection connection)
    {
        lock (_sync)
            _entries[connection.Id] = new Entry(PinKey.Of(connection), ChainSearchExhausted: false);
    }

    /// <summary>
    /// True when <paramref name="connection"/> is remembered and neither of its pins moved
    /// since. An entry whose pins moved is dropped, so the wire gets a fresh chance.
    /// </summary>
    public bool IsRemembered(WaveguideConnection connection) => TryGetCurrent(connection, out _);

    /// <summary>
    /// Records that the crossing-chain search also failed for a remembered wire, so later
    /// crossing passes skip it too. No-op for a wire that is not remembered.
    /// </summary>
    public void MarkChainSearchExhausted(WaveguideConnection connection)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(connection.Id, out var entry) && entry.Pins == PinKey.Of(connection))
                _entries[connection.Id] = entry with { ChainSearchExhausted = true };
        }
    }

    /// <summary>True when the wire is remembered and its crossing-chain search already failed.</summary>
    public bool IsChainSearchExhausted(WaveguideConnection connection) =>
        TryGetCurrent(connection, out var entry) && entry.ChainSearchExhausted;

    /// <summary>Forgets one wire (explicit re-route). Returns true when it was remembered.</summary>
    public bool Forget(WaveguideConnection connection)
    {
        lock (_sync)
            return _entries.Remove(connection.Id);
    }

    /// <summary>Forgets every wire (the design was replaced).</summary>
    public void Clear()
    {
        lock (_sync)
            _entries.Clear();
    }

    private bool TryGetCurrent(WaveguideConnection connection, out Entry entry)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(connection.Id, out entry))
                return false;
            if (entry.Pins == PinKey.Of(connection))
                return true;
            _entries.Remove(connection.Id);
            return false;
        }
    }

    private readonly record struct Entry(PinKey Pins, bool ChainSearchExhausted);

    /// <summary>Quantized absolute positions of a wire's two pins.</summary>
    private readonly record struct PinKey(long StartX, long StartY, long EndX, long EndY)
    {
        public static PinKey Of(WaveguideConnection connection)
        {
            var (startX, startY) = connection.StartPin.GetAbsolutePosition();
            var (endX, endY) = connection.EndPin.GetAbsolutePosition();
            return new PinKey(Quantize(startX), Quantize(startY), Quantize(endX), Quantize(endY));
        }

        private static long Quantize(double micrometers) =>
            (long)Math.Round(micrometers / PositionQuantumMicrometers);
    }
}

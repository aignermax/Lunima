using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Analysis.LogicAnalysis.GateView;

namespace CAP.Avalonia.ViewModels.Logic.GateView;

/// <summary>The live logic level of a net in the gate view.</summary>
public enum GateNetLevel
{
    /// <summary>No evaluated network (Logic not run yet, or the design changed since).</summary>
    Unknown,

    /// <summary>The net carries logic 0.</summary>
    Zero,

    /// <summary>The net carries logic 1.</summary>
    One,
}

/// <summary>
/// Reads net levels from the same source the canvas 0/1 badges and the Logic tab use —
/// the <see cref="LogicGateStateOverlay"/> the Logic panel fills after every evaluation
/// — so the gate view can never disagree with the Logic tab. A driven net takes its
/// driver's output bit; a network-input net takes its input toggle's bit (or, failing
/// that, the badge of one of its named input pins).
/// </summary>
public sealed class GateNetLevels
{
    private readonly LogicGateStateOverlay _overlay;
    private readonly Func<IReadOnlyDictionary<string, bool>>? _networkInputBits;
    private Dictionary<LogicPinRef, bool> _bits = new();
    private IReadOnlyDictionary<string, bool> _inputBits = new Dictionary<string, bool>();

    /// <summary>Starts following the overlay's evaluations.</summary>
    /// <param name="overlay">The canvas logic-state overlay the Logic panel fills.</param>
    /// <param name="networkInputBits">
    /// The Logic tab's current input toggles by network input name, read after every
    /// evaluation — they give unnamed input nets their level, which carries no badge.
    /// </param>
    public GateNetLevels(LogicGateStateOverlay overlay, Func<IReadOnlyDictionary<string, bool>>? networkInputBits = null)
    {
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _networkInputBits = networkInputBits;
        _overlay.StatesChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Raised after the levels changed (new evaluation, or cleared).</summary>
    public event Action? Changed;

    /// <summary>True while an evaluated network supplies levels.</summary>
    public bool HasLevels => _bits.Count > 0;

    /// <summary>The live level of one net.</summary>
    public GateNetLevel LevelOf(GateViewNet net)
    {
        ArgumentNullException.ThrowIfNull(net);
        if (net.Driver != null)
            return LevelAt(net.Driver);
        if (_inputBits.TryGetValue(net.Name, out var inputBit))
            return inputBit ? GateNetLevel.One : GateNetLevel.Zero;
        foreach (var load in net.Loads)
        {
            var level = LevelAt(load);
            if (level != GateNetLevel.Unknown)
                return level;
        }
        return GateNetLevel.Unknown;
    }

    private GateNetLevel LevelAt(LogicPinRef pin) =>
        _bits.TryGetValue(pin, out var bit) ? (bit ? GateNetLevel.One : GateNetLevel.Zero) : GateNetLevel.Unknown;

    private void Refresh()
    {
        _bits = _overlay.Badges
            .GroupBy(badge => new LogicPinRef(badge.GroupName, badge.PinName))
            .ToDictionary(group => group.Key, group => group.First().IsOne);
        _inputBits = _bits.Count > 0 && _networkInputBits != null
            ? _networkInputBits()
            : new Dictionary<string, bool>();
        Changed?.Invoke();
    }
}

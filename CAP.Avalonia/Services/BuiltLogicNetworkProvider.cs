using CAP_Core.Analysis.LogicAnalysis;

namespace CAP.Avalonia.Services;

/// <summary>
/// Hand-off point for the logic network the Logic tab assembled last (rung 5,
/// issue #1215): the Logic panel publishes its freshly built network here, so
/// sibling features — the ISA playground's "compute ADD on the photonic chip"
/// toggle — can run on the student's own design without importing the Logic
/// analysis feature. The provider holds no state of its own beyond the
/// reference; a rebuild failure, a cancel or a design edit clears it again, so
/// consumers never see a network that no longer matches the design on the
/// canvas. Registered as a DI singleton: the Logic panel (transient) and the
/// playground window (singleton) must share one instance.
/// </summary>
public sealed class BuiltLogicNetworkProvider
{
    private LogicNetworkEvaluator? _network;

    /// <summary>Raised whenever the held network changes (publish or clear).</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when a consumer drove operand bits into the held network (issue #1240):
    /// the ISA playground publishes the A/B/Cin bits of every photonic ADD here, so
    /// the Logic panel can mirror them onto its input toggles — the canvas 0/1 badges
    /// then show the addition the program just executed. Carries signal name → bit.
    /// </summary>
    public event Action<IReadOnlyDictionary<string, bool>>? InputsDriven;

    /// <summary>The last successfully assembled network, or null when none is current.</summary>
    public LogicNetworkEvaluator? Network => _network;

    /// <summary>Publishes a freshly assembled network.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="network"/> is null.</exception>
    public void Publish(LogicNetworkEvaluator network)
    {
        _network = network ?? throw new ArgumentNullException(nameof(network));
        Changed?.Invoke();
    }

    /// <summary>
    /// Announces operand bits a consumer drove into the network; subscribers (the
    /// Logic panel) mirror them onto their input toggles. No-op without subscribers.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="bits"/> is null.</exception>
    public void DriveInputs(IReadOnlyDictionary<string, bool> bits)
    {
        ArgumentNullException.ThrowIfNull(bits);
        InputsDriven?.Invoke(bits);
    }

    /// <summary>Drops the held network (rebuild failed, cancelled, or the design changed).</summary>
    public void Clear()
    {
        if (_network is null)
        {
            return;
        }

        _network = null;
        Changed?.Invoke();
    }
}

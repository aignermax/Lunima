namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>
/// One optical fan-out warning raised while assembling a logic network: a single
/// driver — a gate output pin wired to several gate inputs, or a network-input
/// signal feeding several gate inputs — feeds more than one gate input. The logic
/// layer hands every driven input the full ideal level, but optically the waveguide
/// behind the driver would have to split, costing ~3 dB per branch (a 1×2 splitter
/// halves the power), so a real photonic implementation needs splitters plus level
/// restoration (amplification or a duplicated stage). The warning is non-blocking:
/// the idealized logic result stays available, and the network evaluates unchanged.
/// The same record also carries the degraded-link level warning of issue #1445 (see
/// <paramref name="LinkDisplayName"/>) — one level-warning type, one UI surface.
/// </summary>
/// <param name="DriverDisplayName">
/// The driver as shown to the user: <c>&lt;gate&gt;.&lt;pin&gt;</c> for a gate
/// output, or the signal name for a network-input signal (e.g. <c>A</c> for the
/// full adder's addend A, <c>Cin</c> for its carry-in).
/// </param>
/// <param name="IsNetworkInputSignal">
/// True when the driver is a network-input signal (one network input feeding
/// several gate inputs); false when it is a gate output pin.
/// </param>
/// <param name="LoadCount">How many gate inputs the driver feeds.</param>
/// <param name="LoadNames">
/// The driven gate inputs in <c>&lt;gate&gt;.&lt;pin&gt;</c> form, in declaration
/// order — listed in the UI so the user sees exactly which pins the warning
/// groups together.
/// </param>
/// <param name="Levels">
/// The quantitative level report (#1011): assuming an ideal 1×N splitter behind the
/// driver, the per-branch power and, per receiving input, whether it would still
/// reach that gate's power threshold and read as a logic 1. Advisory only — the
/// idealized logic result stays unchanged.
/// </param>
/// <param name="LinkDisplayName">
/// Set instead of a fan-out site when the warning flags a point-to-point wire whose
/// delivered 1-level was degraded below the receiving gate's threshold by a chiplet
/// edge-coupler link (issue #1445) — the link named in the
/// <c>'Chiplet A' / 'Chiplet B'</c> form the Design Checks findings use. Null for
/// genuine fan-out warnings.
/// </param>
public sealed record LogicFanOutWarning(
    string DriverDisplayName,
    bool IsNetworkInputSignal,
    int LoadCount,
    IReadOnlyList<string> LoadNames,
    FanOutLevelReport Levels,
    string? LinkDisplayName = null);

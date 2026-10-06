namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>
/// The coupling loss one inter-gate logic wire picks up by physically crossing a
/// chiplet edge-coupler link (issue #1445, rung 4×6): the power coupling factor of
/// the link — the same Gaussian mode-overlap model the S-matrix charges
/// (<see cref="ChipletEdgeCouplerCoupling"/>) — so the logic layer's level report
/// can multiply it in instead of reading an idealized 1. Only wires whose hopped
/// connection path crosses a cross-chiplet link carry an entry; an aligned link
/// couples perfectly (factor 1) and produces none.
/// </summary>
/// <param name="PowerCoupling">
/// Power factor the wire's level is multiplied by (field factor squared), in
/// [0, 1): 1 would be a lossless crossing, which never produces an entry.
/// </param>
/// <param name="LinkDisplayName">
/// The link as the user knows it — the two chiplet group names, in the same
/// <c>'Chiplet A' / 'Chiplet B'</c> form the Design Checks findings use.
/// </param>
public sealed record LogicWireLinkLoss(double PowerCoupling, string LinkDisplayName);

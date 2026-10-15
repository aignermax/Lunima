namespace CAP_Core.Routing.WaveguideCompanions;

/// <summary>
/// What a companion band drawn along a routed waveguide core means to the foundry.
/// Only <see cref="KeepOut"/> changes design-rule checking: cladding and trench bands of
/// neighbouring waveguides may touch or merge (the mask is their union), whereas two
/// keep-out bands of different wires must not overlap.
/// </summary>
public enum WaveguideCompanionRole
{
    /// <summary>Cladding opening / cladding definition band around the core.</summary>
    Cladding,

    /// <summary>Etch trench band (e.g. a shallow- or deep-etch trench) around the core.</summary>
    Trench,

    /// <summary>Exclusion band no other waveguide's keep-out may overlap.</summary>
    KeepOut,
}

namespace CAP.Avalonia.Controls.Rendering.DeviceCategories;

/// <summary>
/// Functional class of a photonic device, used only to give component bodies a category
/// accent on the canvas so an MMI and a phase shifter are distinguishable before (or without)
/// their GDS preview. Mirrors the PDK library categories, folded into a handful of visual groups.
/// </summary>
public enum DeviceCategory
{
    /// <summary>Unclassified device.</summary>
    Other,
    /// <summary>Straight and bent waveguides, tapers, spirals.</summary>
    Waveguide,
    /// <summary>MMIs, directional couplers, Y-branches, splitters.</summary>
    Coupler,
    /// <summary>Rings, MZIs, Bragg / contra-directional filters, polarizers.</summary>
    Filter,
    /// <summary>Phase shifters, modulators, heaters — devices driven electrically.</summary>
    Active,
    /// <summary>Grating and edge couplers, light sources.</summary>
    InputOutput,
    /// <summary>Photodetectors, terminators, analyzers — where light ends.</summary>
    Detector,
    /// <summary>Waveguide crossings.</summary>
    Crossing,
    /// <summary>Bond and probe pads.</summary>
    Electrical,
}

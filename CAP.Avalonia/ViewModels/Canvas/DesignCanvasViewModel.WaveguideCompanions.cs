using CAP_Core.Components.Core;
using CAP_Core.Routing.WaveguideCompanions;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Process companion layers (cladding / trench / keep-out bands) of the canvas' routed
/// waveguides — the single source the canvas renderer and the Nazca export both read, so
/// what the canvas shows is what the GDS gets.
/// </summary>
public partial class DesignCanvasViewModel
{
    /// <summary>
    /// Resolves the companion layers of a waveguide between two pins. Set by the composition
    /// root; null (tests, design-time) draws and exports the core only.
    /// </summary>
    public Func<PhysicalPin?, PhysicalPin?, WaveguideCompanionSpec>? WaveguideCompanionSpecProvider { get; set; }

    /// <summary>The companion layers of a waveguide between two pins; empty when no provider is set.</summary>
    public WaveguideCompanionSpec GetWaveguideCompanions(PhysicalPin? startPin, PhysicalPin? endPin) =>
        WaveguideCompanionSpecProvider?.Invoke(startPin, endPin) ?? WaveguideCompanionSpec.Empty;
}

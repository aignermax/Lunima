using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Grid;

namespace UnitTests.Simulation;

/// <summary>
/// Holds the complete setup for a test circuit including all components and managers.
/// </summary>
public class CircuitSetup
{
    /// <summary>
    /// The tile manager containing all components.
    /// </summary>
    public ComponentListTileManager TileManager { get; }

    /// <summary>
    /// Manages waveguide connections between components.
    /// </summary>
    public WaveguideConnectionManager ConnectionManager { get; }

    /// <summary>
    /// Input grating coupler receiving external light.
    /// </summary>
    public ComponentInfo GcInput { get; }

    /// <summary>
    /// 1x2 splitter dividing light into two paths.
    /// </summary>
    public ComponentInfo Splitter { get; }

    /// <summary>
    /// Top directional coupler.
    /// </summary>
    public ComponentInfo DcTop { get; }

    /// <summary>
    /// Bottom directional coupler.
    /// </summary>
    public ComponentInfo DcBottom { get; }

    /// <summary>
    /// Output grating couplers (4 total).
    /// </summary>
    public ComponentInfo[] GcOutputs { get; }

    /// <summary>
    /// Creates a new circuit setup with all components.
    /// </summary>
    public CircuitSetup(
        ComponentListTileManager tileManager,
        WaveguideConnectionManager connectionManager,
        ComponentInfo gcInput,
        ComponentInfo splitter,
        ComponentInfo dcTop,
        ComponentInfo dcBottom,
        ComponentInfo gcOut1,
        ComponentInfo gcOut2,
        ComponentInfo gcOut3,
        ComponentInfo gcOut4)
    {
        TileManager = tileManager;
        ConnectionManager = connectionManager;
        GcInput = gcInput;
        Splitter = splitter;
        DcTop = dcTop;
        DcBottom = dcBottom;
        GcOutputs = new[] { gcOut1, gcOut2, gcOut3, gcOut4 };
    }
}

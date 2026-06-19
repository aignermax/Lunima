using CAP_Core.Components.Core;
using CAP_Core.Export;
using CAP_Core.Solvers.Fdtd;

namespace CAP.Avalonia.Services.Solvers;

/// <summary>
/// Builds an <see cref="FdtdSMatrixRequest"/> for a placed component by rendering
/// its Nazca geometry (reusing <see cref="NazcaComponentPreviewService"/>, the
/// same single-component renderer the PDK Offset Editor uses) and turning the
/// rendered polygons and pin stubs into FDTD geometry and ports. Lunima knows
/// its own pins, so ports come from the render rather than being reconstructed.
/// </summary>
public class ComponentFdtdRequestFactory
{
    /// <summary>Default port (waveguide) width in µm when the render doesn't carry one.</summary>
    public const double DefaultPortWidthUm = 0.5;

    /// <summary>
    /// Default GDS layer carrying the optical waveguide. The render returns many
    /// layers (metal, dummy, design-area); FDTD must only see the guiding layer,
    /// so polygons are filtered to this layer (with a fall-back to all layers when
    /// none match, so a non-standard PDK still produces geometry).
    /// </summary>
    public const int DefaultSiliconLayer = 1;

    private readonly NazcaComponentPreviewService _preview;
    private readonly double _portWidthUm;
    private readonly int _siliconLayer;

    /// <summary>Initializes the factory.</summary>
    public ComponentFdtdRequestFactory(
        NazcaComponentPreviewService preview,
        double portWidthUm = DefaultPortWidthUm,
        int siliconLayer = DefaultSiliconLayer)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _portWidthUm = portWidthUm;
        _siliconLayer = siliconLayer;
    }

    /// <summary>
    /// Renders the component and builds an FDTD request, or returns null when the
    /// geometry/pins could not be obtained (the caller surfaces a clear status).
    /// </summary>
    public async Task<FdtdSMatrixRequest?> BuildAsync(Component component, CancellationToken ct = default)
    {
        var preview = await _preview.RenderAsync(component.NazcaModuleName, component.NazcaFunctionName, null, ct);
        if (!preview.Success || preview.Polygons.Count == 0 || preview.Pins.Count == 0)
            return null;

        var componentPinNames = component.PhysicalPins?.Select(p => p.Name).ToList() ?? new List<string>();

        return new FdtdSMatrixRequest
        {
            Polygons = BuildPolygons(preview.Polygons, _siliconLayer),
            Ports = BuildPorts(preview.Pins, componentPinNames, _portWidthUm),
            LayerNumber = _siliconLayer,
            Is3D = false, // 2D for a quick recompute; a 3D/accuracy toggle can come later
        };
    }

    /// <summary>
    /// Keeps only polygons on the optical layer (falls back to all layers when
    /// none match, so a PDK that puts its guide on another layer still renders).
    /// </summary>
    internal static IReadOnlyList<FdtdPolygon> BuildPolygons(
        IReadOnlyList<NazcaPreviewPolygon> polygons, int siliconLayer)
    {
        var onLayer = polygons.Where(p => p.Layer == siliconLayer).ToList();
        var source = onLayer.Count > 0 ? onLayer : polygons;
        return source.Select(p => new FdtdPolygon
        {
            Layer = p.Layer,
            Points = p.Vertices.Select(v => new FdtdPoint(v.X, v.Y)).ToList(),
        }).ToList();
    }

    /// <summary>
    /// Builds FDTD ports from the rendered Nazca pin stubs. Positions and angles
    /// come from the preview (same coordinate frame as the polygons), but the port
    /// <b>names</b> come from the component's own pins so the resulting S-matrix is
    /// keyed by the names the simulator expects (e.g. "port 1"), not the Nazca cell
    /// pin names ("a0"/"pin1"). Without this the override can't be mapped onto the
    /// component and every wavelength is skipped.
    ///
    /// Pins are matched by index (a PDK's pin order matches its Nazca cell's pin
    /// order). If the counts differ we keep the preview names rather than guess —
    /// the override will then report the mismatch instead of mislabelling ports.
    /// </summary>
    internal static IReadOnlyList<FdtdPort> BuildPorts(
        IReadOnlyList<NazcaPreviewPin> pins, IReadOnlyList<string> componentPinNames, double portWidthUm)
    {
        bool useComponentNames = componentPinNames.Count == pins.Count;
        return pins.Select((pin, i) => new FdtdPort
        {
            Name = useComponentNames ? componentPinNames[i] : pin.Name,
            X = pin.X,
            Y = pin.Y,
            Orientation = pin.Angle,
            Width = portWidthUm,
        }).ToList();
    }
}

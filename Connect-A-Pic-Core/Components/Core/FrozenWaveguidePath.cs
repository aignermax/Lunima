using System.Numerics;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.Routing;

namespace CAP_Core.Components.Core;

/// <summary>
/// Represents a waveguide path that is frozen (fixed geometry).
/// Unlike regular RoutedPath, these don't recalculate during group moves.
/// The path geometry is stored in absolute coordinates and translated when the group moves.
/// </summary>
public class FrozenWaveguidePath : ICloneable
{
    /// <summary>
    /// The routed path segments with fixed geometry.
    /// </summary>
    public RoutedPath Path { get; set; }

    /// <summary>
    /// Physical pin where this frozen path starts, or null for pin-less geometry
    /// (e.g. a GDS-imported route outline): pin-less paths render, move with the
    /// group and persist, but never re-expand into a live connection (group edit
    /// mode and ungroup skip them) and do not participate in simulation.
    /// </summary>
    public PhysicalPin? StartPin { get; set; }

    /// <summary>
    /// Physical pin where this frozen path ends (null under the same conditions
    /// as <see cref="StartPin"/>; a path is pin-less only when BOTH pins are null).
    /// </summary>
    public PhysicalPin? EndPin { get; set; }

    /// <summary>
    /// GDS layer of the source geometry this path was imported from (e.g. the
    /// top-cell route polygon's layer), paired with <see cref="DataType"/>. Null for
    /// connections routed inside the app (no source layer) and for .lun files that
    /// predate the field — exports then fall back to the process default layers,
    /// exactly like before. The tag is meaningful only when BOTH values are set.
    /// </summary>
    public int? Layer { get; set; }

    /// <summary>
    /// GDS datatype of the source geometry — see <see cref="Layer"/>.
    /// </summary>
    public int? DataType { get; set; }

    /// <summary>
    /// Unique identifier for this frozen path.
    /// </summary>
    public Guid PathId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Propagation loss in dB per centimeter.
    /// Default matches the standard waveguide loss used in WaveguideConnection.
    /// Default: 0.5 dB/cm (high-quality strip waveguide)
    /// </summary>
    public double PropagationLossDbPerCm { get; set; } = 0.5;

    /// <summary>
    /// Loss per 90-degree bend in dB, captured from the original connection so the
    /// frozen path keeps the connection's bend loss. Default matches the
    /// WaveguideConnection default (0.05 dB per 90° bend).
    /// </summary>
    public double BendLossDbPer90Deg { get; set; } = 0.05;

    /// <summary>
    /// Dispersion model of the original connection's waveguide (group index and loss vs.
    /// wavelength), captured from the live connection when the group was formed. Null when
    /// the source waveguide carried none — and after a .lun round-trip, since the model is
    /// not persisted; consumers then fall back to their documented defaults.
    /// </summary>
    public IDispersionModel? DispersionModel { get; set; }

    /// <summary>
    /// Routing style of the original connection (Auto/Bend/SBend/Cobra).
    /// Preserved so re-expanding the group (edit mode, ungroup, template
    /// instantiation) restores the user's chosen style instead of "Auto".
    /// </summary>
    public WaveguideType ConnectionType { get; set; } = WaveguideType.Auto;

    /// <summary>
    /// Bend radius of the original connection in micrometers.
    /// </summary>
    public double BendRadiusMicrometers { get; set; } = WaveguideConnection.DefaultBendRadiusMicrometers;

    /// <summary>
    /// Waveguide width of the original connection in micrometers.
    /// </summary>
    public double WidthMicrometers { get; set; } = WaveguideConnection.DefaultWidthMicrometers;

    /// <summary>
    /// Whether the original connection's route was frozen (manually pinned geometry).
    /// </summary>
    public bool IsRouteFrozen { get; set; }

    /// <summary>
    /// Manual per-bend radius overrides of the original connection, keyed by bend index.
    /// </summary>
    public Dictionary<int, double> BendRadiusOverrides { get; } = new();

    /// <summary>
    /// Manual straight-segment shift offsets of the original connection, keyed by the index
    /// of the segment among the path's straight segments (issue #791).
    /// </summary>
    public Dictionary<int, double> StraightShiftOffsets { get; } = new();

    /// <summary>
    /// Captures the per-connection routing settings (style, radius, width, freeze
    /// flag, bend overrides, segment shifts, propagation loss) from a live connection
    /// so they survive while the connection only exists as a frozen path inside a group.
    /// The import source-layer tag (<see cref="WaveguideConnection.SourceGdsLayer"/>)
    /// rides along into <see cref="Layer"/>/<see cref="DataType"/>, so a re-routed
    /// GDS connection that gets grouped still exports on its original layer.
    /// </summary>
    /// <param name="connection">The live connection to capture settings from.</param>
    public void CaptureSettingsFrom(WaveguideConnection connection)
    {
        Layer = connection.SourceGdsLayer;
        DataType = connection.SourceGdsDataType;
        ConnectionType = connection.Type;
        BendRadiusMicrometers = connection.BendRadiusMicrometers;
        WidthMicrometers = connection.WidthMicrometers;
        IsRouteFrozen = connection.IsRouteFrozen;
        PropagationLossDbPerCm = connection.PropagationLossDbPerCm;
        BendLossDbPer90Deg = connection.BendLossDbPer90Deg;
        DispersionModel = connection.DispersionModel;
        BendRadiusOverrides.Clear();
        foreach (var (bendIndex, radius) in connection.BendRadiusOverrides)
            BendRadiusOverrides[bendIndex] = radius;
        StraightShiftOffsets.Clear();
        foreach (var (straightIndex, offset) in connection.StraightShiftOffsets)
            StraightShiftOffsets[straightIndex] = offset;
    }

    /// <summary>
    /// Applies the stored routing settings back onto a live connection, used when
    /// the group is expanded again (group edit mode or ungroup). The source-layer
    /// tag is restored too (null when the frozen path carries none, e.g. a
    /// connection routed inside the app).
    /// </summary>
    /// <param name="connection">The live connection to restore settings onto.</param>
    public void ApplySettingsTo(WaveguideConnection connection)
    {
        connection.SourceGdsLayer = Layer;
        connection.SourceGdsDataType = DataType;
        connection.Type = ConnectionType;
        connection.BendRadiusMicrometers = BendRadiusMicrometers;
        connection.WidthMicrometers = WidthMicrometers;
        connection.IsRouteFrozen = IsRouteFrozen;
        connection.PropagationLossDbPerCm = PropagationLossDbPerCm;
        connection.BendLossDbPer90Deg = BendLossDbPer90Deg;
        connection.DispersionModel = DispersionModel;
        connection.BendRadiusOverrides.Clear();
        foreach (var (bendIndex, radius) in BendRadiusOverrides)
            connection.BendRadiusOverrides[bendIndex] = radius;
        connection.StraightShiftOffsets.Clear();
        foreach (var (straightIndex, offset) in StraightShiftOffsets)
            connection.StraightShiftOffsets[straightIndex] = offset;
    }

    /// <summary>
    /// Copies the stored routing settings from another frozen path (deep-copy and
    /// clone support) — including the source-layer tag: group template
    /// instantiation and copy/paste must keep the imported geometry's layer.
    /// </summary>
    /// <param name="source">The frozen path to copy settings from.</param>
    public void CopySettingsFrom(FrozenWaveguidePath source)
    {
        Layer = source.Layer;
        DataType = source.DataType;
        ConnectionType = source.ConnectionType;
        BendRadiusMicrometers = source.BendRadiusMicrometers;
        WidthMicrometers = source.WidthMicrometers;
        IsRouteFrozen = source.IsRouteFrozen;
        PropagationLossDbPerCm = source.PropagationLossDbPerCm;
        BendLossDbPer90Deg = source.BendLossDbPer90Deg;
        DispersionModel = source.DispersionModel;
        BendRadiusOverrides.Clear();
        foreach (var (bendIndex, radius) in source.BendRadiusOverrides)
            BendRadiusOverrides[bendIndex] = radius;
        StraightShiftOffsets.Clear();
        foreach (var (straightIndex, offset) in source.StraightShiftOffsets)
            StraightShiftOffsets[straightIndex] = offset;
    }

    /// <summary>
    /// Amplitude transmission coefficient accounting for propagation and bend loss —
    /// the same loss model WaveguideConnection applies, so freezing a connection does
    /// not silently drop its bend loss. Smooth polyline styles (SBend/Cobra) contain
    /// no bend segments: their bend loss stays approximated as pure propagation loss
    /// over the sampled curve length, exactly like the connection does.
    /// Returns Complex.One when no path is available (conservative, no loss assumed).
    /// Formula: amplitude = 10^(-loss_dB / 20), where
    /// loss_dB = PropagationLossDbPerCm * length_cm + equivalent_90deg_bends * BendLossDbPer90Deg.
    /// </summary>
    public Complex TransmissionCoefficient
    {
        get
        {
            if (Path?.Segments == null || Path.Segments.Count == 0)
                return Complex.One;

            double lengthMicrometers = Path.TotalLengthMicrometers;
            double lengthCm = lengthMicrometers / 10_000.0;
            double lossDb = PropagationLossDbPerCm * lengthCm
                + Path.TotalEquivalent90DegreeBends * BendLossDbPer90Deg;
            double amplitude = Math.Pow(10.0, -lossDb / 20.0);
            return new Complex(amplitude, 0);
        }
    }

    /// <summary>
    /// Effective refractive index at the given wavelength: from the captured
    /// <see cref="DispersionModel"/> when present, otherwise from the PDK dispersion
    /// stamped on an endpoint pin's component (start pin wins, exactly like a freshly
    /// routed connection resolves it — and the state every frozen path is in after a
    /// .lun round-trip, which does not persist the captured model), otherwise
    /// <see cref="WaveguideConnection.DefaultEffectiveIndex"/>.
    /// </summary>
    /// <param name="wavelengthNm">Wavelength in nanometers.</param>
    public double GetEffectiveIndex(double wavelengthNm)
    {
        var dispersion = DispersionModel
            ?? StartPin?.ParentComponent?.WaveguideDispersion
            ?? EndPin?.ParentComponent?.WaveguideDispersion;
        return dispersion?.NEffAt(wavelengthNm) ?? WaveguideConnection.DefaultEffectiveIndex;
    }

    /// <summary>
    /// Loss-only <see cref="TransmissionCoefficient"/> multiplied by the coherent
    /// propagation phase exp(-i·2π·n_eff(λ)·L/λ) accumulated along the frozen path —
    /// the same coefficient a routed connection carries in coherent mode, so grouping
    /// a circuit does not change its interference. The magnitude is unchanged;
    /// only the phase carries the optical path length.
    /// </summary>
    /// <param name="wavelengthNm">Wavelength in nanometers.</param>
    public Complex GetCoherentTransmission(double wavelengthNm)
    {
        if (Path?.Segments == null || Path.Segments.Count == 0)
            return Complex.One;

        double wavelengthMicrometers = wavelengthNm / 1000.0;
        double phaseRadians = -2.0 * Math.PI * GetEffectiveIndex(wavelengthNm)
            * Path.TotalLengthMicrometers / wavelengthMicrometers;
        return TransmissionCoefficient * Complex.Exp(new Complex(0, phaseRadians));
    }

    /// <summary>
    /// Translates all segments in the path by the specified delta.
    /// Used when moving the containing ComponentGroup.
    /// </summary>
    /// <param name="deltaX">X offset in micrometers.</param>
    /// <param name="deltaY">Y offset in micrometers.</param>
    public void TranslateBy(double deltaX, double deltaY)
    {
        if (Path?.Segments == null) return;

        foreach (var segment in Path.Segments)
        {
            segment.StartPoint = (
                segment.StartPoint.X + deltaX,
                segment.StartPoint.Y + deltaY
            );
            segment.EndPoint = (
                segment.EndPoint.X + deltaX,
                segment.EndPoint.Y + deltaY
            );

            // If it's a bend segment, translate the center point as well
            if (segment is BendSegment bend)
            {
                bend.Center = (
                    bend.Center.X + deltaX,
                    bend.Center.Y + deltaY
                );
            }
        }
    }

    /// <summary>
    /// Creates a clone of this frozen path with a new ID.
    /// </summary>
    public object Clone()
    {
        var clone = new FrozenWaveguidePath
        {
            Path = Path.DeepCopy(),
            PathId = Guid.NewGuid(),
            // StartPin and EndPin references must be updated after cloning by the ComponentGroup
        };
        clone.CopySettingsFrom(this);
        return clone;
    }
}

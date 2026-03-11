using System.Numerics;
using CAP_Core.Routing;
using CAP_Core.Components.Core;

namespace CAP_Core.Components.Connections
{
    /// <summary>
    /// Represents a waveguide routing connection between two physical pins.
    /// Automatically calculates transmission coefficient based on geometry and loss parameters.
    /// </summary>
    public class WaveguideConnection
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public PhysicalPin StartPin { get; set; }
        public PhysicalPin EndPin { get; set; }
        public double WidthMicrometers { get; set; } = 0.5; // Standard: 500nm
        public double BendRadiusMicrometers { get; set; } = 10.0;
        public WaveguideType Type { get; set; } = WaveguideType.Auto;

        /// <summary>
        /// Indicates whether this connection is locked (cannot be deleted or modified).
        /// </summary>
        public bool IsLocked { get; set; }

        /// <summary>
        /// Propagation loss in dB per centimeter. Typical values: 1-3 dB/cm for silicon photonics.
        /// </summary>
        public double PropagationLossDbPerCm { get; set; } = 2.0;

        /// <summary>
        /// Loss per 90-degree bend in dB. Typical values: 0.01-0.1 dB per bend.
        /// </summary>
        public double BendLossDbPer90Deg { get; set; } = 0.05;

        /// <summary>
        /// The actual routed path with all segments (straights and bends).
        /// Populated after calling RecalculateTransmission().
        /// </summary>
        public RoutedPath? RoutedPath { get; private set; }

        /// <summary>
        /// Number of equivalent 90-degree bends in the routing.
        /// Calculated from actual path segments.
        /// </summary>
        public double BendCount => RoutedPath?.TotalEquivalent90DegreeBends ?? 0;

        /// <summary>
        /// Calculated path length in micrometers between the two pins.
        /// </summary>
        public double PathLengthMicrometers => RoutedPath?.TotalLengthMicrometers ?? 0;

        /// <summary>
        /// Gets the transmission coefficient calculated from current geometry and loss parameters.
        /// Call RecalculateTransmission() after component positions change.
        /// </summary>
        public Complex TransmissionCoefficient { get; private set; } = Complex.One;

        /// <summary>
        /// Total loss in dB for this connection.
        /// </summary>
        public double TotalLossDb { get; private set; }

        /// <summary>
        /// Shared router instance for all connections.
        /// Public to allow initialization of A* pathfinding grid.
        /// </summary>
        public static WaveguideRouter SharedRouter { get; } = new();

        // Nazca-Export
        public string ExportToNazca()
        {
            var startComp = StartPin.ParentComponent;
            var endComp = EndPin.ParentComponent;
            var startCellName = $"cell_{startComp.PhysicalX}_{startComp.PhysicalY}";
            var endCellName = $"cell_{endComp.PhysicalX}_{endComp.PhysicalY}";

            return Type switch
            {
                WaveguideType.Straight =>
                    $"        ic.strt_p2p(pin1={startCellName}.pin['{StartPin.Name}'], " +
                    $"pin2={endCellName}.pin['{EndPin.Name}']).put()\n",

                WaveguideType.SBend =>
                    $"        ic.sbend_p2p(pin1={startCellName}.pin['{StartPin.Name}'], " +
                    $"pin2={endCellName}.pin['{EndPin.Name}'], " +
                    $"radius={BendRadiusMicrometers}).put()\n",

                _ => // Auto: Nazca wählt automatisch
                    $"        ic.cobra_p2p(pin1={startCellName}.pin['{StartPin.Name}'], " +
                    $"pin2={endCellName}.pin['{EndPin.Name}']).put()\n"
            };
        }

        /// <summary>
        /// Recalculates the transmission coefficient based on current pin positions and loss parameters.
        /// Should be called whenever connected components are moved.
        /// </summary>
        public void RecalculateTransmission()
        {
            if (StartPin == null || EndPin == null)
            {
                RoutedPath = null;
                TransmissionCoefficient = Complex.One;
                TotalLossDb = 0;
                return;
            }

            // Update router settings
            SharedRouter.MinBendRadiusMicrometers = BendRadiusMicrometers;

            // Route the connection
            RoutedPath = SharedRouter.Route(StartPin, EndPin);

            // Calculate total loss from actual path
            double propagationLoss = (PathLengthMicrometers / 10000.0) * PropagationLossDbPerCm; // µm to cm
            double bendLoss = BendCount * BendLossDbPer90Deg;
            TotalLossDb = propagationLoss + bendLoss;

            // Convert dB loss to linear amplitude coefficient
            // Loss in dB = -20 * log10(|amplitude|)
            // |amplitude| = 10^(-loss_dB / 20)
            double amplitudeCoefficient = Math.Pow(10, -TotalLossDb / 20.0);
            TransmissionCoefficient = new Complex(amplitudeCoefficient, 0);
        }

        /// <summary>
        /// Restores a previously cached routed path without invoking the router.
        /// Recalculates transmission loss from the provided path geometry.
        /// Used when loading designs with cached route data.
        /// </summary>
        public void RestoreCachedPath(RoutedPath cachedPath)
        {
            RoutedPath = cachedPath;

            double propagationLoss = (PathLengthMicrometers / 10000.0) * PropagationLossDbPerCm;
            double bendLoss = BendCount * BendLossDbPer90Deg;
            TotalLossDb = propagationLoss + bendLoss;

            double amplitudeCoefficient = Math.Pow(10, -TotalLossDb / 20.0);
            TransmissionCoefficient = new Complex(amplitudeCoefficient, 0);
        }

        /// <summary>
        /// Gets all path segments for rendering or export.
        /// </summary>
        public IReadOnlyList<PathSegment> GetPathSegments()
        {
            return RoutedPath?.Segments ?? new List<PathSegment>();
        }

        /// <summary>
        /// Checks if the routed path is valid.
        /// </summary>
        public bool IsPathValid => RoutedPath?.IsValid ?? false;

        /// <summary>
        /// Indicates if this connection uses a fallback path that goes through obstacles.
        /// When true, the path should be displayed differently (e.g., red/dashed).
        /// </summary>
        public bool IsBlockedFallback => RoutedPath?.IsBlockedFallback ?? false;
    }
}

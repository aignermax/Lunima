using System.Numerics;
using CAP_Core.LightCalculation.BendLoss;

namespace CAP_Core.Components.Connections
{
    /// <summary>
    /// Insertion-loss state of a connection: the radius-dependent bend-loss model, the
    /// itemized breakdown of the current route and the resulting amplitude transmission.
    /// </summary>
    public partial class WaveguideConnection
    {
        /// <summary>
        /// Radius-dependent bend-loss model of this connection's waveguide cross-section,
        /// inherited from the endpoint components' PDK process (see
        /// <see cref="Core.Component.WaveguideBendLoss"/>); <see cref="BendLossPresets.Generic"/>
        /// when no process is known.
        /// </summary>
        public BendLossModel BendLoss { get; set; } = BendLossPresets.Generic;

        /// <summary>
        /// Optional flat extra loss per 90° of turn (dB) on top of the radius model — for a
        /// measured per-bend figure the model does not capture. Default 0: the bend loss comes
        /// from <see cref="BendLoss"/> and therefore depends on the actual radius.
        /// </summary>
        public double ExcessBendLossDbPer90Deg { get; set; }

        /// <summary>Itemized loss (propagation, bends, junctions) of the current route.</summary>
        public ConnectionLossBreakdown LossBreakdown { get; private set; } = ConnectionLossBreakdown.Empty;

        private Complex _transmissionCoefficient = Complex.One;

        /// <summary>
        /// Gets the transmission coefficient calculated from current geometry and loss parameters.
        /// A disconnected preserved import transmits no light. Call RecalculateTransmission()
        /// after component positions change to refresh the geometry-dependent loss.
        /// </summary>
        public Complex TransmissionCoefficient
        {
            get => HasImportedEndpointMismatch ? Complex.Zero : _transmissionCoefficient;
            private set => _transmissionCoefficient = value;
        }

        /// <summary>Total loss in dB for this connection (sum of <see cref="LossBreakdown"/>).</summary>
        public double TotalLossDb { get; private set; }

        /// <summary>
        /// Recalculates <see cref="LossBreakdown"/>, <see cref="TotalLossDb"/> and
        /// <see cref="TransmissionCoefficient"/> from the current <see cref="RoutedPath"/>
        /// geometry with the shared <see cref="ConnectionLossCalculator"/>: propagation over the
        /// path length, radius-dependent radiation in every bend (arcs and sampled smooth
        /// curves alike) and mode-mismatch loss at every curvature junction.
        /// </summary>
        /// <param name="wavelengthNm">Wavelength in nm used when a <see cref="DispersionModel"/> is set.</param>
        public void UpdateLossFromPath(double wavelengthNm = 1550.0)
        {
            double lossDbPerCm = DispersionModel?.LossDbPerCmAt(wavelengthNm) ?? PropagationLossDbPerCm;
            ApplyLossBreakdown(ConnectionLossCalculator.Compute(
                RoutedPath, BendLoss, lossDbPerCm, ExcessBendLossDbPer90Deg));
        }

        private void ApplyLossBreakdown(ConnectionLossBreakdown breakdown)
        {
            LossBreakdown = breakdown;
            TotalLossDb = breakdown.TotalLossDb;
            TransmissionCoefficient = new Complex(breakdown.AmplitudeTransmission, 0);
        }
    }
}

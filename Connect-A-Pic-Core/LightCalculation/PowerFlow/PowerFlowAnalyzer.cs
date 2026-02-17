using System.Numerics;
using CAP_Core.Components;

namespace CAP_Core.LightCalculation.PowerFlow;

/// <summary>
/// Analyzes optical power flow through waveguide connections using simulation results.
/// Maps pin-level light amplitudes to connection-level power values.
/// </summary>
public class PowerFlowAnalyzer
{
    /// <summary>
    /// Threshold in dB below maximum power for fading out connections.
    /// Default: -20 dB.
    /// </summary>
    public double FadeThresholdDb { get; set; } = -20.0;

    /// <summary>
    /// Calculates power flow through all connections based on simulation field results.
    /// </summary>
    /// <param name="connections">The waveguide connections to analyze.</param>
    /// <param name="fieldResults">Pin-level simulation results (Guid to Complex amplitude).</param>
    /// <returns>Power flow result with normalized values for all connections.</returns>
    public PowerFlowResult Analyze(
        IReadOnlyList<WaveguideConnection> connections,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        var flows = new Dictionary<Guid, ConnectionPowerFlow>();

        foreach (var connection in connections)
        {
            var flow = CalculateConnectionFlow(connection, fieldResults);
            flows[connection.Id] = flow;
        }

        return new PowerFlowResult(flows, FadeThresholdDb);
    }

    private static ConnectionPowerFlow CalculateConnectionFlow(
        WaveguideConnection connection,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        var inputAmplitude = GetPinOutputAmplitude(
            connection.StartPin, fieldResults);

        var outputAmplitude = GetPinInputAmplitude(
            connection.EndPin, fieldResults);

        return new ConnectionPowerFlow(
            connection.Id, inputAmplitude, outputAmplitude);
    }

    /// <summary>
    /// Gets the outflow amplitude at a physical pin (light leaving the component).
    /// </summary>
    private static Complex GetPinOutputAmplitude(
        PhysicalPin pin,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        if (pin.LogicalPin == null) return Complex.Zero;

        return fieldResults.TryGetValue(pin.LogicalPin.IDOutFlow, out var value)
            ? value
            : Complex.Zero;
    }

    /// <summary>
    /// Gets the inflow amplitude at a physical pin (light entering the component).
    /// </summary>
    private static Complex GetPinInputAmplitude(
        PhysicalPin pin,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        if (pin.LogicalPin == null) return Complex.Zero;

        return fieldResults.TryGetValue(pin.LogicalPin.IDInFlow, out var value)
            ? value
            : Complex.Zero;
    }
}

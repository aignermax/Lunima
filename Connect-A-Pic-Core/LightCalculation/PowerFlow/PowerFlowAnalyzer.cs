using System.Numerics;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.LightCalculation.PowerFlow;

/// <summary>
/// Analyzes optical power flow through waveguide connections using simulation results.
/// Maps pin-level light amplitudes to connection-level power values.
/// </summary>
public class PowerFlowAnalyzer
{
    /// <summary>
    /// Threshold in dB below maximum power for fading out connections.
    /// Default: -40 dB (shows most connections, only extremely weak signals are faded).
    /// </summary>
    public double FadeThresholdDb { get; set; } = -40.0;

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
        return CalculatePinPairFlow(connection.Id, connection.StartPin, connection.EndPin, fieldResults);
    }

    /// <summary>
    /// Calculates power flow between a pair of physical pins.
    /// </summary>
    private static ConnectionPowerFlow CalculatePinPairFlow(
        Guid flowId,
        PhysicalPin startPin,
        PhysicalPin endPin,
        IReadOnlyDictionary<Guid, Complex> fieldResults)
    {
        // Check both directions: waveguide connections are bidirectional.
        var forwardInput = GetPinOutputAmplitude(startPin, fieldResults);
        var forwardOutput = GetPinInputAmplitude(endPin, fieldResults);

        var reverseInput = GetPinOutputAmplitude(endPin, fieldResults);
        var reverseOutput = GetPinInputAmplitude(startPin, fieldResults);

        var forwardPower = forwardInput.Magnitude * forwardInput.Magnitude
                         + forwardOutput.Magnitude * forwardOutput.Magnitude;
        var reversePower = reverseInput.Magnitude * reverseInput.Magnitude
                         + reverseOutput.Magnitude * reverseOutput.Magnitude;

        var inputAmplitude = forwardPower >= reversePower ? forwardInput : reverseInput;
        var outputAmplitude = forwardPower >= reversePower ? forwardOutput : reverseOutput;

        return new ConnectionPowerFlow(flowId, inputAmplitude, outputAmplitude);
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

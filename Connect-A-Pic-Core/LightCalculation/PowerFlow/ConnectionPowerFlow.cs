using System.Numerics;

namespace CAP_Core.LightCalculation.PowerFlow;

/// <summary>
/// Represents the calculated power flow through a single waveguide connection.
/// </summary>
public class ConnectionPowerFlow
{
    /// <summary>
    /// The connection ID this power flow corresponds to.
    /// </summary>
    public Guid ConnectionId { get; }

    /// <summary>
    /// Complex amplitude of the light signal entering the connection.
    /// </summary>
    public Complex InputAmplitude { get; }

    /// <summary>
    /// Complex amplitude of the light signal exiting the connection.
    /// </summary>
    public Complex OutputAmplitude { get; }

    /// <summary>
    /// Optical power at the input in linear scale (|amplitude|^2).
    /// </summary>
    public double InputPower => InputAmplitude.Magnitude * InputAmplitude.Magnitude;

    /// <summary>
    /// Optical power at the output in linear scale (|amplitude|^2).
    /// </summary>
    public double OutputPower => OutputAmplitude.Magnitude * OutputAmplitude.Magnitude;

    /// <summary>
    /// Average power through the connection in linear scale.
    /// </summary>
    public double AveragePower => (InputPower + OutputPower) / 2.0;

    /// <summary>
    /// Average power in dB relative to the maximum power in the system.
    /// Set by <see cref="PowerFlowResult"/> after normalization.
    /// </summary>
    public double NormalizedPowerDb { get; internal set; }

    /// <summary>
    /// Power fraction in range [0, 1] relative to the maximum power in the system.
    /// 1.0 = maximum power, 0.0 = no power.
    /// </summary>
    public double NormalizedPowerFraction { get; internal set; }

    /// <summary>
    /// Creates a new connection power flow result.
    /// </summary>
    public ConnectionPowerFlow(
        Guid connectionId,
        Complex inputAmplitude,
        Complex outputAmplitude)
    {
        ConnectionId = connectionId;
        InputAmplitude = inputAmplitude;
        OutputAmplitude = outputAmplitude;
    }
}

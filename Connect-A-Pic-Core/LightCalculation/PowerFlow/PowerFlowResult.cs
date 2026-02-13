namespace CAP_Core.LightCalculation.PowerFlow;

/// <summary>
/// Contains power flow analysis results for all connections in the system.
/// </summary>
public class PowerFlowResult
{
    /// <summary>
    /// Power flow data for each connection, keyed by connection ID.
    /// </summary>
    public IReadOnlyDictionary<Guid, ConnectionPowerFlow> ConnectionFlows { get; }

    /// <summary>
    /// Maximum average power across all connections (linear scale).
    /// Used for normalization.
    /// </summary>
    public double MaxPower { get; }

    /// <summary>
    /// Threshold in dB below maximum. Connections below this are considered faded.
    /// </summary>
    public double FadeThresholdDb { get; }

    /// <summary>
    /// Creates a power flow result and normalizes all connection power values.
    /// </summary>
    public PowerFlowResult(
        IReadOnlyDictionary<Guid, ConnectionPowerFlow> connectionFlows,
        double fadeThresholdDb = -20.0)
    {
        ConnectionFlows = connectionFlows;
        FadeThresholdDb = fadeThresholdDb;

        MaxPower = connectionFlows.Count > 0
            ? connectionFlows.Values.Max(f => f.AveragePower)
            : 0;

        NormalizeConnectionPowers();
    }

    /// <summary>
    /// Checks whether a connection should be faded out (below threshold).
    /// </summary>
    public bool IsFadedOut(Guid connectionId)
    {
        if (!ConnectionFlows.TryGetValue(connectionId, out var flow))
            return true;

        return flow.NormalizedPowerDb < FadeThresholdDb;
    }

    private void NormalizeConnectionPowers()
    {
        if (MaxPower <= 0) return;

        foreach (var flow in ConnectionFlows.Values)
        {
            flow.NormalizedPowerFraction = flow.AveragePower / MaxPower;

            flow.NormalizedPowerDb = flow.AveragePower > 0
                ? 10.0 * Math.Log10(flow.AveragePower / MaxPower)
                : double.NegativeInfinity;
        }
    }
}

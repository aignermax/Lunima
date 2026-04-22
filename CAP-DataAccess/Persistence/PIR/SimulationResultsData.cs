namespace CAP_DataAccess.Persistence.PIR;

/// <summary>
/// Stores simulation results persisted in the .lun PIR format.
/// Contains the most recent run and any stored parameter sweep results.
/// </summary>
public class SimulationResultsData
{
    /// <summary>
    /// Results from the last simulation run, or null if no simulation has been run.
    /// </summary>
    public SimulationRunData? LastRun { get; set; }

    /// <summary>
    /// Stored parameter sweep results. May be null or empty if no sweeps have been saved.
    /// </summary>
    public List<ParameterSweepResultData>? ParameterSweeps { get; set; }
}

/// <summary>
/// Results of a single light simulation run.
/// </summary>
public class SimulationRunData
{
    /// <summary>
    /// ISO 8601 timestamp of when this simulation was run (UTC).
    /// Example: "2024-01-15T10:30:00Z"
    /// </summary>
    public string Timestamp { get; set; } = "";

    /// <summary>
    /// Wavelength used for the simulation, in nanometers.
    /// </summary>
    public int WavelengthNm { get; set; }

    /// <summary>
    /// Power flow results per connection, keyed by connection Guid string.
    /// </summary>
    public Dictionary<string, ConnectionPowerFlowData> PowerFlow { get; set; } = new();
}

/// <summary>
/// Power flow result for a single waveguide connection.
/// </summary>
public class ConnectionPowerFlowData
{
    /// <summary>
    /// Input optical power at the start pin (linear scale, normalized to max source power).
    /// </summary>
    public double InputPower { get; set; }

    /// <summary>
    /// Output optical power at the end pin (linear scale, normalized to max source power).
    /// </summary>
    public double OutputPower { get; set; }

    /// <summary>
    /// Power normalized relative to the maximum power in the circuit, in dB.
    /// 0 dB = maximum, negative values indicate relative loss.
    /// </summary>
    public double NormalizedPowerDb { get; set; }
}

/// <summary>
/// Results of a stored parameter sweep.
/// </summary>
public class ParameterSweepResultData
{
    /// <summary>
    /// Human-readable name of the swept parameter (e.g., "SliderValue", "LaserPower").
    /// </summary>
    public string ParameterName { get; set; } = "";

    /// <summary>
    /// Identifier of the component whose parameter was swept.
    /// </summary>
    public string ComponentIdentifier { get; set; } = "";

    /// <summary>
    /// The parameter values that were evaluated during the sweep.
    /// </summary>
    public List<double> ParameterValues { get; set; } = new();

    /// <summary>
    /// Output power (linear, normalized) at the target connection for each parameter value.
    /// Length must match ParameterValues.
    /// </summary>
    public List<double> OutputPowers { get; set; } = new();

    /// <summary>
    /// Guid string of the connection used as the sweep output measurement point.
    /// </summary>
    public string TargetConnectionId { get; set; } = "";

    /// <summary>
    /// Wavelength used during the sweep, in nanometers.
    /// </summary>
    public int WavelengthNm { get; set; }

    /// <summary>
    /// ISO 8601 timestamp of when this sweep was run (UTC).
    /// </summary>
    public string? Timestamp { get; set; }
}

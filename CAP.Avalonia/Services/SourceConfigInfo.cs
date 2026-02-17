namespace CAP.Avalonia.Services;

/// <summary>
/// Records the configuration applied to a single light source during simulation.
/// </summary>
public class SourceConfigInfo
{
    /// <summary>
    /// Component identifier of the light source.
    /// </summary>
    public string ComponentId { get; }

    /// <summary>
    /// Wavelength in nanometers used for this source.
    /// </summary>
    public int WavelengthNm { get; }

    /// <summary>
    /// Input power (linear, 0.0 to 1.0) used for this source.
    /// </summary>
    public double InputPower { get; }

    /// <summary>
    /// Creates a source configuration record.
    /// </summary>
    public SourceConfigInfo(string componentId, int wavelengthNm, double inputPower)
    {
        ComponentId = componentId;
        WavelengthNm = wavelengthNm;
        InputPower = inputPower;
    }
}

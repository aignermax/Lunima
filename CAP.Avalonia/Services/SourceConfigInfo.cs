namespace CAP.Avalonia.Services;

/// <summary>
/// Records the configuration applied to a single light source during simulation.
/// </summary>
public class SourceConfigInfo
{
    public string ComponentId { get; }
    public int WavelengthNm { get; }
    public double InputPower { get; }

    public SourceConfigInfo(string componentId, int wavelengthNm, double inputPower)
    {
        ComponentId = componentId;
        WavelengthNm = wavelengthNm;
        InputPower = inputPower;
    }
}

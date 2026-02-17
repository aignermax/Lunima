using CAP_Core.Components.ComponentHelpers;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Represents an available wavelength selection for laser configuration.
/// </summary>
public class WavelengthOption
{
    /// <summary>
    /// Wavelength in nanometers.
    /// </summary>
    public int WavelengthNm { get; }

    /// <summary>
    /// Display label (e.g. "1550nm (Red)").
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Creates a wavelength option.
    /// </summary>
    public WavelengthOption(int wavelengthNm, string label)
    {
        WavelengthNm = wavelengthNm;
        Label = label;
    }

    /// <summary>
    /// All available wavelength options.
    /// </summary>
    public static IReadOnlyList<WavelengthOption> All { get; } = new[]
    {
        new WavelengthOption(StandardWaveLengths.RedNM, "1550nm (Red)"),
        new WavelengthOption(StandardWaveLengths.GreenNM, "1310nm (Green)"),
        new WavelengthOption(StandardWaveLengths.BlueNM, "980nm (Blue)")
    };

    /// <summary>
    /// Gets the display label for a given wavelength value.
    /// </summary>
    public static string GetLabel(int wavelengthNm)
    {
        foreach (var opt in All)
        {
            if (opt.WavelengthNm == wavelengthNm)
                return opt.Label;
        }
        return $"{wavelengthNm}nm";
    }

    /// <inheritdoc />
    public override string ToString() => Label;
}

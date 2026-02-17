using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Components.ComponentHelpers;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Configuration for a laser light source (wavelength and input power).
/// Applied per light source component (Grating Coupler, Edge Coupler).
/// </summary>
public partial class LaserConfig : ObservableObject
{
    /// <summary>
    /// Selected wavelength in nanometers.
    /// </summary>
    [ObservableProperty]
    private int _wavelengthNm = StandardWaveLengths.RedNM;

    /// <summary>
    /// Optical input power (linear, 0.0 to 1.0).
    /// </summary>
    [ObservableProperty]
    private double _inputPower = 1.0;

    /// <summary>
    /// Display label for the selected wavelength.
    /// </summary>
    public string WavelengthLabel => WavelengthOption.GetLabel(WavelengthNm);

    partial void OnWavelengthNmChanged(int value)
    {
        OnPropertyChanged(nameof(WavelengthLabel));
    }
}

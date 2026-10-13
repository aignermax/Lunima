using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.MakeGate;

/// <summary>
/// One simulated row of the Make-gate panel: input bits → output power drawn as a
/// small bar with the threshold marked on it, the bit that power reads as, and the
/// bit the intended function expects. Re-classifies live when the threshold moves —
/// the powers stay, no new simulation runs.
/// </summary>
public partial class GateResultRowViewModel : ObservableObject
{
    /// <summary>Width of a full-scale power bar in pixels.</summary>
    public const double MaxBarWidth = 120.0;

    private readonly double _fullScalePower;

    /// <summary>Creates the row.</summary>
    /// <param name="inputBitsText">Input bits in header order, e.g. "0 1".</param>
    /// <param name="outputName">Short name of the output this row measures.</param>
    /// <param name="power">Simulated normalized output power.</param>
    /// <param name="fullScalePower">Power drawn as a full bar (the brightest row).</param>
    public GateResultRowViewModel(string inputBitsText, string outputName, double power, double fullScalePower)
    {
        InputBitsText = inputBitsText;
        OutputName = outputName;
        Power = power;
        _fullScalePower = fullScalePower > 0 ? fullScalePower : 1.0;
    }

    /// <summary>Input bits in header order.</summary>
    public string InputBitsText { get; }

    /// <summary>Short name of the measured output.</summary>
    public string OutputName { get; }

    /// <summary>Simulated normalized output power.</summary>
    public double Power { get; }

    /// <summary>Power with three decimals.</summary>
    public string PowerText => Power.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>Bar width in pixels, proportional to the power.</summary>
    public double BarWidth => ScaleToBar(Power);

    /// <summary>True when the power reaches the current threshold (reads 1).</summary>
    [ObservableProperty]
    private bool _isOne;

    /// <summary>Bit the intended function expects; null for a custom gate.</summary>
    [ObservableProperty]
    private bool? _expectedBit;

    /// <summary>Horizontal position of the threshold marker on the bar in pixels.</summary>
    [ObservableProperty]
    private double _thresholdMarkerX;

    /// <summary>The read bit as text.</summary>
    public string BitText => IsOne ? "1" : "0";

    /// <summary>True when the row reads what the intended function expects (always true for custom).</summary>
    public bool Matches => ExpectedBit == null || ExpectedBit == IsOne;

    /// <summary>Re-reads the bit and moves the threshold marker for a new threshold.</summary>
    /// <param name="threshold">The analog→digital threshold.</param>
    public void Classify(double threshold)
    {
        IsOne = Power >= threshold;
        ThresholdMarkerX = ScaleToBar(threshold);
    }

    private double ScaleToBar(double power) => Math.Clamp(power / _fullScalePower, 0.0, 1.0) * MaxBarWidth;

    partial void OnIsOneChanged(bool value)
    {
        OnPropertyChanged(nameof(BitText));
        OnPropertyChanged(nameof(Matches));
    }

    partial void OnExpectedBitChanged(bool? value) => OnPropertyChanged(nameof(Matches));
}

using CAP_Core.Analysis.WavelengthSpectrum;

namespace CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;

/// <summary>
/// One FSR readout line shown under the spectrum plot (#1382): the curve's
/// legend label, the extracted free spectral range and the localized display
/// text. Structured values stay accessible so tests can assert the number
/// instead of parsing the string.
/// </summary>
public sealed class FsrReadoutLine
{
    /// <summary>Legend label of the analyzed curve (e.g. "In → Through.port 2").</summary>
    public string CurveLabel { get; }

    /// <summary>Mean free spectral range in nm.</summary>
    public double MeanFsrNm { get; }

    /// <summary>Number of extrema (dips or peaks) the FSR was extracted from.</summary>
    public int ExtremumCount { get; }

    /// <summary>Whether the fringes are dips or peaks in the plotted curve.</summary>
    public SpectrumExtremumKind ExtremumKind { get; }

    /// <summary>Localized one-line readout shown under the plot.</summary>
    public string Text { get; }

    /// <summary>Creates a readout line.</summary>
    public FsrReadoutLine(
        string curveLabel,
        double meanFsrNm,
        int extremumCount,
        SpectrumExtremumKind extremumKind,
        string text)
    {
        CurveLabel = curveLabel;
        MeanFsrNm = meanFsrNm;
        ExtremumCount = extremumCount;
        ExtremumKind = extremumKind;
        Text = text;
    }
}

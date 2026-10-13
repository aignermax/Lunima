namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// The tabs of the analysis dock. Each value is the tab's index in
/// <c>AnalysisDockPanel.axaml</c>, so <c>(int)tab</c> is a valid
/// <see cref="AnalysisDockViewModel.SelectedTabIndex"/>.
/// </summary>
public enum AnalysisDockTab
{
    /// <summary>Time-domain (transient) analysis.</summary>
    Transient = 0,

    /// <summary>Eye diagram / bit-error-rate analysis.</summary>
    EyeBer = 1,

    /// <summary>Transmission-vs-wavelength spectrum.</summary>
    Spectrum = 2,

    /// <summary>Monte-Carlo fabrication variance.</summary>
    MonteCarlo = 3,

    /// <summary>Circuit optimization over the slider parameters.</summary>
    Optimization = 4,

    /// <summary>Parameter sweep.</summary>
    Sweep = 5,

    /// <summary>DRC-lite design checks.</summary>
    Checks = 6,

    /// <summary>Logic network: build, toggle, clock.</summary>
    Logic = 7,
}

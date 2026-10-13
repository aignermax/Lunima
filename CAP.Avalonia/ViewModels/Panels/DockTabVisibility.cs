using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// Which analysis-dock tab headers are shown. One bindable flag per
/// <see cref="AnalysisDockTab"/>; <see cref="AnalysisDockViewModel"/> recomputes them
/// whenever the workspace, the "show all tabs" toggle, or the selected tab changes.
/// </summary>
public partial class DockTabVisibility : ObservableObject
{
    [ObservableProperty] private bool _transient = true;
    [ObservableProperty] private bool _eyeBer = true;
    [ObservableProperty] private bool _spectrum = true;
    [ObservableProperty] private bool _monteCarlo = true;
    [ObservableProperty] private bool _optimization = true;
    [ObservableProperty] private bool _sweep = true;
    [ObservableProperty] private bool _checks = true;
    [ObservableProperty] private bool _netlist = true;
    [ObservableProperty] private bool _logic = true;

    /// <summary>Sets every flag from <paramref name="isShown"/>.</summary>
    public void Apply(Func<AnalysisDockTab, bool> isShown)
    {
        Transient = isShown(AnalysisDockTab.Transient);
        EyeBer = isShown(AnalysisDockTab.EyeBer);
        Spectrum = isShown(AnalysisDockTab.Spectrum);
        MonteCarlo = isShown(AnalysisDockTab.MonteCarlo);
        Optimization = isShown(AnalysisDockTab.Optimization);
        Sweep = isShown(AnalysisDockTab.Sweep);
        Checks = isShown(AnalysisDockTab.Checks);
        Netlist = isShown(AnalysisDockTab.Netlist);
        Logic = isShown(AnalysisDockTab.Logic);
    }

    /// <summary>Whether the header of <paramref name="tab"/> is currently shown.</summary>
    public bool IsShown(AnalysisDockTab tab) => tab switch
    {
        AnalysisDockTab.Transient => Transient,
        AnalysisDockTab.EyeBer => EyeBer,
        AnalysisDockTab.Spectrum => Spectrum,
        AnalysisDockTab.MonteCarlo => MonteCarlo,
        AnalysisDockTab.Optimization => Optimization,
        AnalysisDockTab.Sweep => Sweep,
        AnalysisDockTab.Checks => Checks,
        AnalysisDockTab.Netlist => Netlist,
        AnalysisDockTab.Logic => Logic,
        _ => false,
    };
}

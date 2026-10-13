using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.AnalysisReadiness;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>Bottom analysis dock: collapsible host for the Transient, Eye/BER and Spectrum tabs (#570/#535/#816).</summary>
public partial class AnalysisDockViewModel : ObservableObject
{
    /// <summary>Transient (time-domain) analysis tab.</summary>
    public TimeDomainViewModel Transient { get; }

    /// <summary>Eye-diagram / BER analysis tab.</summary>
    public EyeDiagramViewModel Eye { get; }

    /// <summary>Transmission-vs-wavelength spectrum tab.</summary>
    public WavelengthSpectrumViewModel Spectrum { get; }

    /// <summary>Monte-Carlo fabrication-variance tab.</summary>
    public MonteCarloViewModel MonteCarlo { get; }

    /// <summary>
    /// Circuit-optimization tab: end-of-design tuning pass over all slider
    /// parameters — a whole-circuit workflow like the sibling analysis tabs,
    /// deliberately NOT a per-component properties section.
    /// </summary>
    public CircuitOptimizationViewModel Optimization { get; }

    /// <summary>Shared analysis-output header (#754): shows/picks/clears THE output coupler.</summary>
    public AnalysisOutputPanelViewModel Output { get; }

    /// <summary>Why an empty optical tab cannot show results yet, and the one-click fix.</summary>
    public AnalysisReadinessViewModel Readiness { get; }

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Expert override: shows every tab header regardless of the active workspace, so no
    /// analysis is ever out of reach.
    /// </summary>
    [ObservableProperty]
    private bool _showAllTabs;

    /// <summary>Which tab headers are currently shown (bound by the dock's TabItems).</summary>
    public DockTabVisibility Tabs { get; } = new();

    private IReadOnlyCollection<AnalysisDockTab> _workspaceTabs = Enum.GetValues<AnalysisDockTab>();

    /// <summary>Index of the Design Checks tab in the dock (tab order in AnalysisDockPanel.axaml).</summary>
    private const int ChecksTabIndex = (int)AnalysisDockTab.Checks;

    /// <summary>Index of the Logic tab in the dock (tab order in AnalysisDockPanel.axaml).</summary>
    private const int LogicTabIndex = (int)AnalysisDockTab.Logic;

    /// <summary>Minimum dock content height (px) when dragging the resize grip.</summary>
    public const double MinDockHeight = 120;

    /// <summary>Maximum dock content height (px) when dragging the resize grip.</summary>
    public const double MaxDockHeight = 640;

    /// <summary>User-adjustable height (px) of the dock's content area; driven by the resize grip.</summary>
    [ObservableProperty]
    private double _dockHeight = 260;

    /// <summary>Sets <see cref="DockHeight"/> to <paramref name="height"/>, clamped to
    /// [<see cref="MinDockHeight"/>, <see cref="MaxDockHeight"/>].</summary>
    public void SetDockHeight(double height) =>
        DockHeight = System.Math.Clamp(height, MinDockHeight, MaxDockHeight);

    /// <summary>Initializes a new instance of <see cref="AnalysisDockViewModel"/>.</summary>
    /// <param name="transient">Transient (time-domain) analysis tab ViewModel.</param>
    /// <param name="eye">Eye-diagram / BER analysis tab ViewModel.</param>
    /// <param name="spectrum">Transmission spectrum tab ViewModel (#816).</param>
    /// <param name="output">Shared analysis-output header ViewModel (#754).</param>
    /// <param name="monteCarlo">Monte-Carlo fabrication-variance tab ViewModel (#818).</param>
    public AnalysisDockViewModel(
        TimeDomainViewModel transient, EyeDiagramViewModel eye,
        WavelengthSpectrumViewModel spectrum, AnalysisOutputPanelViewModel output,
        MonteCarloViewModel monteCarlo, CircuitOptimizationViewModel optimization)
    {
        Transient = transient;
        Eye = eye;
        Spectrum = spectrum;
        Output = output;
        MonteCarlo = monteCarlo;
        Optimization = optimization;
        Readiness = new AnalysisReadinessViewModel(output);
    }

    /// <summary>Wires all tabs and the shared output header to the active design canvas.</summary>
    public void Configure(DesignCanvasViewModel canvas)
    {
        Transient.Configure(canvas);
        Eye.Configure(canvas);
        Spectrum.Configure(canvas);
        Output.Configure(canvas);
        MonteCarlo.Configure(canvas);
        Optimization.ConfigureForCanvas(canvas);
        Readiness.Configure(canvas);
    }

    /// <summary>Opens the dock on the Transient tab (called when Run is invoked in Transient mode).</summary>
    public void OpenTransient()
    {
        SelectedTabIndex = 0;
        IsVisible = true;
    }

    /// <summary>Opens the dock on the Design Checks tab (called by the "Check design" menu entry).</summary>
    public void OpenChecks()
    {
        SelectedTabIndex = ChecksTabIndex;
        IsVisible = true;
    }

    /// <summary>Opens the dock on the Logic tab (called when the "Watch it compute" tour starts).</summary>
    public void OpenLogic()
    {
        SelectedTabIndex = LogicTabIndex;
        IsVisible = true;
    }

    /// <summary>
    /// Restricts the shown tab headers to <paramref name="tabs"/> (the active workspace's
    /// set). When the selected tab falls outside the set, the first tab of the set is
    /// selected. The selected tab always stays shown, so a programmatic jump (a tour
    /// opening the Logic tab, the "Check design" menu) never lands on a hidden tab.
    /// </summary>
    public void ApplyWorkspaceTabs(IReadOnlyList<AnalysisDockTab> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        _workspaceTabs = tabs;
        if (!ShowAllTabs && tabs.Count > 0 && !tabs.Contains((AnalysisDockTab)SelectedTabIndex))
            SelectedTabIndex = (int)tabs[0];
        RefreshTabVisibility();
    }

    /// <summary>Opens the dock on <paramref name="tab"/>.</summary>
    public void Open(AnalysisDockTab tab)
    {
        SelectedTabIndex = (int)tab;
        IsVisible = true;
    }

    partial void OnSelectedTabIndexChanged(int value) => RefreshTabVisibility();

    partial void OnShowAllTabsChanged(bool value) => RefreshTabVisibility();

    private void RefreshTabVisibility() =>
        Tabs.Apply(tab => ShowAllTabs || _workspaceTabs.Contains(tab) || (int)tab == SelectedTabIndex);

    /// <summary>Toggles the dock's visibility.</summary>
    [RelayCommand]
    private void Toggle() => IsVisible = !IsVisible;
}

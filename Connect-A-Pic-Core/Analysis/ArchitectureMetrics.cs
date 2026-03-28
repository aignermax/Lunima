namespace CAP_Core.Analysis;

/// <summary>
/// Static snapshot of Connect-A-PIC-Pro architectural health metrics.
/// Updated each time a formal architecture analysis is conducted.
/// These values capture the state measured during Issue #320 (2026-03-28).
/// </summary>
public class ArchitectureMetrics
{
    /// <summary>Gets the total number of feature ViewModels in the application.</summary>
    public int ViewModelCount { get; init; }

    /// <summary>Gets the number of services registered in the DI container (App.axaml.cs).</summary>
    public int DiRegistrationCount { get; init; }

    /// <summary>Gets the total number of unit/integration test files.</summary>
    public int TestFileCount { get; init; }

    /// <summary>Gets the line count of MainViewModel.cs.</summary>
    public int MainViewModelLines { get; init; }

    /// <summary>Gets the line count of MainWindow.axaml.</summary>
    public int MainWindowLines { get; init; }

    /// <summary>Gets the combined line count of all DesignCanvasViewModel partial files.</summary>
    public int DesignCanvasViewModelLines { get; init; }

    /// <summary>
    /// Gets the architectural maturity score on a 1–5 scale.
    /// 1 = ad-hoc, 3 = functional, 5 = exemplary.
    /// </summary>
    public int MaturityScore { get; init; }

    /// <summary>Gets whether a full PRISM framework migration is recommended at this time.</summary>
    public bool PrismMigrationRecommended { get; init; }

    /// <summary>Gets the top prioritized architectural recommendations (ordered by impact).</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates the current architecture metrics snapshot based on Issue #320 analysis.
    /// </summary>
    /// <returns>A populated <see cref="ArchitectureMetrics"/> instance.</returns>
    public static ArchitectureMetrics Current() => new()
    {
        ViewModelCount = 28,
        DiRegistrationCount = 14,
        TestFileCount = 163,
        MainViewModelLines = 654,
        MainWindowLines = 1117,
        DesignCanvasViewModelLines = 1562,
        MaturityScore = 4,
        PrismMigrationRecommended = false,
        Recommendations = new[]
        {
            "Extract MainWindow.axaml panels into UserControls (1117 lines is too large for one file)",
            "Remove backward-compatibility delegates from MainViewModel once AXAML bindings are updated",
            "Split DesignCanvasViewModel (1562 lines) into focused sub-ViewModels per responsibility",
            "Hybrid modularization (Option C) is the recommended path — PRISM is not needed yet",
        }
    };
}

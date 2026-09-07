using System.Collections.ObjectModel;
using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComponentGroup = CAP_Core.Components.Core.ComponentGroup;
using Component = CAP_Core.Components.Core.Component;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// ViewModel for the Design Checks panel.
/// Validates waveguide connections and provides navigation between issues.
/// </summary>
public partial class DesignValidationViewModel : ObservableObject
{
    private const double NavigationPaddingMicrometers = 200;

    private readonly DesignValidator _validator = new();

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private int _currentIndex = -1;

    [ObservableProperty]
    private bool _hasIssues;

    /// <summary>
    /// The list of design issues found during the last validation run.
    /// </summary>
    public ObservableCollection<DesignIssue> Issues { get; } = new();

    /// <summary>
    /// Callback to pan/zoom the canvas to a specific coordinate.
    /// Set by MainViewModel to wire up canvas navigation.
    /// Parameters: (centerX, centerY) in micrometers.
    /// </summary>
    public Action<double, double>? NavigateToPosition { get; set; }

    /// <summary>
    /// Callback to highlight a specific connection on the canvas.
    /// Set by MainViewModel. Parameter: the connection to highlight.
    /// </summary>
    public Action<WaveguideConnection?>? HighlightConnection { get; set; }

    /// <summary>
    /// Gets a display string for the current navigation position.
    /// </summary>
    public string NavigationText => Issues.Count == 0
        ? "No issues"
        : $"{CurrentIndex + 1} / {Issues.Count}";

    /// <summary>
    /// Runs design validation on the provided connections.
    /// Detects invalid geometry, blocked paths, overlaps with frozen group paths,
    /// per-connection pin width/layer mismatches, (when components are provided) dangling
    /// optical pins, (when a positive minimum spacing is provided) waveguides closer than
    /// the process minimum, (when min-width rules are provided) waveguides narrower than
    /// the fabrication minimum of their cross-section, (when chip bounds are provided)
    /// out-of-bounds component placement, and (when PDK data is provided) placed
    /// components whose PDK no longer matches the active process.
    /// </summary>
    /// <param name="connections">Waveguide connections to validate.</param>
    /// <param name="groups">ComponentGroups whose frozen paths are checked for overlap. Optional.</param>
    /// <param name="allComponents">All placed components checked for dangling pins, chip bounds and PDK compatibility. Optional.</param>
    /// <param name="chipWidthMicrometers">Chip boundary width; ignored when ≤0. Optional.</param>
    /// <param name="chipHeightMicrometers">Chip boundary height; ignored when ≤0. Optional.</param>
    /// <param name="pdkSourceByComponent">Each component's resolved PDK source name. Optional — skips the PDK check when absent.</param>
    /// <param name="processAgnosticPdkNames">PDK names exempt from process enforcement (tool libraries). Optional.</param>
    /// <param name="enabledPdkNames">PDK names currently allowed under the active process lock. Optional — skips the PDK check when absent.</param>
    /// <param name="processLockActive">Whether a real (non-Playground) fabrication process is active.</param>
    /// <param name="externalPortPins">Pins treated as external ports; exempt from the dangling-pin check. Optional.</param>
    /// <param name="minWaveguideSpacingMicrometers">Process minimum edge-to-edge waveguide spacing; ≤0 disables the spacing check. Optional.</param>
    /// <param name="minWaveguideWidthRules">Per-cross-section minimum feature widths of the active process; null/empty disables the min-width check. Optional.</param>
    /// <param name="connectionDrcRuleProvider">
    /// Optional per-connection DRC rule-set resolver (issue #936): when wired, each
    /// connection's width/spacing limits come from its own endpoint PDKs' processes —
    /// per-chiplet limits on a multi-process canvas and PDK rules even in Playground —
    /// instead of the design-wide values above. Optional.
    /// </param>
    public void RunValidation(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup>? groups = null,
        IEnumerable<Component>? allComponents = null,
        double chipWidthMicrometers = 0,
        double chipHeightMicrometers = 0,
        IReadOnlyDictionary<Component, string?>? pdkSourceByComponent = null,
        IReadOnlyCollection<string>? processAgnosticPdkNames = null,
        IReadOnlyCollection<string>? enabledPdkNames = null,
        bool processLockActive = true,
        IEnumerable<PhysicalPin>? externalPortPins = null,
        double minWaveguideSpacingMicrometers = 0,
        IReadOnlyList<WaveguideMinWidthRule>? minWaveguideWidthRules = null,
        Func<WaveguideConnection, ConnectionDrcRules?>? connectionDrcRuleProvider = null)
    {
        var request = new DesignValidationRequest(
            connections, groups, allComponents,
            chipWidthMicrometers, chipHeightMicrometers,
            pdkSourceByComponent, processAgnosticPdkNames, enabledPdkNames,
            processLockActive, externalPortPins,
            minWaveguideSpacingMicrometers, minWaveguideWidthRules,
            connectionDrcRuleProvider);
        BeginValidation();
        CommitIssues(ComputeIssues(request));
    }

    /// <summary>
    /// Async variant of <see cref="RunValidation"/> for the 100 ms UI-responsiveness
    /// budget (issue #1150): the three <see cref="DesignValidator"/> passes run on a
    /// worker thread while the <see cref="Issues"/> reset and result commit stay on the
    /// caller's (UI) thread. On a loaded logic-gate example the synchronous prefix of
    /// the old all-in-one path exceeded the budget by 6×.
    /// </summary>
    public async Task RunValidationAsync(
        IEnumerable<WaveguideConnection> connections,
        IEnumerable<ComponentGroup>? groups = null,
        IEnumerable<Component>? allComponents = null,
        double chipWidthMicrometers = 0,
        double chipHeightMicrometers = 0,
        IReadOnlyDictionary<Component, string?>? pdkSourceByComponent = null,
        IReadOnlyCollection<string>? processAgnosticPdkNames = null,
        IReadOnlyCollection<string>? enabledPdkNames = null,
        bool processLockActive = true,
        IEnumerable<PhysicalPin>? externalPortPins = null,
        double minWaveguideSpacingMicrometers = 0,
        IReadOnlyList<WaveguideMinWidthRule>? minWaveguideWidthRules = null,
        Func<WaveguideConnection, ConnectionDrcRules?>? connectionDrcRuleProvider = null,
        CancellationToken cancellationToken = default)
    {
        var request = new DesignValidationRequest(
            connections, groups, allComponents,
            chipWidthMicrometers, chipHeightMicrometers,
            pdkSourceByComponent, processAgnosticPdkNames, enabledPdkNames,
            processLockActive, externalPortPins,
            minWaveguideSpacingMicrometers, minWaveguideWidthRules,
            connectionDrcRuleProvider);
        BeginValidation();
        var issues = await Task.Run(() => ComputeIssues(request), cancellationToken);
        CommitIssues(issues);
    }

    /// <summary>
    /// Resets the panel for a new validation run. Must run on the UI thread — touches
    /// <see cref="Issues"/> and fires <see cref="HighlightConnection"/>.
    /// </summary>
    private void BeginValidation()
    {
        Issues.Clear();
        CurrentIndex = -1;
        HighlightConnection?.Invoke(null);
    }

    /// <summary>
    /// Populates <see cref="Issues"/> with the validator's findings and refreshes the
    /// status / navigation surface. Must run on the UI thread.
    /// </summary>
    private void CommitIssues(IReadOnlyList<DesignIssue> issues)
    {
        foreach (var issue in issues)
            Issues.Add(issue);

        HasIssues = Issues.Count > 0;
        StatusText = Issues.Count == 0
            ? "No issues found"
            : $"{Issues.Count} issue(s) found";

        OnPropertyChanged(nameof(NavigationText));

        if (HasIssues)
        {
            NavigateToIssue(0);
        }
    }

    /// <summary>
    /// Runs every validation pass against <paramref name="request"/> and returns the
    /// aggregated findings. Pure computation — no <see cref="ObservableCollection{T}"/>
    /// or property-changed interaction — so it is safe to invoke from a worker thread.
    /// </summary>
    private List<DesignIssue> ComputeIssues(DesignValidationRequest request)
    {
        var results = new List<DesignIssue>();

        // Single full-aggregation call: per-connection checks + frozen-path overlap +
        // dangling pins + spacing + min width each contribute their findings exactly once (#915).
        results.AddRange(_validator.Validate(
            request.Connections,
            request.Groups ?? Array.Empty<ComponentGroup>(),
            request.AllComponents ?? Array.Empty<Component>(),
            request.ExternalPortPins,
            request.MinWaveguideSpacingMicrometers,
            request.MinWaveguideWidthRules,
            request.ConnectionDrcRuleProvider));

        if (request.AllComponents is not null
            && request.ChipWidthMicrometers > 0
            && request.ChipHeightMicrometers > 0)
        {
            results.AddRange(_validator.ValidateComponentBounds(
                request.AllComponents, request.ChipWidthMicrometers, request.ChipHeightMicrometers));
        }

        if (request.AllComponents is not null
            && request.PdkSourceByComponent is not null
            && request.EnabledPdkNames is not null)
        {
            results.AddRange(_validator.ValidateComponentPdkCompatibility(
                request.AllComponents, request.PdkSourceByComponent,
                request.ProcessAgnosticPdkNames ?? Array.Empty<string>(),
                request.EnabledPdkNames, request.ProcessLockActive));
        }

        return results;
    }

    /// <summary>
    /// Immutable bundle of every input a validation run needs — lets
    /// <see cref="ComputeIssues"/> cross a thread boundary without fourteen positional
    /// parameters on the call site.
    /// </summary>
    private sealed record DesignValidationRequest(
        IEnumerable<WaveguideConnection> Connections,
        IEnumerable<ComponentGroup>? Groups,
        IEnumerable<Component>? AllComponents,
        double ChipWidthMicrometers,
        double ChipHeightMicrometers,
        IReadOnlyDictionary<Component, string?>? PdkSourceByComponent,
        IReadOnlyCollection<string>? ProcessAgnosticPdkNames,
        IReadOnlyCollection<string>? EnabledPdkNames,
        bool ProcessLockActive,
        IEnumerable<PhysicalPin>? ExternalPortPins,
        double MinWaveguideSpacingMicrometers,
        IReadOnlyList<WaveguideMinWidthRule>? MinWaveguideWidthRules,
        Func<WaveguideConnection, ConnectionDrcRules?>? ConnectionDrcRuleProvider);

    /// <summary>
    /// Navigates to the next issue in the list (wraps around).
    /// </summary>
    [RelayCommand]
    private void NextIssue()
    {
        if (Issues.Count == 0) return;

        int next = CurrentIndex + 1;
        if (next >= Issues.Count) next = 0;

        NavigateToIssue(next);
    }

    /// <summary>
    /// Navigates to the previous issue in the list (wraps around).
    /// </summary>
    [RelayCommand]
    private void PreviousIssue()
    {
        if (Issues.Count == 0) return;

        int prev = CurrentIndex - 1;
        if (prev < 0) prev = Issues.Count - 1;

        NavigateToIssue(prev);
    }

    /// <summary>
    /// Navigates to a specific issue by index.
    /// </summary>
    private void NavigateToIssue(int index)
    {
        if (index < 0 || index >= Issues.Count) return;

        CurrentIndex = index;
        OnPropertyChanged(nameof(NavigationText));

        var issue = Issues[index];
        StatusText = issue.Description;

        HighlightConnection?.Invoke(issue.Connection);
        NavigateToPosition?.Invoke(issue.X, issue.Y);
    }
}

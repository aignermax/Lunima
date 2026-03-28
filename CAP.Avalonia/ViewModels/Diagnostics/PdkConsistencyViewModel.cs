using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Library;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// ViewModel for the PDK Consistency panel.
/// Validates JSON PDK component definitions for coordinate correctness and
/// compares them against built-in ComponentTemplates to detect mismatches.
/// Issue #334: Investigate PDK JSON vs Nazca Python coordinate mismatch.
/// </summary>
public partial class PdkConsistencyViewModel : ObservableObject
{
    private readonly PdkConsistencyChecker _checker;
    private readonly PdkLoader _loader;

    /// <summary>Status text shown below the Run button.</summary>
    [ObservableProperty]
    private string _statusText = "Press 'Check PDKs' to validate loaded JSON PDK files.";

    /// <summary>True when a check has been run and findings are available.</summary>
    [ObservableProperty]
    private bool _hasFindings;

    /// <summary>Summary line shown in the header (e.g., "3 warnings, 1 error").</summary>
    [ObservableProperty]
    private string _summaryText = "";

    /// <summary>Collection of all consistency findings to display.</summary>
    public ObservableCollection<PdkFindingDisplayItem> Findings { get; } = new();

    /// <summary>Initializes a new <see cref="PdkConsistencyViewModel"/>.</summary>
    public PdkConsistencyViewModel()
    {
        _checker = new PdkConsistencyChecker();
        _loader = new PdkLoader();
    }

    /// <summary>
    /// Runs consistency checks on all bundled PDK JSON files and built-in templates.
    /// </summary>
    [RelayCommand]
    private void CheckPdks()
    {
        Findings.Clear();
        HasFindings = false;
        StatusText = "Running PDK consistency checks…";

        try
        {
            var allFindings = RunChecksOnBundledPdks();
            PopulateFindings(allFindings);
            UpdateSummary(allFindings);
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
    }

    private List<PdkConsistencyFinding> RunChecksOnBundledPdks()
    {
        var allFindings = new List<PdkConsistencyFinding>();
        var builtInTemplates = ComponentTemplates.GetAllTemplates();

        var pdkDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PDKs");
        if (!Directory.Exists(pdkDir))
        {
            StatusText = $"PDK directory not found: {pdkDir}";
            return allFindings;
        }

        foreach (var file in Directory.GetFiles(pdkDir, "*.json"))
        {
            try
            {
                var pdk = _loader.LoadFromFile(file);
                var internalFindings = _checker.Check(pdk);
                var templateFindings = _checker.CompareWithTemplates(pdk, builtInTemplates);
                allFindings.AddRange(internalFindings);
                allFindings.AddRange(templateFindings);
            }
            catch (Exception ex)
            {
                allFindings.Add(new PdkConsistencyFinding
                {
                    ComponentName = Path.GetFileName(file),
                    FindingType = "LoadError",
                    Message = $"Failed to load: {ex.Message}",
                    Severity = PdkFindingSeverity.Error
                });
            }
        }

        return allFindings;
    }

    private void PopulateFindings(List<PdkConsistencyFinding> findings)
    {
        foreach (var f in findings.OrderByDescending(f => f.Severity))
        {
            Findings.Add(new PdkFindingDisplayItem
            {
                ComponentName = f.ComponentName,
                FindingType = f.FindingType,
                Message = f.Message,
                SeverityLabel = f.Severity.ToString(),
                SeverityColor = f.Severity switch
                {
                    PdkFindingSeverity.Error => "Tomato",
                    PdkFindingSeverity.Warning => "Gold",
                    _ => "LightGray"
                },
                DeviationText = f.DeviationMicrometers.HasValue
                    ? $"Δ{f.DeviationMicrometers.Value.ToString("F3", CultureInfo.InvariantCulture)} µm"
                    : ""
            });
        }

        HasFindings = Findings.Count > 0;
    }

    private void UpdateSummary(List<PdkConsistencyFinding> findings)
    {
        var errors = findings.Count(f => f.Severity == PdkFindingSeverity.Error);
        var warnings = findings.Count(f => f.Severity == PdkFindingSeverity.Warning);
        var infos = findings.Count(f => f.Severity == PdkFindingSeverity.Info);

        if (findings.Count == 0)
        {
            SummaryText = "All PDK definitions are consistent.";
            StatusText = "No issues found.";
        }
        else
        {
            SummaryText = $"{errors} error(s), {warnings} warning(s), {infos} info(s)";
            StatusText = errors > 0
                ? "Issues found — see findings below."
                : "Warnings found — review recommended.";
        }
    }
}

/// <summary>
/// Display model for a single PDK consistency finding in the UI.
/// </summary>
public class PdkFindingDisplayItem
{
    /// <summary>Name of the component the finding belongs to.</summary>
    public string ComponentName { get; set; } = "";

    /// <summary>Short type label (e.g., "PinOutOfBounds").</summary>
    public string FindingType { get; set; } = "";

    /// <summary>Full description of the issue.</summary>
    public string Message { get; set; } = "";

    /// <summary>Severity label (Info / Warning / Error).</summary>
    public string SeverityLabel { get; set; } = "";

    /// <summary>Avalonia color name for the severity badge.</summary>
    public string SeverityColor { get; set; } = "Gray";

    /// <summary>Formatted deviation value, or empty string if not applicable.</summary>
    public string DeviationText { get; set; } = "";
}

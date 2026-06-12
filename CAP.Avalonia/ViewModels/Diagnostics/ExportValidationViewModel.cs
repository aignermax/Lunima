using System.Collections.ObjectModel;
using CAP_Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.CodeExporter;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_DataAccess.Persistence.PIR;

namespace CAP.Avalonia.ViewModels.Diagnostics;

/// <summary>
/// ViewModel for end-to-end Nazca export validation.
/// Validates that exported code matches UI design exactly.
/// </summary>
public partial class ExportValidationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _validationStatus = "Ready";

    [ObservableProperty]
    private bool _isValid = false;

    [ObservableProperty]
    private int _totalChecks = 0;

    [ObservableProperty]
    private int _passedChecks = 0;

    [ObservableProperty]
    private int _failedChecks = 0;

    [ObservableProperty]
    private int _warningCount = 0;

    [ObservableProperty]
    private bool _hasResults = false;

    public ObservableCollection<ValidationMessage> Messages { get; } = new();

    /// <summary>
    /// Supplies the live per-instance Nazca overrides (keyed by component identifier)
    /// so validation exports and checks the SAME script the production exporter emits.
    /// Wired by <c>MainViewModel</c> to <c>FileOperations.StoredNazcaOverrides</c>; null
    /// means "no overrides" (validation then behaves as a plain PDK export).
    /// </summary>
    public Func<IReadOnlyDictionary<string, NazcaCodeOverride>>? OverridesProvider { get; set; }

    private readonly SimpleNazcaExporter _exporter;
    private readonly ExportValidator _validator;
    private readonly ErrorConsoleService? _errorConsole;

    /// <summary>Initializes a new instance of <see cref="ExportValidationViewModel"/>.</summary>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public ExportValidationViewModel(ErrorConsoleService? errorConsole = null)
    {
        _exporter = new SimpleNazcaExporter();
        _validator = new ExportValidator();
        _errorConsole = errorConsole;
    }

    /// <summary>
    /// Runs end-to-end validation on the current design.
    /// </summary>
    [RelayCommand]
    public void RunValidation(DesignCanvasViewModel? canvas)
    {
        if (canvas == null)
        {
            ValidationStatus = "No design to validate";
            return;
        }

        Messages.Clear();
        ValidationStatus = "Running validation...";

        try
        {
            // Use the SAME overrides the production export uses, so the validator never
            // checks a different script than the one shipped to fab.
            var overrides = OverridesProvider?.Invoke();

            // Export to Nazca code
            var nazcaCode = _exporter.Export(canvas, overrides: overrides);

            // Collect components and connections
            var components = canvas.Components.Select(vm => vm.Component).ToList();
            var connections = canvas.Connections.Select(vm => vm.Connection).ToList();

            // Run validation, passing the persisted bbox anchors of raw-code overrides
            // so bbox-anchored placements are not flagged as position mismatches.
            var result = _validator.Validate(
                components, connections, nazcaCode, BuildOverrideAnchors(overrides));

            // Update UI with results
            DisplayResults(result);
        }
        catch (Exception ex)
        {
            _errorConsole?.LogError($"Export validation failed: {ex.Message}", ex);
            ValidationStatus = $"Validation failed: {ex.Message}";
            HasResults = false;
        }
    }

    /// <summary>
    /// Extracts the persisted bbox anchors (XMin, YMax) from raw-code overrides into the
    /// plain tuple map the Core validator accepts (Core must not reference CAP-DataAccess).
    /// Only entries with a RawCode AND both anchor fields are placed bbox-anchored — those
    /// are the ones whose expected position depends on the anchor.
    /// </summary>
    private static IReadOnlyDictionary<string, (double XMin, double YMax)>? BuildOverrideAnchors(
        IReadOnlyDictionary<string, NazcaCodeOverride>? overrides)
    {
        if (overrides == null)
            return null;

        var anchors = new Dictionary<string, (double XMin, double YMax)>(StringComparer.Ordinal);
        foreach (var kv in overrides)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value?.RawCode)
                && kv.Value!.OverrideBboxXMinMicrometers is { } xMin
                && kv.Value.OverrideBboxYMaxMicrometers is { } yMax)
            {
                anchors[kv.Key] = (xMin, yMax);
            }
        }
        return anchors;
    }

    /// <summary>
    /// Displays validation results in the UI.
    /// </summary>
    private void DisplayResults(ValidationResult result)
    {
        IsValid = result.IsValid;
        TotalChecks = result.TotalChecks;
        PassedChecks = result.PassedChecks;
        FailedChecks = result.FailedChecks;
        WarningCount = result.WarningCount;

        if (result.IsValid)
        {
            ValidationStatus = $"✓ Validation passed ({PassedChecks}/{TotalChecks} checks)";
        }
        else
        {
            ValidationStatus = $"✗ Validation failed ({FailedChecks} errors, {WarningCount} warnings)";
        }

        // Add errors
        foreach (var error in result.Errors)
        {
            Messages.Add(new ValidationMessage
            {
                Severity = "Error",
                Message = error
            });
        }

        // Add warnings
        foreach (var warning in result.Warnings)
        {
            Messages.Add(new ValidationMessage
            {
                Severity = "Warning",
                Message = warning
            });
        }

        // Add successes (only show first 10 to avoid clutter)
        var successesToShow = result.Successes.Take(10).ToList();
        foreach (var success in successesToShow)
        {
            Messages.Add(new ValidationMessage
            {
                Severity = "Success",
                Message = success
            });
        }

        if (result.Successes.Count > 10)
        {
            Messages.Add(new ValidationMessage
            {
                Severity = "Info",
                Message = $"... and {result.Successes.Count - 10} more successful checks"
            });
        }

        HasResults = true;
    }

    /// <summary>
    /// Clears validation results.
    /// </summary>
    [RelayCommand]
    public void ClearResults()
    {
        Messages.Clear();
        ValidationStatus = "Ready";
        IsValid = false;
        TotalChecks = 0;
        PassedChecks = 0;
        FailedChecks = 0;
        WarningCount = 0;
        HasResults = false;
    }
}

/// <summary>
/// Represents a single validation message.
/// </summary>
public class ValidationMessage
{
    public string Severity { get; init; } = "Info"; // "Error", "Warning", "Success", "Info"
    public string Message { get; init; } = "";
}

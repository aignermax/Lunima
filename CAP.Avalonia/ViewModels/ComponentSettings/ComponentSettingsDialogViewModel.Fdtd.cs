using CAP_Core.Components.Core;
using CAP_Core.Solvers.Fdtd;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Solvers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.ComponentSettings;

/// <summary>
/// FDTD "Recalculate S-matrix" half of the dialog: instead of loading an
/// S-matrix from a file, compute it from the component's geometry with the
/// open-source Meep solver and feed the result through the same store-and-apply
/// path the file import uses.
/// </summary>
public partial class ComponentSettingsDialogViewModel
{
    private readonly IFdtdSMatrixService? _fdtdService;
    private readonly Func<Component, CancellationToken, Task<FdtdSMatrixRequest?>>? _fdtdRequestFactory;
    private CancellationTokenSource? _recalcCts;

    /// <summary>True while an FDTD recompute is running.</summary>
    [ObservableProperty]
    private bool _isComputing;

    /// <summary>
    /// Live simulation/solver status shown in the dialog (provisioning, running,
    /// energy-conservation summary, or the error/setup hint on failure).
    /// </summary>
    [ObservableProperty]
    private string _solverStatus = string.Empty;

    /// <summary>
    /// True when FDTD recompute is wired up for this dialog instance (solver
    /// service + geometry factory present and a live component is configured).
    /// </summary>
    public bool CanRecalculate =>
        _fdtdService != null && _fdtdRequestFactory != null && _liveComponent != null;

    private bool CanRunRecalculate => CanRecalculate && !IsComputing && !IsImporting;

    partial void OnIsComputingChanged(bool value) => RecalculateSMatrixCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Recomputes this component's S-matrix from its geometry via FDTD and applies
    /// it like an import. Surfaces the raw solver error on failure — no silent fallback.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunRecalculate))]
    private async Task RecalculateSMatrix()
    {
        if (_fdtdService == null || _fdtdRequestFactory == null || _liveComponent == null || _storedSMatrices == null)
            return;

        IsComputing = true;
        SolverStatus = "Preparing component geometry…";
        _recalcCts = new CancellationTokenSource();

        try
        {
            var request = await _fdtdRequestFactory(_liveComponent, _recalcCts.Token);
            if (request == null)
            {
                SolverStatus = "Could not export this component's geometry for FDTD.";
                return;
            }

            SolverStatus = "Running FDTD (Meep in Docker). The first run builds the solver image and may take several minutes…";
            var result = await _fdtdService.SolveAsync(request, _recalcCts.Token);

            if (!result.Success)
            {
                SolverStatus = result.MissingDependency != null
                    ? $"FDTD unavailable — '{result.MissingDependency}' is required. {result.Error}"
                    : $"FDTD failed: {result.Error}";
                _errorConsole?.LogError($"FDTD recompute failed for '{_displayName}': {result.Error}\n{result.RawStderr}");
                return;
            }

            var note = $"FDTD Meep {(result.Is3D ? "3D" : "2D")}";
            var data = FdtdSMatrixConverter.ToComponentSMatrixData(result, note);
            _storedSMatrices[_entityKey] = data;

            var applyResult = SMatrixOverrideApplicator.Apply(_liveComponent, data, _errorConsole);
            SolverStatus = BuildSolverStatus(result, applyResult);
            StatusText = $"Recomputed S-matrix via FDTD ({note}).";
        }
        catch (OperationCanceledException)
        {
            SolverStatus = "FDTD recompute cancelled.";
        }
        catch (Exception ex)
        {
            SolverStatus = $"FDTD error: {ex.Message}";
            _errorConsole?.LogError($"FDTD recompute crashed for '{_displayName}'", ex);
        }
        finally
        {
            IsComputing = false;
            _recalcCts?.Dispose();
            _recalcCts = null;
            RefreshEntries(notifyChanged: true);
        }
    }

    private static string BuildSolverStatus(FdtdSMatrixResult result, ApplyResult? apply)
    {
        var worst = result.EnergySumPerInput.Count > 0 ? result.EnergySumPerInput.Values.Max() : 0.0;
        var energy = result.EnergySumPerInput.Count > 0 ? $" Energy Σ|S|² ≤ {worst:F3} per input." : "";
        var applied = apply == null ? "" : $" Applied {apply.Applied} wavelength(s).";
        return $"FDTD done: {result.Wavelengths.Count} wavelength(s).{energy}{applied}";
    }
}

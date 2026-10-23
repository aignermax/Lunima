using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Solvers.ModeSolver;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Solvers;

/// <summary>
/// ViewModel for the "Compute Modes for Waveguide" dialog.
/// Drives cross-section input fields, backend selection, and result display.
/// </summary>
public partial class ModeSolverViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Retires solve admission and awaits subprocess/install cleanup.</summary>
    public Task StopAsync()
    {
        var stopping = _work.StopAsync();
        OnPropertyChanged(nameof(CanSolve));
        SolveCommand.NotifyCanExecuteChanged();
        return stopping;
    }
    public ValueTask DisposeAsync() => new(StopAsync());
    private readonly ShellWorkScope _work = new();
    // ── cross-section inputs ────────────────────────────────────────────────

    /// <summary>Waveguide core width in µm.</summary>
    [ObservableProperty]
    private double _width = 0.45;

    /// <summary>Waveguide core height (thickness) in µm.</summary>
    [ObservableProperty]
    private double _height = 0.22;

    /// <summary>Slab height in µm (0 for fully-etched strip).</summary>
    [ObservableProperty]
    private double _slabHeight = 0.0;

    /// <summary>Core refractive index.</summary>
    [ObservableProperty]
    private double _coreIndex = 3.48;

    /// <summary>Cladding refractive index.</summary>
    [ObservableProperty]
    private double _cladIndex = 1.44;

    /// <summary>Wavelength in nm (converted to µm on submission).</summary>
    [ObservableProperty]
    private double _wavelengthNm = 1550;

    /// <summary>Maximum number of modes to compute.</summary>
    [ObservableProperty]
    private int _numModes = 4;

    /// <summary>Selected backend name string (bound to ComboBox).</summary>
    [ObservableProperty]
    private string _selectedBackend = "GdsfactoryModes";

    // ── status / result ─────────────────────────────────────────────────────

    /// <summary>True while a solve is in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSolve))]
    private bool _isSolving;

    /// <summary>Status text shown below the result table.</summary>
    [ObservableProperty]
    private string _statusText = "";

    /// <summary>True on the last successful solve (shows result grid).</summary>
    [ObservableProperty]
    private bool _hasResult;

    /// <summary>Raw stderr captured from the last failed solve.</summary>
    [ObservableProperty]
    private string _rawStderr = "";

    /// <summary>True when stderr is non-empty (shows the raw-error expander).</summary>
    [ObservableProperty]
    private bool _hasStderr;

    /// <summary>Mode entries displayed in the results table.</summary>
    public IReadOnlyList<ModeSolverModeEntry> Modes { get; private set; } = Array.Empty<ModeSolverModeEntry>();

    /// <summary>True when a solve can be initiated.</summary>
    public bool CanSolve => !IsSolving && !_work.IsStopping;

    // ── available backend names for ComboBox ─────────────────────────────────
    /// <summary>Backend names exposed to the ComboBox.</summary>
    public static IReadOnlyList<string> AvailableBackends { get; } =
        Enum.GetNames<ModeSolverBackend>();

    // ── dependencies ────────────────────────────────────────────────────────

    private readonly IModeSolverService _service;
    private readonly CAP_Core.Solvers.ModeProbe.CrossSectionDefaultsStore? _defaultsStore;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Optional hook to auto-install a missing mode-solver backend into a managed
    /// Python environment and activate it; wired by DI. Args: pip package spec,
    /// progress, cancellation token. Returns true when the backend became available,
    /// so the solve can be retried. When unset, a missing backend only shows a hint.
    /// </summary>
    public Func<string, IProgress<string>, CancellationToken, Task<bool>>? EnsureBackendAsync { get; set; }

    /// <summary>
    /// Initialises the ViewModel with its solver service. The optional
    /// <paramref name="defaultsStore"/> records each manually entered cross-section
    /// so the canvas mode probe (issue #691) can fall back to it.
    /// </summary>
    public ModeSolverViewModel(
        IModeSolverService service,
        CAP_Core.Solvers.ModeProbe.CrossSectionDefaultsStore? defaultsStore = null,
        ApplicationWorkCoordinator? applicationWork = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _defaultsStore = defaultsStore;
        _work.RegisterWith(applicationWork, StopAsync);
    }

    // ── commands ─────────────────────────────────────────────────────────────

    /// <summary>Runs the mode solver with the current cross-section inputs.</summary>
    [RelayCommand(CanExecute = nameof(CanSolve))]
    private Task Solve() => _work.RunAsync(SolveCoreAsync);

    private async Task SolveCoreAsync(CancellationToken stopping)
    {
        _cts?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _cts = cancellation;
        var token = cancellation.Token;

        IsSolving   = true;
        HasResult   = false;
        HasStderr   = false;
        RawStderr   = "";
        StatusText  = LocalizationService.Instance.Translate("Solver.Solving");

        try
        {
            var request = BuildRequest();
            // Remember the manual entry so the canvas mode probe can fall back to it
            // when the active PDK carries no geometry/material data (issue #691).
            _defaultsStore?.RecordManualEntry(Width, Height, SlabHeight, CoreIndex, CladIndex);
            var result  = await _service.SolveAsync(request, token);
            _work.ThrowIfStopping();
            token.ThrowIfCancellationRequested();

            if (!result.Success && EnsureBackendAsync != null
                && !string.IsNullOrWhiteSpace(result.MissingBackend))
            {
                var retried = await InstallBackendAndRetry(result, request, token);
                _work.ThrowIfStopping();
                token.ThrowIfCancellationRequested();
                if (retried == null) return; // install handled the status itself; don't overwrite
                result = retried;
            }

            if (result.Success)
            {
                Modes      = result.Modes;
                OnPropertyChanged(nameof(Modes));
                HasResult  = true;
                StatusText = string.Format(
                    LocalizationService.Instance.Translate("Solver.Done"),
                    result.Modes.Count, result.BackendUsed);
            }
            else
            {
                HasResult  = false;
                StatusText = result.Error ?? LocalizationService.Instance.Translate("Solver.SolveFailed");

                if (!string.IsNullOrWhiteSpace(result.RawStderr))
                {
                    RawStderr = result.RawStderr;
                    HasStderr = true;
                }

                if (!string.IsNullOrWhiteSpace(result.MissingBackend))
                    StatusText += $"  →  pip install {result.MissingBackend}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.Instance.Translate("Solver.Cancelled");
        }
        finally
        {
            if (ReferenceEquals(_cts, cancellation)) _cts = null;
            IsSolving = false;
        }
    }

    /// <summary>
    /// Auto-installs the missing backend into a managed environment and, on success,
    /// re-runs the solve once against the freshly-activated interpreter. Returns the
    /// retry result to display, or <c>null</c> when it has already set an explanatory
    /// <see cref="StatusText"/> (install failed/declined, or the backend is still
    /// unusable after install) and the caller should not overwrite it.
    /// </summary>
    private async Task<ModeSolverResult?> InstallBackendAndRetry(
        ModeSolverResult failure, ModeSolverRequest request, CancellationToken ct)
    {
        var progress = new Progress<string>(m => { if (!ct.IsCancellationRequested && !_work.IsStopping) StatusText = m; });
        StatusText = string.Format(
            LocalizationService.Instance.Translate("Solver.Installing"), failure.MissingBackend);
        var installed = await EnsureBackendAsync!(failure.MissingBackend!, progress, ct);
        _work.ThrowIfStopping();
        ct.ThrowIfCancellationRequested();
        if (!installed)
        {
            StatusText = string.Format(
                LocalizationService.Instance.Translate("Solver.AutoInstallIncomplete"), failure.MissingBackend);
            return null;
        }

        StatusText = LocalizationService.Instance.Translate("Solver.BackendInstalledRetrying");
        var retry = await _service.SolveAsync(request, ct);
        _work.ThrowIfStopping();
        ct.ThrowIfCancellationRequested();
        if (!retry.Success && retry.MissingBackend == failure.MissingBackend)
        {
            StatusText = string.Format(
                LocalizationService.Instance.Translate("Solver.BackendStillUnavailable"), failure.MissingBackend);
            return null;
        }
        return retry;
    }

    /// <summary>Cancels a running solve.</summary>
    [RelayCommand]
    private void CancelSolve()
    {
        _cts?.Cancel();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private ModeSolverRequest BuildRequest()
    {
        if (!Enum.TryParse<ModeSolverBackend>(SelectedBackend, out var backend))
            backend = ModeSolverBackend.GdsfactoryModes;

        return new ModeSolverRequest
        {
            Width       = Width,
            Height      = Height,
            SlabHeight  = SlabHeight,
            CoreIndex   = CoreIndex,
            CladIndex   = CladIndex,
            Wavelengths = new[] { WavelengthNm / 1000.0 },  // nm → µm
            Backend     = backend,
            NumModes    = NumModes,
        };
    }
}

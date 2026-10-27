using CAP_Core.Export.Contracts;
using CAP_Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Export;
using CAP.Avalonia.Services.Localization;
using System.Collections.ObjectModel;
using CAP.Avalonia.Services.Lifetime;

namespace CAP.Avalonia.ViewModels.Export;

/// <summary>
/// ViewModel for GDS export functionality.
/// Allows users to export designs to GDS format with automatic Python/Nazca execution.
/// </summary>
public partial class GdsExportViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ShellWorkScope _exports = new();
    /// <summary>Retires accepted export workflows.</summary>
    public Task StopAsync() => _exports.StopAsync();
    public ValueTask DisposeAsync() => new(StopAsync());
    private Task RunEnvironmentAsync(Func<GdsEnvironmentOperation, Task> work, CancellationToken caller = default) =>
        _exports.RunAsync(async stop =>
        {
            await using var operation = new GdsEnvironmentOperation(_exports, stop, caller);
            try { operation.Check(); await work(operation); }
            catch (OperationCanceledException) when (_exports.IsStopping) { }
        });
    private readonly IGdsExportSession _exportService;
    private readonly IPythonDiscovery _discoveryService;

    [ObservableProperty]
    private bool _pythonAvailable;

    [ObservableProperty]
    private string _pythonStatus = LocalizationService.Instance.Translate("Export.Checking");

    [ObservableProperty]
    private bool _nazcaAvailable;

    [ObservableProperty]
    private string _nazcaStatus = LocalizationService.Instance.Translate("Export.Checking");

    [ObservableProperty]
    private bool _generateGdsEnabled = true;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private string _lastExportStatus = string.Empty;

    [ObservableProperty]
    private string _customPythonPath = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private ObservableCollection<PythonInstallation> _availablePythons = new();

    [ObservableProperty]
    private string _pythonPathSource = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ManagedEnvCandidate> _managedCandidates = new();

    [ObservableProperty]
    private bool _showNazcaInstallOffer;

    [ObservableProperty]
    private ObservableCollection<InterpreterOption> _interpreterOptions = new();

    [ObservableProperty]
    private bool _showCreateEnvironmentButton;

    /// <summary>
    /// True if both Python and Nazca are available and ready.
    /// </summary>
    public bool IsEnvironmentReady => PythonAvailable && NazcaAvailable;

    /// <summary>
    /// Callback to save Python path to preferences when changed.
    /// </summary>
    public Action<string?>? OnPythonPathChanged { get; set; }

    /// <summary>
    /// Supplies the managed environments that carry Nazca (wired by the DI layer;
    /// the GDS-export slice never references the environment-manager slice directly).
    /// </summary>
    public Func<IReadOnlyList<ManagedEnvCandidate>>? ManagedEnvironmentsProvider { get; set; }

    /// <summary>Activates a managed environment by name (wired by the DI layer).</summary>
    public Action<string>? ActivateManagedEnvironment { get; set; }

    /// <summary>Starts the default Nazca environment installation (wired by the DI layer).</summary>
    public Action? RequestNazcaInstall { get; set; }

    private readonly ErrorConsoleService? _errorConsole;

    /// <summary>Initializes a new instance of <see cref="GdsExportViewModel"/>.</summary>
    /// <param name="exportService">GDS export service.</param>
    /// <param name="discovery">Available Python interpreter discovery.</param>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public GdsExportViewModel(IGdsExportSession exportService, IPythonDiscovery discovery, ErrorConsoleService? errorConsole = null,
        ApplicationWorkCoordinator? applicationWork = null)
    {
        _exportService = exportService;
        _discoveryService = discovery;
        _errorConsole = errorConsole;
        _exports.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>
    /// Initializes the ViewModel with saved preferences. A null path clears the
    /// configuration (e.g. the active managed environment was deleted) so a stale
    /// interpreter path never lingers; the next refresh picks a fallback.
    /// </summary>
    /// <param name="savedPythonPath">Previously saved Python path, or null to clear.</param>
    public void Initialize(string? savedPythonPath)
    {
        if (!string.IsNullOrEmpty(savedPythonPath))
        {
            CustomPythonPath = savedPythonPath;
            _exportService.SetCustomPythonPath(savedPythonPath);
            PythonPathSource = "Custom";
            return;
        }

        CustomPythonPath = string.Empty;
        _exportService.SetCustomPythonPath(null);
        PythonPathSource = string.Empty;
    }

    /// <summary>
    /// Checks the Python/Nazca environment and updates status. Exceptions
    /// are surfaced on the status strings and logged to the ErrorConsole —
    /// this method is also invoked fire-and-forget from MainViewModel wiring,
    /// where an unobserved task exception would crash silently.
    /// </summary>
    [RelayCommand]
    public Task CheckEnvironmentAsync() => CheckEnvironmentAsync(CancellationToken.None);

    public Task CheckEnvironmentAsync(CancellationToken cancellationToken) =>
        RunEnvironmentAsync(CheckEnvironmentCoreAsync, cancellationToken);

    private async Task CheckEnvironmentCoreAsync(GdsEnvironmentOperation operation)
    {
        try
        {
            operation.Publish(() => IsChecking = true);
            operation.Publish(() => PythonStatus = LocalizationService.Instance.Translate("Export.Checking"));
            operation.Publish(() => NazcaStatus = LocalizationService.Instance.Translate("Export.Checking"));
            var envInfo = await _exportService.CheckPythonEnvironmentAsync(operation.Token);
            operation.Check();

            operation.Publish(() => PythonAvailable = envInfo.PythonAvailable);
            operation.Publish(() => NazcaAvailable = envInfo.NazcaAvailable);

            if (envInfo.PythonAvailable)
            {
                operation.Publish(() => PythonStatus = $"✓ Found (v{envInfo.PythonVersion})");
            }
            else
            {
                operation.Publish(() => PythonStatus = "✗ Not found");
            }

            if (envInfo.NazcaAvailable)
            {
                operation.Publish(() => NazcaStatus = $"✓ Found (v{envInfo.NazcaVersion})");
            }
            else if (envInfo.PythonAvailable)
            {
                operation.Publish(() => NazcaStatus = "✗ Not installed");
            }
            else
            {
                operation.Publish(() => NazcaStatus = "N/A (Python not found)");
            }

            operation.Publish(() => OnPropertyChanged(nameof(IsEnvironmentReady)));
            RefreshManagedCandidatesCore(operation.Check);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            operation.Publish(() => PythonAvailable = false);
            operation.Publish(() => NazcaAvailable = false);
            operation.Publish(() => PythonStatus = $"✗ Check failed: {ex.Message}");
            operation.Publish(() => NazcaStatus = "✗ Check failed");
            operation.Publish(() => _errorConsole?.LogError($"Python environment check failed: {ex.Message}", ex));
            operation.Publish(() => OnPropertyChanged(nameof(IsEnvironmentReady)));
            RefreshManagedCandidatesCore(operation.Check);
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Rebuilds <see cref="ManagedCandidates"/> from <see cref="ManagedEnvironmentsProvider"/>
    /// and recomputes whether the "install Nazca" offer should be shown (no Nazca in the
    /// active interpreter AND no managed environment to switch to).
    /// </summary>
    public void RefreshManagedCandidates()
    {
        if (_exports.IsStopping) return;
        try { RefreshManagedCandidatesCore(_exports.ThrowIfStopping); }
        catch (OperationCanceledException) when (_exports.IsStopping) { }
    }

    private void RefreshManagedCandidatesCore(Action check)
    {
        check();
        var candidates = (ManagedEnvironmentsProvider?.Invoke() ?? Array.Empty<ManagedEnvCandidate>()).ToArray();
        check();
        ManagedCandidates.Clear();
        check();
        foreach (var candidate in candidates)
        {
            ManagedCandidates.Add(candidate);
            check();
        }

        ShowNazcaInstallOffer = !NazcaAvailable && ManagedCandidates.Count == 0;
        check();
        // The one-click create stays available whenever no managed environment exists,
        // even if a system Python happens to carry Nazca.
        ShowCreateEnvironmentButton = ManagedCandidates.Count == 0;
        check();
        RebuildInterpreterOptions(check);
    }

    /// <summary>
    /// Rebuilds the unified interpreter list (managed environments first, then discovered
    /// system Pythons), marking the entry that matches the configured interpreter path.
    /// </summary>
    private void RebuildInterpreterOptions(Action check)
    {
        var managed = ManagedCandidates.ToArray();
        var discovered = AvailablePythons.ToArray();
        InterpreterOptions.Clear();
        check();
        foreach (var candidate in managed)
        {
            InterpreterOptions.Add(new InterpreterOption(
                candidate.DisplayText,
                candidate.PythonExecutable,
                IsConfiguredPath(candidate.PythonExecutable),
                candidate.Name));
            check();
        }

        foreach (var python in discovered)
        {
            InterpreterOptions.Add(new InterpreterOption(
                $"{python.Source} · Python {python.PythonVersion ?? "?"} · Nazca {python.NazcaVersion ?? "—"}",
                python.Path,
                IsConfiguredPath(python.Path),
                ManagedName: null));
            check();
        }
    }

    private bool IsConfiguredPath(string path) =>
        string.Equals(path, CustomPythonPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Selects an interpreter from the unified list: managed environments are activated
    /// through the registry (persists + updates the running pipeline), system Pythons via
    /// the classic path selection. Both re-check the environment afterwards.
    /// </summary>
    /// <param name="option">The interpreter the user clicked.</param>
    [RelayCommand]
    public Task SelectInterpreter(InterpreterOption option) =>
        RunEnvironmentAsync(operation => SelectInterpreterCoreAsync(option, operation));

    private async Task SelectInterpreterCoreAsync(InterpreterOption option, GdsEnvironmentOperation operation)
    {
        operation.Check();
        if (option.ManagedName != null)
        {
            operation.Publish(() => ActivateManagedEnvironment?.Invoke(option.ManagedName));
            await CheckEnvironmentCoreAsync(operation);
            return;
        }

        await SetPythonPathCoreAsync(option.Path, "Custom", operation);
    }

    /// <summary>
    /// One-stop refresh for the settings page: discovers system Pythons with Nazca
    /// (auto-selecting the first hit when nothing is configured yet — the "first start"
    /// default), then re-checks the configured interpreter and rebuilds the list.
    /// </summary>
    [RelayCommand]
    public Task RefreshInterpretersAsync() => RunEnvironmentAsync(async operation =>
    {
        await SearchForPythonCoreAsync(operation);
        operation.Check();
        await CheckEnvironmentCoreAsync(operation);
        operation.Check();
        await TrySelectFallbackInterpreterCoreAsync(operation);
    });

    /// <summary>
    /// When no interpreter is configured — or the configured one no longer exists on
    /// disk (e.g. its managed environment was deleted) — falls back to the first
    /// available candidate (managed environments come first) instead of showing
    /// "not found" while working alternatives sit in the list.
    /// </summary>
    public Task TrySelectFallbackInterpreterAsync() => RunEnvironmentAsync(TrySelectFallbackInterpreterCoreAsync);

    private async Task TrySelectFallbackInterpreterCoreAsync(GdsEnvironmentOperation operation)
    {
        operation.Check();
        var configuredIsUsable = !string.IsNullOrEmpty(CustomPythonPath) && File.Exists(CustomPythonPath);
        if (configuredIsUsable) return;

        var fallback = InterpreterOptions.FirstOrDefault();
        if (fallback != null)
            await SelectInterpreterCoreAsync(fallback, operation);
    }

    /// <summary>
    /// Activates a managed environment: the registry callback (DI layer) persists the
    /// interpreter and pushes it into this ViewModel, then the status is re-checked.
    /// </summary>
    /// <param name="candidate">The managed environment to activate.</param>
    [RelayCommand]
    public Task SelectManagedEnvironment(ManagedEnvCandidate candidate) => RunEnvironmentAsync(async operation =>
    {
        operation.Publish(() => ActivateManagedEnvironment?.Invoke(candidate.Name));
        await CheckEnvironmentCoreAsync(operation);
    });

    /// <summary>Requests creation + installation of the default Nazca environment.</summary>
    [RelayCommand]
    public void InstallNazca() { if (!_exports.IsStopping) RequestNazcaInstall?.Invoke(); }

    /// <summary>
    /// Decides how to proceed with GDS generation based on the current environment
    /// state. Callers refresh the state (<see cref="CheckEnvironmentAsync"/>) first;
    /// this method only maps state + user choice to a decision.
    /// </summary>
    /// <param name="messageBox">Dialog service; null (headless) proceeds like before.</param>
    public async Task<GdsPreflightDecision> PreflightGdsAsync(Services.IMessageBoxService? messageBox)
    {
        if (!GenerateGdsEnabled || IsEnvironmentReady || messageBox == null)
            return GdsPreflightDecision.Proceed;

        var choice = await messageBox.ShowChoicePromptAsync(
            "Nazca is required to generate a GDS file, but no Python environment with Nazca is available. "
            + "The Nazca Python script itself has been exported.",
            "Nazca required",
            new[] { "Install Nazca now", "Open Settings", "Skip GDS" });

        return choice switch
        {
            0 => GdsPreflightDecision.InstallRequested,
            1 => GdsPreflightDecision.OpenSettingsRequested,
            _ => GdsPreflightDecision.SkipGds,
        };
    }

    /// <summary>
    /// Exports a Python script to GDS (if enabled and environment is ready).
    /// </summary>
    /// <param name="scriptPath">Path to the exported Python script.</param>
    /// <param name="exportRequestId">Identity reserved by the outer command before its first await; omitted for direct exports.</param>
    /// <returns>Export result with status information.</returns>
    public Task<GdsExportResult> ExportScriptToGdsAsync(string scriptPath, long? exportRequestId = null,
        CancellationToken cancellationToken = default) => _exports.RunAsync(async stop =>
    {
        await using var operation = new AsyncCancellationScope(stop, cancellationToken);
        void CheckCurrent()
        {
            _exports.ThrowIfStopping();
            cancellationToken.ThrowIfCancellationRequested();
            operation.Token.ThrowIfCancellationRequested();
        }
        CheckCurrent();
        var requestId = exportRequestId ?? BeginExportRequest();
        CheckCurrent();
        var result = await _exportService.ExportToGdsAsync(scriptPath, GenerateGdsEnabled, operation.Token);
        CheckCurrent();
        LastExportStatus = result.Status;
        CheckCurrent();
        if (result.Success && !string.IsNullOrWhiteSpace(result.GdsPath))
        {
            LastSuccessfulGdsExportRequestId = requestId;
            OnPropertyChanged(nameof(LastSuccessfulGdsExportRequestId));
        }
        CheckCurrent();
        return result;
    });

    /// <summary>Reserves the identity at the start of an export command, before dialogs or other awaits.</summary>
    public long BeginExportRequest() => ++LatestStartedGdsExportRequestId;

    /// <summary>Monotonic request identity assigned before asynchronous export execution.</summary>
    public long LatestStartedGdsExportRequestId { get; private set; }

    /// <summary>Identity of the request that most recently produced a GDS artifact.</summary>
    public long LastSuccessfulGdsExportRequestId { get; private set; }

    private int _searchGeneration;

    /// <summary>
    /// Runs the actual interpreter discovery. Virtual so tests can substitute a
    /// controllable discovery to exercise overlapping-search behavior.
    /// </summary>
    protected virtual Task<List<PythonInstallation>> DiscoverPythonsAsync(CancellationToken cancellationToken = default) =>
        _discoveryService.DiscoverPythonWithNazcaAsync(cancellationToken);

    /// <summary>
    /// Searches for Python installations with Nazca and updates the available list.
    /// Starting a new search supersedes any still-running one: the stale run discards
    /// its results, so rapid page switches can never duplicate the list.
    /// </summary>
    [RelayCommand]
    public Task SearchForPythonAsync() => SearchForPythonAsync(CancellationToken.None);

    public Task SearchForPythonAsync(CancellationToken cancellationToken) =>
        RunEnvironmentAsync(SearchForPythonCoreAsync, cancellationToken);

    private async Task SearchForPythonCoreAsync(GdsEnvironmentOperation operation)
    {
        operation.Check();
        var generation = ++_searchGeneration;

        try
        {
            operation.Publish(() => IsSearching = true);
            var found = await DiscoverPythonsAsync(operation.Token);
            operation.Check();
            if (generation != _searchGeneration)
                return;   // a newer search owns the list now

            AvailablePythons.Clear();
            operation.Check();
            foreach (var installation in found)
            {
                AvailablePythons.Add(installation);
                operation.Check();
                if (generation != _searchGeneration) return;
            }

            if (found.Count > 0)
            {
                // Auto-select first one if no path is set
                if (string.IsNullOrEmpty(CustomPythonPath))
                {
                    await SetPythonPathCoreAsync(found[0].Path, found[0].Source, operation);
                }
            }
            else
            {
                operation.Publish(() => PythonStatus = "✗ No Python with Nazca found");
                operation.Publish(() => NazcaStatus = "Install Nazca from nazca-design.org");
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            if (generation == _searchGeneration)
            {
                operation.Publish(() => _errorConsole?.LogError($"Python discovery failed: {ex.Message}", ex));
                operation.Publish(() => PythonStatus = $"✗ Search failed: {ex.Message}");
            }
        }
        finally
        {
            if (generation == _searchGeneration)
                IsSearching = false;
        }
    }

    /// <summary>
    /// Manually sets the Python path and updates status.
    /// </summary>
    /// <param name="pythonPath">Path to Python executable.</param>
    public Task SetPythonPathAsync(string pythonPath) =>
        RunEnvironmentAsync(operation => SetPythonPathCoreAsync(pythonPath, "Custom", operation));

    private async Task SetPythonPathCoreAsync(string pythonPath, string source, GdsEnvironmentOperation operation)
    {
        operation.Publish(() => CustomPythonPath = pythonPath);
        operation.Publish(() => PythonPathSource = source);
        operation.Publish(() => _exportService.SetCustomPythonPath(pythonPath));
        operation.Publish(() => OnPythonPathChanged?.Invoke(pythonPath));
        await CheckEnvironmentCoreAsync(operation);
    }

    /// <summary>
    /// Selects a discovered Python installation.
    /// </summary>
    /// <param name="installation">The Python installation to use.</param>
    [RelayCommand]
    public Task SelectPython(PythonInstallation installation) => SelectPython(installation, CancellationToken.None);

    public Task SelectPython(PythonInstallation installation, CancellationToken cancellationToken) =>
        RunEnvironmentAsync(operation => SetPythonPathCoreAsync(installation.Path, installation.Source, operation), cancellationToken);

}

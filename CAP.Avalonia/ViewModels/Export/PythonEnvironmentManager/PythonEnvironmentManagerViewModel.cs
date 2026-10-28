using CAP_Core.Export.Contracts;
using System.ComponentModel;
using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Export.PythonEnvironmentManager;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;

/// <summary>
/// ViewModel for the Python Environment Manager panel. Creates managed Python venvs
/// (Nazca + gdsfactory pre-installed) and, together with the discovered system Pythons
/// (see the SystemInterpreters partial), presents them as one unified interpreter list —
/// the single place to pick the active interpreter for export and preview (issue #645).
/// All long-running ops are async, report progress, and are cancellable.
/// </summary>
public partial class PythonEnvironmentManagerViewModel : ObservableObject, IAsyncDisposable
{
    /// <summary>Retires panel operations and awaits all process/download cleanup.</summary>
    public Task StopAsync()
    {
        if (_startup != null) _startup.PropertyChanged -= OnStartupChanged;
        return _work.StopAsync();
    }
    public ValueTask DisposeAsync() => new(StopAsync());
    private readonly ShellWorkScope _work = new();
    private readonly IPythonEnvironmentRegistry _registry;
    private readonly IManagedEnvironmentPackages _packages;
    private readonly IPythonEnvironmentHealth _healthChecker;
    private readonly ManagedPythonEnvironmentOptions _options;
    private readonly CAP_Core.Export.Contracts.IPythonDiscovery _discovery;
    private readonly Func<string?> _getActiveInterpreterPath;
    private readonly IManagedPythonProvisioner _provisioner;
    private readonly PythonStartupViewModel? _startup;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _pythonVersion;

    /// <summary>
    /// Python versions offered in the create-environment dropdown. Limited to versions
    /// with known-good Nazca/pyclipper support (3.14+ currently breaks numpy/nazca);
    /// the default is supplied by the managed environment configuration.
    /// </summary>
    public IReadOnlyList<string> PythonVersionChoices { get; } =
        new[] { "3.10", "3.11", "3.12", "3.13" };

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _canCancel;

    /// <summary>Initialises the ViewModel with core services.</summary>
    /// <param name="registry">Managed-environment registry.</param>
    /// <param name="packages">Managed package setup workflow.</param>
    /// <param name="options">Managed locations and Python creation defaults.</param>
    /// <param name="healthChecker">Environment health prober.</param>
    /// <param name="discovery">Discovers system Python interpreters (issue #645).</param>
    /// <param name="getActiveInterpreterPath">Returns the currently active interpreter path
    /// so discovered system interpreters can be marked active; null treats none as active.</param>
    public PythonEnvironmentManagerViewModel(
        IPythonEnvironmentRegistry registry,
        IManagedEnvironmentPackages packages,
        ManagedPythonEnvironmentOptions options,
        IPythonEnvironmentHealth healthChecker,
        CAP_Core.Export.Contracts.IPythonDiscovery discovery,
        IManagedPythonProvisioner provisioner,
        Func<string?>? getActiveInterpreterPath = null,
        PythonStartupViewModel? startup = null,
        ApplicationWorkCoordinator? applicationWork = null)
    {
        _registry = registry;
        _packages = packages;
        _options = options;
        _pythonVersion = options.DefaultPythonVersion;
        _healthChecker = healthChecker;
        _discovery = discovery;
        _getActiveInterpreterPath = getActiveInterpreterPath ?? (() => null);
        _provisioner = provisioner;
        _startup = startup;
        RebuildInterpreters();
        _work.RegisterWith(applicationWork, StopAsync);
        if (startup != null)
            startup.PropertyChanged += OnStartupChanged;
    }

    private void OnStartupChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PythonStartupViewModel.IsRunning) && _startup?.IsRunning == false)
            RebuildInterpreters();
    }

    /// <summary>Name of the environment created by the one-click "install Nazca" offers.</summary>
    public const string DefaultEnvironmentName = "nazca";

    /// <summary>
    /// One-click entry point used by the GDS-export fallback and the export guard:
    /// creates the default Nazca environment with default settings. No-ops (with an
    /// explanatory status) when an environment of that name already exists, and does
    /// nothing while another operation runs.
    /// </summary>
    public async Task StartDefaultNazcaInstallAsync()
    {
        if (OperationInProgress()) return;

        if (_registry.Exists(_options.DefaultEnvironmentName))
        {
            ProgressText = $"Environment '{_options.DefaultEnvironmentName}' already exists — "
                + "select it in the list below, or remove it and recreate it if it is broken.";
            return;
        }

        PythonVersion = _options.DefaultPythonVersion;
        await CreateAndInstallCoreAsync(_options.DefaultEnvironmentName, _options.DefaultPythonVersion);
    }

    /// <summary>
    /// Creates a new venv and installs the full dependency set — Nazca + pyclipper and
    /// gdsfactory + ubcpdk — so a freshly created environment works for both the Nazca and
    /// the gdsfactory export without a separate install step (issue #645). The user only
    /// picks the Python version; the environment name is generated automatically
    /// (e.g. <c>py3.11</c>, with a numeric suffix on collision — issue #698).
    /// </summary>
    [RelayCommand]
    private async Task CreateAndInstallAsync()
    {
        if (OperationInProgress()) return;
        var version = PythonVersion.Trim();
        if (!EnvironmentNaming.IsValidPythonVersion(version))
        {
            ProgressText = "Please enter a plain Python version, e.g. 3.11 or 3.11.4.";
            return;
        }

        var name = EnvironmentNaming.GenerateName(version, _registry.Exists);
        await CreateAndInstallCoreAsync(name, version);
    }

    /// <summary>
    /// Shared create + install flow for a validated, non-colliding environment name.
    /// </summary>
    private async Task CreateAndInstallCoreAsync(string name, string pythonVersion)
    {
        var venvPath = Path.Combine(_options.EnvironmentsRoot, name);
        var env = new PythonEnvironment { Name = name, VenvPath = venvPath };

        _registry.AddOrUpdate(env);
        RebuildInterpreters();

        await RunLongOperationAsync(async ct =>
        {
            var progress = CreateProgress(env, ct);

            await _provisioner.ProvisionAsync(env, pythonVersion, progress, ct);
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            _registry.AddOrUpdate(env);
        }, env, $"Environment '{name}' is ready (Nazca + gdsfactory).");
    }

    /// <summary>
    /// Ensures a managed environment with gdsfactory exists and is active — used by the
    /// gdsfactory export to auto-provision on first use. Installs gdsfactory (+ubcpdk) into
    /// the active managed env, or creates the default environment (Nazca + gdsfactory) when
    /// none exists, then activates it. Reports progress; returns true when gdsfactory is
    /// available afterwards. No-ops to false while another operation runs.
    /// </summary>
    public Task<bool> EnsureGdsFactoryInstalledAsync(IProgress<string> progress, CancellationToken ct) =>
        _work.RunAsync(stopping => EnsureGdsFactoryInstalledCoreAsync(progress, ct, stopping));

    private async Task<bool> EnsureGdsFactoryInstalledCoreAsync(
        IProgress<string> progress, CancellationToken ct, CancellationToken stopping)
    {
        if (OperationInProgress()) return false;

        var active = _registry.GetActive();
        var target = active ?? _registry.GetAll().FirstOrDefault();

        IsBusy = true;
        CanCancel = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping);
        _cts = cancellation;
        try
        {
            var token = cancellation.Token;
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            target = await _packages.EnsureGdsFactoryAsync(target, progress, token, ct, stopping);
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            _registry.AddOrUpdate(target);
            _registry.SetActive(target.Name);
            RebuildInterpreters();
            return target.GdsFactoryVersion != null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            ProgressText = $"gdsfactory install failed: {ex.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            if (ReferenceEquals(_cts, cancellation)) _cts = null;
        }
    }

    /// <summary>
    /// Ensures a single pip package — a mode-solver backend such as "gdsfactory[femwell]",
    /// "EMpy", or "tidy3d" — is installed in a managed environment and that environment is
    /// active. Used by the mode-solver dialog and canvas probe to auto-provision a backend
    /// on first use, mirroring <see cref="EnsureGdsFactoryInstalledAsync"/>. Installs into
    /// the active managed env, or creates the default environment (Nazca + gdsfactory) when
    /// none exists, then activates it. Reports progress; returns true when the install
    /// completed. No-ops to false while another operation runs or the spec is blank.
    /// </summary>
    public Task<bool> EnsureBackendInstalledAsync(string packageSpec, IProgress<string> progress, CancellationToken ct) =>
        _work.RunAsync(stopping => EnsureBackendInstalledCoreAsync(packageSpec, progress, ct, stopping));

    private async Task<bool> EnsureBackendInstalledCoreAsync(
        string packageSpec, IProgress<string> progress, CancellationToken ct, CancellationToken stopping)
    {
        if (string.IsNullOrWhiteSpace(packageSpec)) return false;
        if (OperationInProgress())
        {
            progress.Report("An environment operation is already running — try again once it finishes.");
            return false;
        }

        var active = _registry.GetActive();
        var target = active ?? _registry.GetAll().FirstOrDefault();

        IsBusy = true;
        CanCancel = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping);
        _cts = cancellation;
        try
        {
            var token = cancellation.Token;
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            target = await _packages.EnsureBackendAsync(target, packageSpec, progress, token, ct, stopping);
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            _registry.AddOrUpdate(target);
            // Activating pushes the env's interpreter into the shared preference the mode
            // solver resolves lazily, so the retry runs against the freshly-installed env.
            _registry.SetActive(target.Name);
            RebuildInterpreters();
            return true;
        }
        catch (OperationCanceledException)
        {
            // Let cancellation surface to the caller's own "Cancelled." handler instead of
            // masquerading as an install failure (#691 review).
            throw;
        }
        catch (Exception ex)
        {
            // Surface the real reason (uv stderr) — the caller points the user here.
            var reason = $"Backend install failed: {ex.Message}";
            ProgressText = reason;
            progress.Report(reason);
            return false;
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            if (ReferenceEquals(_cts, cancellation)) _cts = null;
        }
    }

    /// <summary>Cancels the in-progress long operation.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        ProgressText = "Cancelling…";
    }

    private IProgress<string> CreateProgress(PythonEnvironment env, CancellationToken ct) =>
        new Progress<string>(msg =>
        {
            if (ct.IsCancellationRequested || _work.IsStopping) return;
            ProgressText = msg;
            Interpreters.FirstOrDefault(i => i.ManagedName == env.Name)?.RefreshAll();
        });

    private bool OperationInProgress()
    {
        if (IsBusy || _work.IsStopping) return true;
        if (_startup?.IsRunning != true) return false;
        ProgressText = CAP.Avalonia.Services.Localization.LocalizationService.Instance.Translate("PythonStartup.Working");
        return true;
    }

    private Task RunLongOperationAsync(
        Func<CancellationToken, Task> operation,
        PythonEnvironment env,
        string successMessage) => _work.RunAsync(stopping => RunLongOperationCoreAsync(operation, env, successMessage, stopping));

    private async Task RunLongOperationCoreAsync(Func<CancellationToken, Task> operation,
        PythonEnvironment env, string successMessage, CancellationToken stopping)
    {
        IsBusy = true;
        CanCancel = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        _cts = cancellation;
        try
        {
            await operation(cancellation.Token);
            _work.ThrowIfStopping();
            cancellation.Token.ThrowIfCancellationRequested();
            _registry.AddOrUpdate(env);
            RebuildInterpreters();
            ProgressText = successMessage;
        }
        catch (OperationCanceledException)
        {
            env.Status = PythonEnvironmentStatus.Broken;
            env.LastError = "Operation was cancelled.";
            _registry.AddOrUpdate(env);
            RebuildInterpreters();
            ProgressText = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            env.Status = PythonEnvironmentStatus.Broken;
            env.LastError = ex.Message;
            _registry.AddOrUpdate(env);
            RebuildInterpreters();
            ProgressText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            if (ReferenceEquals(_cts, cancellation)) _cts = null;
        }
    }
}

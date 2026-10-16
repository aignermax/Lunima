using CAP.Avalonia.Services;
using CAP_Core.Export.PythonEnvironmentManager;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;

/// <summary>One background setup attempt per desktop start, with an explicit retry after failure.</summary>
public partial class PythonStartupViewModel : ObservableObject
{
    private readonly PythonEnvironmentRegistry _registry;
    private readonly UserPreferencesService _preferences;
    private readonly IManagedPythonProvisioner _provisioner;
    private readonly string _root;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _attempt;
    private int _generation;

    public PythonStartupViewModel(PythonEnvironmentRegistry registry, UserPreferencesService preferences,
        IManagedPythonProvisioner provisioner, string? environmentsRoot = null)
    {
        _registry = registry;
        _preferences = preferences;
        _provisioner = provisioner;
        _root = environmentsRoot ?? UvBootstrapper.EnvironmentsBaseDir;
    }
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RetryCommand))] private bool _isRunning;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(RetryCommand))] private bool _hasError;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _errorText = "";

    /// <summary>Automatic startup is bounded to one attempt; failures remain explicitly retryable.</summary>
    public Task StartOnceAsync() => _attempt ??= RunAsync();
    private bool CanRetry() => HasError && !IsRunning && !_shutdown.IsCancellationRequested;
    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task Retry() => _attempt = RunAsync();

    /// <summary>Cancels downloads and child processes when the desktop exits.</summary>
    public void Cancel()
    {
        _shutdown.Cancel();
        RetryCommand.NotifyCanExecuteChanged();
    }

    private async Task RunAsync()
    {
        if (_shutdown.IsCancellationRequested || IsRunning) return;
        var preferred = _preferences.GetCustomPythonPath();
        var selected = _registry.GetAll().FirstOrDefault(e => SamePath(e.PythonExecutable, preferred));
        // An external interpreter is a deliberate user choice, even when currently broken.
        if (!string.IsNullOrWhiteSpace(preferred) && selected == null) return;
        IsRunning = true;
        var generation = ++_generation;
        ProgressText = "";
        HasError = false;
        ErrorText = "";
        PythonEnvironment? target = null;
        var token = _shutdown.Token;
        try
        {
            var candidates = selected == null ? _registry.GetAll().ToArray() : new[] { selected };
            foreach (var candidate in candidates)
            {
                if (await _provisioner.IsReadyAsync(candidate, token))
                {
                    target = candidate;
                    break;
                }
            }
            if (target == null)
            {
                target = selected ?? _registry.GetAll().FirstOrDefault(e => e.Name == PythonEnvironmentManagerViewModel.DefaultEnvironmentName)
                    ?? new PythonEnvironment { Name = PythonEnvironmentManagerViewModel.DefaultEnvironmentName,
                        VenvPath = Path.Combine(_root, PythonEnvironmentManagerViewModel.DefaultEnvironmentName) };
                if (!EnvironmentNaming.IsInsideDirectory(_root, target.VenvPath))
                    throw new InvalidOperationException("Automatic setup can only repair environments inside Lunima's managed directory.");
                _registry.AddOrUpdate(target);
                var progress = new Progress<string>(message => { if (IsRunning && generation == _generation) ProgressText = message; });
                await _provisioner.ProvisionAsync(target, UvBootstrapper.DefaultPythonVersion, progress, token);
                if (!await _provisioner.IsReadyAsync(target, token))
                    throw new InvalidOperationException(target.LastError ?? "The Python environment is missing required preview packages.");
            }
            token.ThrowIfCancellationRequested();
            target.Status = PythonEnvironmentStatus.Healthy;
            _registry.AddOrUpdate(target);
            // The user may select a different interpreter while installation is running.
            if (SamePath(_preferences.GetCustomPythonPath(), preferred)) _registry.SetActive(target.Name);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (target != null)
            {
                target.Status = PythonEnvironmentStatus.Broken;
                target.LastError = ex.Message;
                try { _registry.AddOrUpdate(target); } catch { /* retain the original actionable error */ }
            }
            ErrorText = ex.Message;
            HasError = true;
        }
        finally { IsRunning = false; }
    }

    private static bool SamePath(string? a, string? b) => string.Equals(a, b,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

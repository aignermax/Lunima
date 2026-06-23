using System.Collections.ObjectModel;
using CAP_Core.Export;
using CAP_Core.Export.PythonEnvironmentManager;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;

/// <summary>
/// ViewModel for the Python Environment Manager panel.
/// Manages named Python venvs: create, install Nazca, health-check, repair, remove, activate.
/// All long-running ops are async, report progress, and are cancellable.
/// </summary>
public partial class PythonEnvironmentManagerViewModel : ObservableObject
{
    private readonly PythonEnvironmentRegistry _registry;
    private readonly UvBootstrapper _bootstrapper;
    private readonly NazcaPackageInstaller _installer;
    private readonly EnvironmentHealthChecker _healthChecker;

    private CancellationTokenSource? _cts;

    public ObservableCollection<PythonEnvironmentItemViewModel> Environments { get; } = new();

    [ObservableProperty]
    private PythonEnvironmentItemViewModel? _selectedEnvironment;

    [ObservableProperty]
    private string _newEnvironmentName = string.Empty;

    [ObservableProperty]
    private string _pythonVersion = UvBootstrapper.DefaultPythonVersion;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _canCancel;

    /// <summary>True when the selected environment is set as active.</summary>
    public bool IsSelectedEnvActive => SelectedEnvironment?.IsActive == true;

    /// <summary>Initialises the ViewModel with core services.</summary>
    public PythonEnvironmentManagerViewModel(
        PythonEnvironmentRegistry registry,
        UvBootstrapper bootstrapper,
        NazcaPackageInstaller installer,
        EnvironmentHealthChecker healthChecker)
    {
        _registry = registry;
        _bootstrapper = bootstrapper;
        _installer = installer;
        _healthChecker = healthChecker;

        RefreshList();
    }

    /// <summary>Creates a new venv and installs Nazca + pyclipper into it.</summary>
    [RelayCommand]
    private async Task CreateAndInstallAsync()
    {
        var name = NewEnvironmentName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ProgressText = "Please enter a name for the new environment.";
            return;
        }

        if (_registry.Exists(name))
        {
            ProgressText = $"An environment named '{name}' already exists.";
            return;
        }

        var venvPath = Path.Combine(UvBootstrapper.EnvironmentsBaseDir, name);
        var env = new PythonEnvironment { Name = name, VenvPath = venvPath };

        _registry.AddOrUpdate(env);
        RefreshList();

        await RunLongOperationAsync(async ct =>
        {
            var progress = CreateProgress(env);

            env.Status = PythonEnvironmentStatus.Creating;
            progress.Report("Locating uv...");

            var uvPath = await _bootstrapper.EnsureUvAsync(progress, ct);

            await _bootstrapper.CreateVenvAsync(uvPath, venvPath, PythonVersion, progress, ct);

            env.Status = PythonEnvironmentStatus.Installing;
            await _installer.InstallAsync(uvPath, venvPath, progress, ct);

            await _healthChecker.CheckAsync(env);
            _registry.AddOrUpdate(env);
        }, env, $"Environment '{name}' is ready.");
    }

    /// <summary>Re-installs packages into the selected environment (repair).</summary>
    [RelayCommand]
    private async Task RepairAsync()
    {
        if (SelectedEnvironment == null) return;
        var env = SelectedEnvironment.Environment;

        await RunLongOperationAsync(async ct =>
        {
            var progress = CreateProgress(env);
            env.Status = PythonEnvironmentStatus.Installing;

            var uvPath = await _bootstrapper.EnsureUvAsync(progress, ct);
            await _installer.InstallAsync(uvPath, env.VenvPath, progress, ct);
            await _healthChecker.CheckAsync(env);
            _registry.AddOrUpdate(env);
        }, env, $"Repair of '{env.Name}' complete.");
    }

    /// <summary>Checks the health of the selected environment.</summary>
    [RelayCommand]
    private async Task CheckHealthAsync()
    {
        if (SelectedEnvironment == null) return;
        var env = SelectedEnvironment.Environment;

        IsBusy = true;
        ProgressText = $"Checking '{env.Name}'...";
        try
        {
            await _healthChecker.CheckAsync(env);
            _registry.AddOrUpdate(env);
            SelectedEnvironment.RefreshAll();
            ProgressText = $"Health check done: {SelectedEnvironment.StatusBadge}";
        }
        catch (Exception ex)
        {
            ProgressText = $"Health check failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Sets the selected environment as the active Python for export/preview.</summary>
    [RelayCommand]
    private void SetActive()
    {
        if (SelectedEnvironment == null) return;
        _registry.SetActive(SelectedEnvironment.Name);
        RefreshList();
        OnPropertyChanged(nameof(IsSelectedEnvActive));
        ProgressText = $"'{SelectedEnvironment.Name}' is now the active environment.";
    }

    /// <summary>Removes the selected environment's venv directory and registry entry.</summary>
    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (SelectedEnvironment == null) return;
        var env = SelectedEnvironment.Environment;

        IsBusy = true;
        ProgressText = $"Removing '{env.Name}'...";
        try
        {
            if (Directory.Exists(env.VenvPath))
                await Task.Run(() => Directory.Delete(env.VenvPath, recursive: true));

            _registry.Remove(env.Name);
            RefreshList();
            ProgressText = $"'{env.Name}' removed.";
        }
        catch (Exception ex)
        {
            ProgressText = $"Remove failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Cancels the in-progress long operation.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        ProgressText = "Cancelling…";
    }

    private void RefreshList()
    {
        var activeName = _registry.GetActive()?.Name;
        Environments.Clear();
        foreach (var env in _registry.GetAll())
            Environments.Add(new PythonEnvironmentItemViewModel(env) { IsActive = env.Name == activeName });
    }

    private IProgress<string> CreateProgress(PythonEnvironment env) =>
        new Progress<string>(msg =>
        {
            ProgressText = msg;
            Environments.FirstOrDefault(e => e.Name == env.Name)?.RefreshAll();
        });

    private async Task RunLongOperationAsync(
        Func<CancellationToken, Task> operation,
        PythonEnvironment env,
        string successMessage)
    {
        IsBusy = true;
        CanCancel = true;
        _cts = new CancellationTokenSource();
        try
        {
            await operation(_cts.Token);
            _registry.AddOrUpdate(env);
            RefreshList();
            ProgressText = successMessage;
        }
        catch (OperationCanceledException)
        {
            env.Status = PythonEnvironmentStatus.Broken;
            env.LastError = "Operation was cancelled.";
            _registry.AddOrUpdate(env);
            RefreshList();
            ProgressText = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            env.Status = PythonEnvironmentStatus.Broken;
            env.LastError = ex.Message;
            _registry.AddOrUpdate(env);
            RefreshList();
            ProgressText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
            _cts?.Dispose();
            _cts = null;
        }
    }
}

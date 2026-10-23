using CAP_Core.Export.Contracts;
using System.Collections.ObjectModel;
using CAP_Core.Export.PythonEnvironmentManager;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;

/// <summary>
/// The unified interpreter list (issue #645): the Python Environments tab is the single hub,
/// listing managed environments <em>and</em> discovered system Pythons in one list. Every row
/// shows its Nazca and gdsfactory version and is activated the same way; managed rows also
/// offer a health check and removal. Managed activation and system activation both flow
/// through <see cref="PythonEnvironmentRegistry"/> so export and preview pick the choice up
/// immediately.
/// </summary>
public partial class PythonEnvironmentManagerViewModel
{
    private List<PythonInstallation> _discoveredSystem = new();
    private int _discoveryGeneration;

    /// <summary>Managed environments and discovered system Pythons, in one list.</summary>
    public ObservableCollection<InterpreterEntryViewModel> Interpreters { get; } = new();

    [ObservableProperty]
    private bool _isDiscoveringInterpreters;

    /// <summary>True when discovery has finished and the list is empty.</summary>
    public bool ShowNoInterpretersHint => !IsDiscoveringInterpreters && Interpreters.Count == 0;

    partial void OnIsDiscoveringInterpretersChanged(bool value) =>
        OnPropertyChanged(nameof(ShowNoInterpretersHint));

    /// <summary>
    /// Discovers system interpreters and rebuilds the unified list. A newer run supersedes an
    /// in-flight one so rapid tab switches cannot duplicate the list. Safe to call on
    /// navigation.
    /// </summary>
    [RelayCommand]
    public Task RefreshInterpretersAsync() => _work.RunAsync(RefreshInterpretersCoreAsync);

    private async Task RefreshInterpretersCoreAsync(CancellationToken ct)
    {
        var generation = ++_discoveryGeneration;
        IsDiscoveringInterpreters = true;
        try
        {
            var found = await _discovery.DiscoverPythonWithNazcaAsync(ct);
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            if (generation != _discoveryGeneration)
                return;   // a newer discovery owns the list now

            _discoveredSystem = found;
            RebuildInterpreters();
        }
        catch (OperationCanceledException) when (_work.IsStopping || ct.IsCancellationRequested) { }
        finally
        {
            if (generation == _discoveryGeneration)
                IsDiscoveringInterpreters = false;
        }
    }

    /// <summary>
    /// Rebuilds <see cref="Interpreters"/> from the registry (managed) plus the cached
    /// discovery result (system), refreshing each row's active marker. System Pythons whose
    /// path is already a managed environment are dropped so an interpreter never appears
    /// twice. Cheap — no subprocess probing — so it runs after every managed change.
    /// </summary>
    internal void RebuildInterpreters()
    {
        if (_work.IsStopping) return;
        var activePath = _getActiveInterpreterPath();
        var activeName = _registry.GetActive()?.Name;

        Interpreters.Clear();

        var managedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var env in _registry.GetAll())
        {
            managedPaths.Add(env.PythonExecutable);
            var isActive = env.Name == activeName || IsSamePath(env.PythonExecutable, activePath);
            Interpreters.Add(new InterpreterEntryViewModel(env, isActive));
        }

        foreach (var install in _discoveredSystem)
        {
            if (managedPaths.Contains(install.Path))
                continue;   // already listed as a managed environment
            Interpreters.Add(new InterpreterEntryViewModel(install, IsSamePath(install.Path, activePath)));
        }

        OnPropertyChanged(nameof(ShowNoInterpretersHint));
    }

    /// <summary>
    /// Activates an interpreter: managed rows go through the registry by name, system rows
    /// through <see cref="PythonEnvironmentRegistry.SetActiveExternalPath"/>. Both clear the
    /// other kind of active selection, so exactly one interpreter is ever active.
    /// </summary>
    /// <param name="entry">The interpreter row the user clicked.</param>
    [RelayCommand]
    private void SetActiveInterpreter(InterpreterEntryViewModel entry)
    {
        if (entry == null || _work.IsStopping) return;

        if (entry.IsManaged)
            _registry.SetActive(entry.ManagedName);
        else
            _registry.SetActiveExternalPath(entry.Path);

        RebuildInterpreters();
        ProgressText = $"Active interpreter set to {entry.Path}.";
    }

    /// <summary>Re-probes a managed environment's health (managed rows only).</summary>
    /// <param name="entry">The managed row to check.</param>
    [RelayCommand]
    private Task CheckHealthInterpreter(InterpreterEntryViewModel entry) =>
        _work.RunAsync(ct => CheckHealthInterpreterCoreAsync(entry, ct));

    private async Task CheckHealthInterpreterCoreAsync(InterpreterEntryViewModel entry, CancellationToken ct)
    {
        if (entry?.ManagedName == null || OperationInProgress()) return;
        var env = _registry.GetAll().FirstOrDefault(e => e.Name == entry.ManagedName);
        if (env == null) return;

        IsBusy = true;
        ProgressText = $"Checking '{env.Name}'...";
        try
        {
            await _healthChecker.CheckAsync(env, ct);
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            _registry.AddOrUpdate(env);
            entry.RefreshAll();
            ProgressText = $"Health check done: {entry.StatusBadge}";
        }
        catch (OperationCanceledException) when (_work.IsStopping || ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ProgressText = $"Health check failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Removes a managed environment: deletes its venv directory (only ever inside the managed
    /// environments directory — a tampered path is left untouched) and its registry entry.
    /// </summary>
    /// <param name="entry">The managed row to remove.</param>
    [RelayCommand]
    private Task RemoveInterpreter(InterpreterEntryViewModel entry) =>
        _work.RunAsync(ct => RemoveInterpreterCoreAsync(entry, ct));

    private async Task RemoveInterpreterCoreAsync(InterpreterEntryViewModel entry, CancellationToken ct)
    {
        if (entry?.ManagedName == null || OperationInProgress()) return;
        var env = _registry.GetAll().FirstOrDefault(e => e.Name == entry.ManagedName);
        if (env == null) return;

        IsBusy = true;
        ProgressText = $"Removing '{env.Name}'...";
        try
        {
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            var deletable = EnvironmentNaming.IsInsideDirectory(
                UvBootstrapper.EnvironmentsBaseDir, env.VenvPath);
            if (deletable && Directory.Exists(env.VenvPath))
                await Task.Run(() => Directory.Delete(env.VenvPath, recursive: true));

            _registry.Remove(env.Name);
            // Finish durable removal after deletion, but never publish into a retired panel.
            _work.ThrowIfStopping();
            ct.ThrowIfCancellationRequested();
            RebuildInterpreters();
            ProgressText = deletable
                ? $"'{env.Name}' removed."
                : $"'{env.Name}' removed from the list; its path ({env.VenvPath}) lies outside "
                  + "the managed environments directory and was left untouched.";
        }
        catch (OperationCanceledException) when (_work.IsStopping || ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ProgressText = $"Remove failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsSamePath(string path, string? other) =>
        !string.IsNullOrEmpty(other) && string.Equals(path, other, StringComparison.OrdinalIgnoreCase);
}

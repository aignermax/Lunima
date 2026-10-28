using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CAP_Core;
using CAP_Core.Analysis.CircuitOptimization;
using CAP_Core.Components.ComponentHelpers;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.Lifetime;
using CAP_Core.Documents;
using CAP.Avalonia.ViewModels.Canvas;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;

/// <summary>
/// ViewModel for the circuit optimization panel (issue #820): runs a seeded,
/// budget-limited hill-climb over all slider parameters on the canvas and shows
/// the top-N improved variants with one-click, undo-safe apply.
/// </summary>
public partial class CircuitOptimizationViewModel : ObservableObject, IAsyncDisposable
{
    private readonly CommandManager _commandManager;
    private readonly ErrorConsoleService? _errorConsole;
    private readonly ICircuitOptimizationRunner _runner;
    private DesignCanvasViewModel? _canvas;
    private readonly ShellWorkScope _work = new();
    private readonly ObservablePublicationQueue _publication = new();
    private bool _runAdmitted;
    private AnalysisSweepOperation? _activeRun;
    private AsyncCancellationScope? _userCancellation;
    private DocumentIdentity? _configuredDocument;
    private OptimizationVariantBinding? _binding;
    private long _version;
    private bool _refreshingTargets;
    private OptimizationTargetOption? _refreshTarget;
    private bool _refreshPending;
    private Task? _retirement;
    private readonly object _retirementGate = new();

    [ObservableProperty]
    private int _evaluationBudget = 50;

    [ObservableProperty]
    private int _seed = 42;

    [ObservableProperty]
    private bool _maximize = true;

    [ObservableProperty]
    private bool _isOptimizing;

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _baselineText = "";

    [ObservableProperty]
    private bool _hasParameters;

    [ObservableProperty]
    private OptimizationTargetOption? _selectedTarget;

    /// <summary>Selectable optimization targets, rebuilt from the canvas.</summary>
    public ObservableCollection<OptimizationTargetOption> Targets { get; } = new();

    /// <summary>Ranked list of improved variants from the last run.</summary>
    public ObservableCollection<OptimizationVariantViewModel> Variants { get; } = new();

    /// <summary>Initializes a new instance of <see cref="CircuitOptimizationViewModel"/>.</summary>
    /// <param name="commandManager">Undo-aware command manager used for variant apply.</param>
    /// <param name="errorConsole">Optional service for error logging.</param>
    public CircuitOptimizationViewModel(CommandManager commandManager, ErrorConsoleService? errorConsole = null,
        ICircuitOptimizationRunner? runner = null, ApplicationWorkCoordinator? applicationWork = null)
    {
        _commandManager = commandManager ?? throw new ArgumentNullException(nameof(commandManager));
        _errorConsole = errorConsole;
        _runner = runner ?? new CircuitOptimizationRunner();
        _work.RegisterWith(applicationWork, StopAsync);
    }

    /// <summary>Attaches the panel to the canvas and builds targets/parameters.</summary>
    public void ConfigureForCanvas(DesignCanvasViewModel canvas)
    {
        if (_work.IsStopping) return;
        if (_canvas != null) _canvas.Components.CollectionChanged -= ComponentsChanged;
        Invalidate();
        var version = _version;
        _canvas = canvas;
        try { _configuredDocument = canvas.Document.CaptureIdentity(); }
        catch (OperationCanceledException) { _configuredDocument = null; }
        canvas.Components.CollectionChanged += ComponentsChanged;
        _refreshPending = _runAdmitted;
        _publication.Publish(() =>
        {
            if (!IsCurrent(version, canvas)) return;
            Variants.Clear();
            if (!IsCurrent(version, canvas)) return;
            BaselineText = "";
            if (!IsCurrent(version, canvas)) return;
            StatusText = "";
            if (IsCurrent(version, canvas)) RefreshFromCanvas();
        });
    }

    private void ComponentsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_work.IsStopping || !ReferenceEquals(sender, _canvas?.Components)) return;
        Invalidate();
        _refreshPending = _runAdmitted;
        RefreshFromCanvas();
    }

    private void Invalidate()
    {
        _version++;
        _binding = null;
        _activeRun?.Cancel();
    }

    private bool IsCurrent(long version, DesignCanvasViewModel canvas) => !_work.IsStopping
        && version == _version && ReferenceEquals(_canvas, canvas)
        && _configuredDocument is { } identity && canvas.Document.IsCurrentIdentity(identity);

    partial void OnSelectedTargetChanged(OptimizationTargetOption? value)
    {
        if (!_refreshingTargets || !ReferenceEquals(value, _refreshTarget)) Invalidate();
    }
    partial void OnSeedChanged(int value) => Invalidate();
    partial void OnEvaluationBudgetChanged(int value) => Invalidate();
    partial void OnMaximizeChanged(bool value) => Invalidate();

    /// <summary>Re-reads tunable parameters and target options from the canvas.</summary>
    public void RefreshFromCanvas()
    {
        if (_canvas == null || _runAdmitted || _work.IsStopping) return;
        var canvas = _canvas;
        var version = _version;
        _publication.Publish(() =>
        {
            if (!IsCurrent(version, canvas) || _runAdmitted) return;
            HasParameters = canvas.Components.Any(c => c.HasSliders);
            if (IsCurrent(version, canvas)) RebuildTargets(canvas, version);
        });
    }

    private void RebuildTargets(DesignCanvasViewModel canvas, long version)
    {
        string? previousSelection = SelectedTarget?.DisplayName;
        Targets.Clear();
        if (!IsCurrent(version, canvas)) return;
        foreach (var option in OptimizationTargetFactory.Build(canvas.Components))
        {
            Targets.Add(option);
            if (!IsCurrent(version, canvas)) return;
        }
        _refreshingTargets = true;
        try
        {
            _refreshTarget = Targets.FirstOrDefault(t => t.DisplayName == previousSelection)
                ?? Targets.FirstOrDefault();
            SelectedTarget = _refreshTarget;
        }
        finally { _refreshingTargets = false; _refreshTarget = null; }
    }

    [RelayCommand]
    private Task RunOptimization() => _work.RunAsync(RunCoreAsync);

    private async Task RunCoreAsync(CancellationToken stop)
    {
        if (_canvas == null || _runAdmitted || _configuredDocument == null) return;
        var canvas = _canvas;
        var version = _version;
        if (!IsCurrent(version, canvas)) return;
        _runAdmitted = true;
        AnalysisSweepOperation? operation = null;
        AsyncCancellationScope? cancellation = null;
        try
        {
            operation = new AnalysisSweepOperation(_work, canvas.Document,
                () => IsCurrent(version, canvas), stop, stop, _configuredDocument);
            _activeRun = operation;
            var components = canvas.Components.Where(value => value.HasSliders).ToArray();
            var parameters = components.Select(value => new OptimizationParameter(value.Component, 0,
                $"{value.Name} · {value.SliderLabel}")).ToArray();
            if (parameters.Length == 0) { StatusText = Translate("Optimize.NoParameters"); return; }
            var target = SelectedTarget;
            if (target == null || target.PinIds.Count == 0) { StatusText = Translate("Optimize.NoTarget"); return; }
            var grid = CanvasSimulationBuilder.TryBuildGrid(canvas);
            if (grid == null) { StatusText = Translate("Optimize.NoCircuit"); return; }
            var settings = new OptimizationSettings(parameters,
                new PinPowerObjective(target.PinIds.ToArray(), target.DisplayName, Maximize),
                StandardWaveLengths.RedNM, EvaluationBudget, Seed);
            var binding = new OptimizationVariantBinding(canvas, version, components, parameters);
            var snapshot = CircuitOptimizationSnapshot.Capture(settings, grid);
            operation.ThrowIfObsolete();
            _binding = null;
            await _publication.PublishAsync(() =>
            {
                operation.ThrowIfObsolete();
                IsOptimizing = true;
                operation.ThrowIfObsolete();
                Variants.Clear();
                operation.ThrowIfObsolete();
                BaselineText = "";
            });
            operation.ThrowIfObsolete();
            cancellation = new AsyncCancellationScope(operation.Token);
            _userCancellation = cancellation;
            var progress = new Progress<OptimizationProgress>(value =>
            {
                _publication.Publish(() =>
                {
                    if (!ReferenceEquals(_activeRun, operation) || !operation.CanPublish || cancellation.Token.IsCancellationRequested) return;
                    StatusText = string.Format(Translate("Optimize.Progress"), value.EvaluationsDone, value.Budget, value.BestScore);
                });
            });
            var result = await _runner.RunAsync(snapshot, cancellation.Token, progress);
            operation.ThrowIfObsolete();
            _activeRun = null; // A queued progress callback cannot overwrite final feedback.
            await _publication.PublishAsync(() => operation.TryPublish(() => PopulateVariants(result, binding, operation)));
        }
        catch (OperationCanceledException)
        {
            if (operation?.IsContextCurrent == true && cancellation?.Token.IsCancellationRequested == true)
                StatusText = string.Format(Translate("Optimize.Cancelled"), 0, 0);
        }
        catch (Exception error)
        {
            if (operation?.CanPublish != true) return;
            _errorConsole?.LogError($"Circuit optimization failed: {error.Message}", error);
            if (operation.CanPublish) StatusText = string.Format(Translate("Optimize.Failed"), error.Message);
        }
        finally
        {
            if (ReferenceEquals(_activeRun, operation)) _activeRun = null;
            if (ReferenceEquals(_userCancellation, cancellation)) _userCancellation = null;
            try { if (cancellation != null) await cancellation.DisposeAsync(); }
            finally { if (operation != null) await operation.DisposeAsync(); }
            _runAdmitted = false;
            IsOptimizing = false;
            if (_refreshPending)
            {
                _refreshPending = false;
                RefreshFromCanvas();
            }
        }
    }

    [RelayCommand]
    private void CancelOptimization() => _userCancellation?.Cancel();

    private void PopulateVariants(CircuitOptimizationRunResult result, OptimizationVariantBinding binding,
        AnalysisSweepOperation operation)
    {
        _binding = binding;
        BaselineText = string.Format(Translate("Optimize.Baseline"), result.BaselineScore);
        if (!operation.CanPublish || !ReferenceEquals(_binding, binding)) return;
        var apply = CreateApplyCallback(new WeakReference<CircuitOptimizationViewModel>(this), binding.Version);
        int rank = 1;
        foreach (var candidate in result.TopVariants)
        {
            Variants.Add(new OptimizationVariantViewModel(rank++, candidate.Score.ToString("F4"),
                $"+{candidate.ImprovementOver(result.BaselineScore):F4}", binding.Summary(candidate),
                candidate.ParameterValues, apply));
            if (!operation.CanPublish || !ReferenceEquals(_binding, binding)) return;
        }
        string key = result.WasCancelled ? "Optimize.Cancelled"
            : result.TopVariants.Count == 0 ? "Optimize.NoImprovement" : "Optimize.Complete";
        StatusText = string.Format(Translate(key), result.EvaluationsUsed, result.TopVariants.Count);
    }

    private static Action<OptimizationVariantViewModel> CreateApplyCallback(
        WeakReference<CircuitOptimizationViewModel> editor, long version) => variant =>
    {
        if (editor.TryGetTarget(out var owner)) owner.ApplyVariant(variant, version);
    };

    private void ApplyVariant(OptimizationVariantViewModel variant, long version)
    {
        var canvas = _canvas;
        var binding = _binding;
        if (canvas == null || binding == null || !IsCurrent(version, canvas) || !Variants.Contains(variant)) return;
        try
        {
            using var operation = canvas.Document.CaptureOperation(_configuredDocument!.Value);
            // Capture the lease first, then check the displayed revision: an intervening edit
            // cannot slip between the stamp check and acquisition of fresh commit permission.
            if (!binding.IsCurrent(canvas, version)) return;
            if (!operation.TryCommitAndAdvance(() =>
            {
                if (_work.IsStopping || version != _version || !ReferenceEquals(_binding, binding)
                    || !binding.HasCurrentMembers(canvas)) throw new OperationCanceledException();
                _commandManager.ExecuteCommand(binding.CreateCommand(variant));
            })) return;
            if (!IsCurrent(version, canvas) || !ReferenceEquals(_binding, binding)) return;
            if (!operation.TryPublish(binding.AdvanceAfterOwnApply)) return;
            foreach (var other in Variants.ToArray())
            {
                other.IsApplied = false;
                if (!CanShowApplied()) return;
            }
            variant.IsApplied = true;
            if (CanShowApplied()) StatusText = string.Format(Translate("Optimize.Applied"), variant.Rank);

            bool CanShowApplied() => IsCurrent(version, canvas) && ReferenceEquals(_binding, binding)
                && binding.IsCurrent(canvas, version);
        }
        catch (OperationCanceledException) { }
    }

    public Task StopAsync()
    {
        TaskCompletionSource completion;
        lock (_retirementGate)
        {
            if (_retirement != null) return _retirement;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _retirement = completion.Task;
        }
        _ = RetireAsync(completion);
        return completion.Task;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task RetireAsync(TaskCompletionSource completion)
    {
        try
        {
            var draining = _work.StopAsync();
            Invalidate();
            if (_canvas != null) _canvas.Components.CollectionChanged -= ComponentsChanged;
            try { await draining; }
            finally
            {
                _canvas = null;
                _configuredDocument = null;
                _binding = null;
                await _publication.PublishAsync(() =>
                {
                    Targets.Clear();
                    Variants.Clear();
                    HasParameters = false;
                    IsOptimizing = false;
                    BaselineText = "";
                });
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private static string Translate(string key) => LocalizationService.Instance.Translate(key);
}

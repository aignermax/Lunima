using CAP_Core.Analysis.LogicAnalysis.GateView;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Logic.GateView;

/// <summary>
/// Captures graph bindings and compact physics on the document owner, classifies detached
/// inputs on a worker, and admits only the current document/request's result.
/// </summary>
public partial class GateViewViewModel
{
    /// <summary>Quiet period after the last design edit before the gate view rebuilds.</summary>
    internal static readonly TimeSpan RebuildDebounce = TimeSpan.FromMilliseconds(250);

    private readonly Func<int> _wavelengthNm;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly GateViewGraphExtractor _extractor = new();
    private readonly GateFunctionClassifier _classifier;
    private AsyncCancellationScope? _rebuildCancellation;
    private long _rebuildVersion;
    private readonly Services.Lifetime.ShellWorkScope _work = new();
    private bool _isStale = true;

    /// <summary>The gate graph currently drawn; empty until the first rebuild completes.</summary>
    [ObservableProperty]
    private GateViewGraph _graph = GateViewGraph.Empty;

    /// <summary>True while a rebuild runs in the background.</summary>
    [ObservableProperty]
    private bool _isRebuilding;

    /// <summary>The rebuild currently in flight (test seam: await it to observe the result).</summary>
    internal Task PendingRebuild { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Schedules a debounced rebuild: compact input capture stays on the document owner,
    /// optical classification runs on a worker, and newer requests supersede pending work.
    /// </summary>
    public void RequestRebuild()
    {
        if (_work.IsStopping) return;
        _isStale = true;
        if (!IsShown)
            return;
        ScheduleRebuild(debounce: !ReferenceEquals(Graph, GateViewGraph.Empty), deferCapture: true);
    }

    /// <summary>Rebuilds immediately (no debounce) and completes when the new graph is published.</summary>
    public Task RebuildNowAsync()
    {
        if (_work.IsStopping) return Task.CompletedTask;
        ScheduleRebuild(debounce: false, deferCapture: false);
        return PendingRebuild;
    }

    private void ScheduleRebuild(bool debounce, bool deferCapture)
    {
        _isStale = true;
        var version = ++_rebuildVersion;
        _rebuildCancellation?.Cancel();
        PendingRebuild = _work.RunAsync(async stop =>
        {
            await using var source = new AsyncCancellationScope(stop);
            _rebuildCancellation = source;
            try { await RebuildAsync(source.Token, stop, version, debounce, deferCapture); }
            finally { if (ReferenceEquals(_rebuildCancellation, source)) _rebuildCancellation = null; }
        });
    }

    private void OnDesignChanged()
    {
        _isStale = true;
        if (IsShown)
            RequestRebuild();
    }

    private async Task RebuildAsync(CancellationToken token, CancellationToken ownerToken, long version, bool debounce,
        bool deferCapture)
    {
        AnalysisSweepOperation? operation = null;
        try
        {
            // Scheduled UI requests may supersede each other in the same owner turn.
            // Yield on that context before reading live inputs; explicit RebuildNowAsync
            // retains its immediate capture contract. No live state moves to a worker.
            if (debounce)
                await _delay(RebuildDebounce, token);
            if (deferCapture) await Task.Yield();
            _work.ThrowIfStopping();
            token.ThrowIfCancellationRequested();
            if (version != _rebuildVersion) return;
            IsRebuilding = true;
            operation = new AnalysisSweepOperation(_work, _canvas.Document,
                () => version == _rebuildVersion, ownerToken, token);
            var components = _canvas.Components.Select(c => c.Component).ToList();
            var connections = _canvas.Connections.Select(c => c.Connection).ToList();
            var wavelength = _wavelengthNm();
            // Topology extraction is tolerant of partially edited wiring and retains the real
            // selection/layout bindings here. The worker receives only detached optical values.
            var graph = _extractor.Extract(components, connections, operation.Token);
            var snapshot = GateViewClassificationSnapshot.Capture(graph, wavelength, operation.Token);
            operation.ThrowIfObsolete();
            var classifier = _classifier;
            var workerToken = operation.Token;
            var kinds = await Task.Run(() => classifier.ClassifyAsync(snapshot, workerToken), workerToken);
            operation.TryPublish(() =>
            {
                GateFunctionClassifier.Apply(graph, kinds);
                Graph = graph;
                _isStale = false;
                RaiseChanged();
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request — that one publishes.
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            if (version == _rebuildVersion && !_work.IsStopping) _isStale = true;
        }
        finally
        {
            if (version == _rebuildVersion) IsRebuilding = false;
            if (operation != null) await operation.DisposeAsync();
        }
    }

    /// <summary>True when the drawn graph may lag behind the design (an edit since the last rebuild).</summary>
    public bool IsStale => _isStale;
}

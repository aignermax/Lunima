using CAP_Core.Analysis.LogicAnalysis.GateView;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Logic.GateView;

/// <summary>
/// Rebuild half of <see cref="GateViewViewModel"/>: the gate graph is extracted and its
/// gates classified off the UI thread, debounced behind design edits, and only the newest
/// request publishes — so even the CPU-sized examples never stall an interaction.
/// </summary>
public partial class GateViewViewModel
{
    /// <summary>Quiet period after the last design edit before the gate view rebuilds.</summary>
    internal static readonly TimeSpan RebuildDebounce = TimeSpan.FromMilliseconds(250);

    private readonly Func<int> _wavelengthNm;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly GateViewGraphExtractor _extractor = new();
    private readonly GateFunctionClassifier _classifier = new();
    private CancellationTokenSource? _rebuildCts;
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
    /// Schedules a debounced background rebuild. Returns immediately — the extraction
    /// never runs on the caller's thread — and supersedes any rebuild still waiting.
    /// </summary>
    public void RequestRebuild()
    {
        if (_work.IsStopping) return;
        _isStale = true;
        if (!IsShown)
            return;
        ScheduleRebuild(debounce: !ReferenceEquals(Graph, GateViewGraph.Empty));
    }

    /// <summary>Rebuilds immediately (no debounce) and completes when the new graph is published.</summary>
    public Task RebuildNowAsync()
    {
        if (_work.IsStopping) return Task.CompletedTask;
        ScheduleRebuild(debounce: false);
        return PendingRebuild;
    }

    private void ScheduleRebuild(bool debounce)
    {
        _rebuildCts?.Cancel();
        PendingRebuild = _work.RunAsync(async stop =>
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(stop);
            _rebuildCts = source;
            try { await RebuildAsync(source.Token, debounce); }
            finally { if (ReferenceEquals(_rebuildCts, source)) _rebuildCts = null; }
        });
    }

    private void OnDesignChanged()
    {
        _isStale = true;
        if (IsShown)
            RequestRebuild();
    }

    private async Task RebuildAsync(CancellationToken token, bool debounce)
    {
        try
        {
            if (debounce)
                await _delay(RebuildDebounce, token);
            _work.ThrowIfStopping();
            token.ThrowIfCancellationRequested();
            IsRebuilding = true;
            var components = _canvas.Components.Select(c => c.Component).ToList();
            var connections = _canvas.Connections.Select(c => c.Connection).ToList();
            var wavelength = _wavelengthNm();
            var graph = await Task.Run(async () =>
            {
                var extracted = _extractor.Extract(components, connections, token);
                await _classifier.ClassifyAsync(extracted, wavelength, token);
                return extracted;
            }, token);
            if (_work.IsStopping || token.IsCancellationRequested)
                return;
            Graph = graph;
            _isStale = false;
            RaiseChanged();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request — that one publishes.
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // The design changed under the background read; the next edit or toggle retries.
            _isStale = true;
        }
        finally
        {
            if (_work.IsStopping || !token.IsCancellationRequested)
                IsRebuilding = false;
        }
    }

    /// <summary>True when the drawn graph may lag behind the design (an edit since the last rebuild).</summary>
    public bool IsStale => _isStale;
}

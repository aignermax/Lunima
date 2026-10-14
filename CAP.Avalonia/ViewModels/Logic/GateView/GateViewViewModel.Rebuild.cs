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
        _isStale = true;
        if (!IsShown)
            return;
        _rebuildCts?.Cancel();
        _rebuildCts = new CancellationTokenSource();
        PendingRebuild = RebuildAsync(_rebuildCts.Token, debounce: !ReferenceEquals(Graph, GateViewGraph.Empty));
    }

    /// <summary>Rebuilds immediately (no debounce) and completes when the new graph is published.</summary>
    public Task RebuildNowAsync()
    {
        _rebuildCts?.Cancel();
        _rebuildCts = new CancellationTokenSource();
        PendingRebuild = RebuildAsync(_rebuildCts.Token, debounce: false);
        return PendingRebuild;
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
            if (token.IsCancellationRequested)
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
            if (!token.IsCancellationRequested)
                IsRebuilding = false;
        }
    }

    /// <summary>True when the drawn graph may lag behind the design (an edit since the last rebuild).</summary>
    public bool IsStale => _isStale;
}

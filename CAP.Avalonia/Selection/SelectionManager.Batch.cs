using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Selection;

public partial class SelectionManager
{
    /// <summary>Defers collection notifications until all selection flags are consistent.</summary>
    public IDisposable BeginUpdate() => new SelectionUpdate(
        ((SelectionCollection<ComponentViewModel>)SelectedComponents).DeferNotifications(),
        ((SelectionCollection<WaveguideConnectionViewModel>)SelectedConnections).DeferNotifications(),
        ((SelectionCollection<CanvasFrozenPathViewModel>)SelectedFrozenPaths).DeferNotifications());

    /// <summary>Selects the current editable scope without walking into closed groups.</summary>
    public void SelectAll(IEnumerable<ComponentViewModel> components,
        IEnumerable<WaveguideConnectionViewModel> connections,
        IEnumerable<CanvasFrozenPathViewModel> frozenPaths)
    {
        // Materialize before clearing: callers may pass the existing selection itself.
        var parts = components.Distinct().ToArray();
        var routes = connections.Distinct().ToArray();
        var paths = frozenPaths.Distinct().ToArray();
        using var update = BeginUpdate();
        ClearSelection();
        foreach (var part in parts)
        {
            part.IsSelected = true;
            SelectedComponents.Add(part);
        }
        foreach (var route in routes)
        {
            route.IsSelected = true;
            SelectedConnections.Add(route);
        }
        foreach (var path in paths)
        {
            path.IsSelected = true;
            SelectedFrozenPaths.Add(path);
        }
    }

    private sealed class SelectionUpdate(params IDisposable[] batches) : IDisposable
    {
        public void Dispose()
        {
            foreach (var batch in batches) batch.Dispose();
        }
    }
}

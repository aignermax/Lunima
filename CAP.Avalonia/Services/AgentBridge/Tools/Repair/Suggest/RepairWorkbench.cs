using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.CrossingInsertion;
using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Documents;
using CAP_Core.Routing.Snapshots;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;

/// <summary>One disposable trial: independent commands, view bindings, graph and document lifetime.</summary>
internal sealed class RepairWorkbench : IAsyncDisposable
{
    internal AgentDesignSession Session { get; }
    private readonly RepairTrialPermission _permission;
    private readonly DocumentOperation _operation;
    private readonly CancellationTokenSource _stop;
    private readonly List<Task> _derivedWork = new();

    internal static async Task<RepairWorkbench> CreateAsync(ExampleDesignsService examples, RoutingInputSnapshot input, RepairCrossingTemplate? crossing,
        RepairTrialPermission permission, CanvasRoutingFactory? routingFactory,
        (double MinX, double MinY, double MaxX, double MaxY) bounds, Guid? scopeId = null, RepairGraphBuilder? graphBuilder = null)
    {
        permission.EnsureCurrent();
        var builder = graphBuilder ?? new RepairGraphBuilder();
        // Only detached snapshot values and the graph builder cross this boundary.
        // An accepted construction is always drained, even after its live owner is revoked.
        var graph = await Task.Run(() => builder.Build(input));
        RepairWorkbench? workbench = null;
        permission.Commit(() => workbench = new RepairWorkbench(examples, graph, crossing, permission, routingFactory, bounds, scopeId));
        return workbench!;
    }

    private RepairWorkbench(ExampleDesignsService examples, RoutingWorkGraph graph, RepairCrossingTemplate? crossing,
        RepairTrialPermission permission, CanvasRoutingFactory? routingFactory,
        (double MinX, double MinY, double MaxX, double MaxY) bounds, Guid? scopeId)
    {
        _permission = permission;
        var canvas = new DesignCanvasViewModel(graph.Router, graph.Manager, routingFactory, preserveRoutingState: true);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(permission.CancellationToken);
        try
        {
            var commands = new CommandManager();
            canvas.CommandManager = commands;
            canvas.ChipMinX = bounds.MinX; canvas.ChipMinY = bounds.MinY;
            canvas.ChipMaxX = bounds.MaxX; canvas.ChipMaxY = bounds.MaxY;
            foreach (var component in graph.Components)
            {
                CrossingCanvasSync.AddCrossing(canvas, component, component.TemplateName, null);
                if (component is ComponentGroup group)
                {
                    var view = canvas.Components.Last();
                    foreach (var pin in group.ExternalPins)
                        canvas.AllPins.Add(new PinViewModel(pin.InternalPin, view));
                }
            }
            CrossingCanvasSync.SyncConnections(canvas, graph.Manager.Connections.ToArray());
            if (graph.Manager.CrossingService is { } service)
            {
                void Added(Component component)
                {
                    CrossingCanvasSync.AddCrossing(canvas, component, component.TemplateName, null);
                    CrossingCanvasSync.SyncConnections(canvas, graph.Manager.Connections.ToArray());
                }
                void Removed(Component component)
                {
                    CrossingCanvasSync.RemoveCrossing(canvas, component);
                    CrossingCanvasSync.SyncConnections(canvas, graph.Manager.Connections.ToArray());
                }
                var journal = new CrossingUndoJournal(canvas, Added, Removed);
                journal.Attach(commands);
                service.ComponentAdded = Added;
                service.ComponentRemoved = Removed;
                service.ChangeRecorded = journal.Record;
            }
            if (scopeId is { } capturedScope)
            {
                var scope = graph.Components.SelectMany(Ancestors).FirstOrDefault(group => group.Id == capturedScope)
                    ?? throw new InvalidDataException("The captured edit scope is absent from the private routing graph.");
                canvas.Groups.AdoptCapturedScope(scope);
            }
            IReadOnlyList<ComponentTemplate> templates = crossing == null ? [] : [crossing.Bind(graph)];
            Session = new AgentDesignSession(canvas, commands, () => null, examples, () => templates);
            _operation = canvas.Document.CaptureOperation(_stop.Token);
        }
        catch
        {
            _stop.Dispose();
            canvas.DisposeDocumentBindings();
            throw;
        }
    }

    private static IEnumerable<ComponentGroup> Ancestors(Component component)
    {
        for (var current = component as ComponentGroup ?? component.ParentGroup as ComponentGroup;
             current != null; current = current.ParentGroup as ComponentGroup)
            yield return current;
    }

    internal Task<bool> ExecuteAsync(RepairEditPlan plan) => plan.ExecuteStepsAsync(Session, Commit, RouteAsync);

    private void Commit(Action action)
    {
        var bounds = Session.Canvas.Dies.DeferScopeBoundsUpdates(_operation, _permission);
        try
        {
            _permission.Commit(() =>
            {
                using var suspended = Session.Canvas.Routing.SuspendAutomaticRouting();
                if (!_operation.TryCommitAndAdvance(action)) throw new OperationCanceledException("The private repair trial stopped.");
            });
        }
        finally
        {
            bounds.Dispose();
            if (!bounds.Completion.IsCompletedSuccessfully) _derivedWork.Add(bounds.Completion);
        }
    }

    private async Task RouteAsync()
    {
        await Task.WhenAll(_derivedWork);
        _permission.EnsureCurrent();
        await Session.Canvas.Routing.RecalculateRoutesAsync(_operation, _permission);
        _permission.EnsureCurrent();
        if (!await RoutingAwaiter.WaitForIdleAsync(Session.Canvas, cancellationToken: _operation.CancellationToken))
            throw new OperationCanceledException("The private repair routing did not finish.");
        _permission.EnsureCurrent();
    }

    public async ValueTask DisposeAsync()
    {
        try { await _stop.CancelAsync(); }
        finally
        {
            // Keep the document operation admitted until every borrowed grid worker has drained.
            try { await Task.WhenAll(_derivedWork); }
            finally
            {
                try { await _operation.DisposeAsync(); }
                finally
                {
                    try { await Session.Canvas.Document.RetireAsync(); }
                    finally
                    {
                        try { Session.Canvas.DisposeDocumentBindings(); }
                        finally { _stop.Dispose(); }
                    }
                }
            }
        }
    }
}

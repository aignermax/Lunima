using Avalonia.Headless.XUnit;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.ViewModels.Canvas;

public sealed class RoutingWorkerIsolationTests
{
    [AvaloniaFact]
    public async Task CalculatedButUnpublishedPass_DoesNotMutateLiveRouteOrGrid()
    {
        var fixture = new RoutingWorkerFixture();
        var grid = fixture.Router.PathfindingGrid;
        var running = fixture.Orchestrator.RecalculateRoutesAsync();
        try
        {
            await fixture.Orchestrator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Orchestrator.Begin.TrySetResult();
            await fixture.Orchestrator.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Wire.RoutedPath.ShouldBeNull("calculation owns a private wire until a UI frame is admitted");
            fixture.Router.PathfindingGrid.ShouldBeSameAs(grid);
        }
        finally { await fixture.FinishAsync(running); }
        fixture.Wire.RoutedPath.ShouldNotBeNull("a current completed result still publishes");
    }

    [AvaloniaFact]
    public async Task WorkerReadsCapturedGeometry_AfterCallerMutatesLiveCoreObjects()
    {
        var fixture = new RoutingWorkerFixture();
        var expectedEnd = fixture.Wire.EndPin.GetAbsolutePosition();
        var running = fixture.Orchestrator.RecalculateRoutesAsync();
        try
        {
            await fixture.Orchestrator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // Deliberately bypass document notifications: this tests ownership, not cancellation.
            fixture.End.PhysicalX += 2000;
            fixture.Orchestrator.Begin.TrySetResult();
            await fixture.Orchestrator.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Orchestrator.Finish.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Wire.RoutedPath.ShouldNotBeNull();
            fixture.Wire.RoutedPath.Segments.Last().EndPoint.ShouldBe((expectedEnd.x, expectedEnd.y));
            fixture.End.PhysicalX.ShouldBe(2200);
        }
        finally { await fixture.FinishAsync(running); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleDocumentOrScope_RejectsCalculatedAndQueuedRouteFrames(bool changeScope)
    {
        var fixture = new RoutingWorkerFixture();
        var running = fixture.Orchestrator.RecalculateRoutesAsync();
        try
        {
            await fixture.Orchestrator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (changeScope) fixture.Document.ChangeScope(Guid.NewGuid());
            else fixture.Document.ReplaceDocument();
            var newer = new RoutedPath();
            newer.Segments.Add(new StraightSegment(5, 15, 25, 15, 0));
            fixture.Wire.RestoreCachedPath(newer);
            fixture.Orchestrator.Begin.TrySetResult();
            await fixture.Orchestrator.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Orchestrator.FlushUpdates();
            fixture.Orchestrator.Finish.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Orchestrator.FlushUpdates();
            fixture.Wire.RoutedPath.ShouldBeSameAs(newer, "a stale worker cannot replace a newer scope's route");
        }
        finally { await fixture.FinishAsync(running); }
    }

    [AvaloniaFact]
    public async Task BackgroundGridReinitialization_RejectsOldDocumentResult()
    {
        var fixture = new RoutingWorkerFixture();
        var original = fixture.Router.PathfindingGrid;
        var running = fixture.Orchestrator.ReinitializeAStarRoutingInBackgroundAsync(-100, -100, 500, 500, null);
        try
        {
            await fixture.Orchestrator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Document.ReplaceDocument();
            fixture.Orchestrator.Begin.TrySetResult();
            await fixture.Orchestrator.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Orchestrator.Finish.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Router.PathfindingGrid.ShouldBeSameAs(original);
        }
        finally { await fixture.FinishAsync(running); }
    }
}

using Shouldly;

namespace UnitTests.ViewModels.Canvas;

public sealed class RoutingExplicitStopAdmissionTests
{
    [Fact]
    public async Task ExplicitStop_RevokesPublicationBeforeNotifyingSubscribers()
    {
        var fixture = new RoutingWorkerFixture();
        // No UI context is needed: the existing worker fixture queues progressive updates,
        // and the completed result is held before the actual orchestrator publication boundary.
        var running = Task.Run(() => fixture.Orchestrator.RecalculateRoutesAsync());
        var completedDuringStop = false;
        void DuringStop()
        {
            fixture.Orchestrator.Finish.TrySetResult();
            completedDuringStop = running.Wait(TimeSpan.FromSeconds(5));
        }
        try
        {
            fixture.Orchestrator.Begin.TrySetResult();
            await fixture.Orchestrator.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Wire.RoutedPath.ShouldBeNull("worker geometry is still private before publication");
            fixture.Orchestrator.StopRequested += DuringStop;
            await Task.Run(fixture.Orchestrator.CancelRouting).WaitAsync(TimeSpan.FromSeconds(8));
            completedDuringStop.ShouldBeTrue("Stop notifications may run application code while the worker finishes");
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Orchestrator.FlushUpdates();
            fixture.Wire.RoutedPath.ShouldBeNull("an explicit Stop must revoke the completed result before external callbacks run");
        }
        finally
        {
            fixture.Orchestrator.StopRequested -= DuringStop;
            await fixture.FinishAsync(running);
            await fixture.Document.RetireAsync();
        }
    }
}

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Connections;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Canvas;

public partial class RoutingOrchestratorProgressTests
{
    [AvaloniaTheory]
    [InlineData(RoutingPhase.Finishing)]
    [InlineData(RoutingPhase.CrossingInsertion)]
    [InlineData(RoutingPhase.ContentionRepair)]
    public async Task ParkedLaterStage_ShowsActualPhaseAndStopsOnCancel(RoutingPhase target)
    {
        WaveguideConnectionManager manager = null!;
        var orchestrator = CreateOrchestratorWithDeferredConnections(1, value => manager = value);
        manager.CrossingInsertion = new CAP_Core.Routing.CrossingInsertion.CrossingInsertionService(() => null);
        orchestrator.BlockWorker = true;
        orchestrator.ReportedPhase = target;
        orchestrator.ReportComplex = true;
        var task = orchestrator.RecalculateRoutesAsync();
        try
        {
            await orchestrator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await AwaitStatus(orchestrator, "Routing.Status." + target);
            string first = orchestrator.RoutingStatusText;
            first.ShouldContain(LocalizationService.Instance.Translate("Routing.Status." + target));
            first.ShouldNotContain(LocalizationService.Instance.Translate("Routing.Status.ComplexPath"));
            int count = orchestrator.RoutedConnectionCount;
            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (orchestrator.RoutingStatusText == first && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            orchestrator.RoutingStatusText.ShouldNotBe(first);
            orchestrator.RoutedConnectionCount.ShouldBe(count);
            orchestrator.CancelRouting();
            string cancelled = orchestrator.RoutingStatusText;
            orchestrator.Mailbox!.PhaseChanged(RoutingPhase.Initial);
            Dispatcher.UIThread.RunJobs();
            orchestrator.RoutingStatusText.ShouldBe(cancelled);
        }
        finally
        {
            orchestrator.Release.TrySetResult();
            await PumpUntilDone(task);
        }
        string finished = orchestrator.RoutingStatusText;
        orchestrator.Mailbox!.PhaseChanged(RoutingPhase.Initial);
        Dispatcher.UIThread.RunJobs();
        orchestrator.RoutingStatusText.ShouldBe(finished);
    }

}

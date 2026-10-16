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
        using var parked = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Action<RoutingPhase>? late = null;
        orchestrator.BuildConnectionProcessFloorProvider = () =>
        {
            // Factory executes before routing installs its pass callbacks; provider runs afterward.
            return (_, _) =>
            {
                if (late == null)
                {
                    late = manager.OnRoutingPhaseChanged;
                    manager.OnRoutingPhaseChanged = phase =>
                    {
                        late!(phase);
                        if (phase != target) return;
                        manager.OnComplexRouteStarted?.Invoke();
                        parked.Set();
                        release.Wait();
                    };
                }
                return null;
            };
        };
        var task = orchestrator.RecalculateRoutesAsync();
        try
        {
            parked.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
            Dispatcher.UIThread.RunJobs();
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
            late!(RoutingPhase.Initial);
            Dispatcher.UIThread.RunJobs();
            orchestrator.RoutingStatusText.ShouldBe(cancelled);
        }
        finally
        {
            release.Set();
            await PumpUntilDone(task);
        }
        string finished = orchestrator.RoutingStatusText;
        late!(RoutingPhase.Initial);
        Dispatcher.UIThread.RunJobs();
        orchestrator.RoutingStatusText.ShouldBe(finished);
    }

}

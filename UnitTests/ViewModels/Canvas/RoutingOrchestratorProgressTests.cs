using System.Collections.ObjectModel;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Canvas;

/// <summary>
/// Tests for the honest routing-status progress (issue #1278): the status text counts
/// routed/total connections with elapsed seconds, and Stop cancels the running pass —
/// already-routed wires keep their routes and the status reports how far the pass got.
/// The assertions on "n/m" substrings are locale-independent; English is pinned so the
/// exact wording checks stay stable regardless of the CI/dev OS language.
/// </summary>
public class RoutingOrchestratorProgressTests
{
    public RoutingOrchestratorProgressTests()
    {
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
    }

    [AvaloniaFact]
    public async Task RecalculateRoutesAsync_ProgressTextReachesTotalOverTotal()
    {
        const int connectionCount = 4;
        var orchestrator = CreateOrchestratorWithDeferredConnections(connectionCount);
        var seen = new List<string>();
        orchestrator.StateChanged += () =>
        {
            if (!string.IsNullOrEmpty(orchestrator.RoutingStatusText))
                seen.Add(orchestrator.RoutingStatusText);
        };

        var task = orchestrator.RecalculateRoutesAsync();
        await PumpUntilDone(task);

        orchestrator.TotalConnectionCount.ShouldBe(connectionCount);
        orchestrator.RoutedConnectionCount.ShouldBe(connectionCount,
            "every deferred (unrouted) connection must be counted once it is routed");
        seen.First().ShouldContain($"0/{connectionCount}");
        seen.ShouldContain(t => t.Contains($"{connectionCount}/{connectionCount}"),
            "the progress text must visibly reach total/total");
        orchestrator.RoutingStatusText.ShouldBe("",
            "a completed pass clears the status, as before");
    }

    [AvaloniaFact]
    public async Task StopRouting_DuringPass_StopsWithoutException_AndReportsProgress()
    {
        const int connectionCount = 6;
        var orchestrator = CreateOrchestratorWithDeferredConnections(connectionCount);

        var task = orchestrator.RecalculateRoutesAsync();
        orchestrator.IsRouting.ShouldBeTrue();
        task.IsCompleted.ShouldBeFalse(
            "routing runs on a background thread — the UI thread must not be blocked");

        orchestrator.CancelRouting();
        await PumpUntilDone(task);

        orchestrator.IsRouting.ShouldBeFalse();
        orchestrator.RoutingStatusText.ShouldContain("/" + connectionCount);
        orchestrator.RoutingStatusText.ShouldContain(
            LocalizationService.Instance.Translate("Routing.Status.Stopped").Split("{0}")[0].Trim());
    }

    [AvaloniaFact]
    public async Task RecalculateRoutesAsync_ProgressTextIsLocalized()
    {
        var orchestrator = CreateOrchestratorWithDeferredConnections(2);

        var task = orchestrator.RecalculateRoutesAsync();

        // The initial status is set synchronously at pass start, from the localized template.
        var expectedStart = string.Format(
            LocalizationService.Instance.Translate("Routing.Status.Progress"), 0, 2, 0);
        orchestrator.RoutingStatusText.ShouldBe(expectedStart);

        await PumpUntilDone(task);
    }

    /// <summary>Pumps the headless dispatcher until the routing task finishes.</summary>
    private static async Task PumpUntilDone(Task task)
    {
        while (!task.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5);
        }
        Dispatcher.UIThread.RunJobs();
        await task;
    }

    /// <summary>
    /// An orchestrator with <paramref name="connectionCount"/> deferred (never routed)
    /// connections, so the next pass routes every one of them and the counter reaches
    /// the total. The ViewModel collections stay empty: the pass only reads them for the
    /// obstacle rebuild and the repaint notifications.
    /// </summary>
    private static RoutingOrchestrator CreateOrchestratorWithDeferredConnections(int connectionCount)
    {
        var router = new WaveguideRouter();
        var manager = new WaveguideConnectionManager(router);
        var orchestrator = new RoutingOrchestrator(
            router,
            manager,
            new ObservableCollection<ComponentViewModel>(),
            new ObservableCollection<WaveguideConnectionViewModel>());
        orchestrator.InitializeAStarRouting();

        for (var i = 0; i < connectionCount; i++)
        {
            var startComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
            startComp.PhysicalX = 0;
            startComp.PhysicalY = i * 200;
            var endComp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
            endComp.PhysicalX = 400;
            endComp.PhysicalY = i * 200;

            var startPin = startComp.PhysicalPins.First(p => p.Name == "out");
            var endPin = endComp.PhysicalPins.First(p => p.Name == "in");
            manager.AddConnectionDeferred(startPin, endPin);
        }

        return orchestrator;
    }
}

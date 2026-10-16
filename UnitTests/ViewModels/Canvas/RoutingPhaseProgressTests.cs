using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas.Services;
using CAP_Core.Components.Connections;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Canvas;

public class RoutingPhaseProgressTests
{
    [AvaloniaTheory]
    [InlineData(RoutingPhase.CrossingInsertion, "Routing.Status.CrossingInsertion")]
    [InlineData(RoutingPhase.Finishing, "Routing.Status.Finishing")]
    [InlineData(RoutingPhase.ContentionRepair, "Routing.Status.ContentionRepair")]
    public async Task LaterPhase_ClearsComplexAndKeepsCountAndTime(RoutingPhase phase, string key)
    {
        LocalizationService.Instance.SetLanguage("en");
        bool current = true;
        int seconds = 0;
        string text = "";
        int updates = 0;
        using var heartbeat = new RoutingProgressHeartbeat(() => current,
            () => $"Routing 3/37 · {seconds} s", value => { text = value; updates++; });
        heartbeat.ComplexRouteStarted();
        text.ShouldContain(LocalizationService.Instance.Translate("Routing.Status.ComplexPath"));
        heartbeat.PhaseChanged(phase);
        heartbeat.ComplexRouteStarted(); // A repair trial is not an initial connection search.
        text.ShouldContain(LocalizationService.Instance.Translate(key));
        text.ShouldNotContain(LocalizationService.Instance.Translate("Routing.Status.ComplexPath"));
        seconds = 1;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!text.Contains("1 s") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        text.ShouldContain("3/37 · 1 s");
        current = false;
        int before = updates;
        heartbeat.PhaseChanged(RoutingPhase.Initial);
        heartbeat.ComplexRouteStarted();
        updates.ShouldBe(before);
    }
}

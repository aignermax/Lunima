using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas.Services;
using Shouldly;

namespace UnitTests.ViewModels.Canvas;

public partial class RoutingOrchestratorProgressTests
{
    private static async Task AwaitStatus(RoutingOrchestrator orchestrator, string key)
    {
        var expected = LocalizationService.Instance.Translate(key);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!orchestrator.RoutingStatusText.Contains(expected) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        orchestrator.RoutingStatusText.ShouldContain(expected);
    }
}

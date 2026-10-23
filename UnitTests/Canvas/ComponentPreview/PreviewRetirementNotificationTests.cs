using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Moq;
using Shouldly;

namespace UnitTests.Canvas.ComponentPreview;

public sealed class PreviewRetirementNotificationTests
{
    [AvaloniaFact]
    public async Task Retirement_DropsAlreadyQueuedRepaintCallbacks()
    {
        var path = Path.Combine(Path.GetTempPath(), "preview-notify-" + Guid.NewGuid());
        var backend = new Mock<NazcaComponentPreviewService>("python", "script", null, null);
        backend.Setup(s => s.RenderAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NazcaPreviewResult { Success = true });
        await using var previews = new GdsPreviewRenderService(backend.Object, new GdsPreviewDiskCache(path));
        var callbacks = 0;
        previews.OnPreviewLoaded += () => callbacks++;
        try
        {
            previews.TryGetGeometry(new("test", "queued-callback", null));
            // The render worker queues a repaint at normal dispatcher priority. Retirement
            // can precede delivery even though the fetch itself is already complete.
            // Hold the dispatcher while this bounded, mocked background fetch posts its callback.
            previews.WaitForPendingAsync().Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue();
            callbacks.ShouldBe(0);
            await previews.StopAsync();
            Dispatcher.UIThread.RunJobs();
            callbacks.ShouldBe(0);
        }
        finally
        {
            await previews.StopAsync();
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }
}

using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Analysis;
using Shouldly;
using Xunit;
using static UnitTests.Analysis.LogicAnalysis.BuildSlicing.CaptureSlicingFixture;

namespace UnitTests.Analysis.LogicAnalysis.BuildSlicing;

/// <summary>
/// <see cref="DispatcherCaptureYield"/>: on the UI thread it lets queued dispatcher work run before
/// the capture resumes and re-validates the operation afterwards; without a running dispatcher it
/// never yields, so plain callers keep their one synchronous capture.
/// </summary>
public class DispatcherCaptureYieldTests
{
    [Fact]
    public void YieldAsync_WithoutDispatcher_CompletesSynchronouslyAndStillValidates()
    {
        var validations = 0;
        var yielding = new DispatcherCaptureYield(() => validations++);

        var task = yielding.YieldAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.ShouldBeTrue();
        validations.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task YieldAsync_OnDispatcher_RunsQueuedWorkBeforeResumingAndValidating()
    {
        await ContinueInsideDispatcherJobAsync();
        var order = new List<string>();
        var yielding = new DispatcherCaptureYield(() => order.Add("validated"));
        Dispatcher.UIThread.Post(() => order.Add("queued input"), DispatcherPriority.Input);
        Dispatcher.UIThread.Post(() => order.Add("queued work"), DispatcherPriority.Normal);

        var task = yielding.YieldAsync(CancellationToken.None);
        order.ShouldBeEmpty("the capture must hand the frame back instead of validating inline");
        await task;

        order.ShouldBe(["queued work", "queued input", "validated"]);
        Dispatcher.UIThread.CheckAccess().ShouldBeTrue("the capture resumes on the owner thread");
    }

    [AvaloniaFact]
    public async Task YieldAsync_OperationObsoleteAfterFrame_ThrowsCancellation()
    {
        await ContinueInsideDispatcherJobAsync();
        var obsolete = false;
        var yielding = new DispatcherCaptureYield(() =>
        {
            if (obsolete) throw new OperationCanceledException("The sweep context changed.");
        });
        Dispatcher.UIThread.Post(() => obsolete = true);

        await Should.ThrowAsync<OperationCanceledException>(() => yielding.YieldAsync(CancellationToken.None));
    }

    [AvaloniaFact]
    public async Task YieldAsync_CancelledWhileYielded_ThrowsCancellation()
    {
        await ContinueInsideDispatcherJobAsync();
        using var cancellation = new CancellationTokenSource();
        var validations = 0;
        var yielding = new DispatcherCaptureYield(() => validations++);
        Dispatcher.UIThread.Post(cancellation.Cancel);

        await Should.ThrowAsync<OperationCanceledException>(() => yielding.YieldAsync(cancellation.Token));
        validations.ShouldBe(0, "a cancelled frame never resumes the capture");
    }
}

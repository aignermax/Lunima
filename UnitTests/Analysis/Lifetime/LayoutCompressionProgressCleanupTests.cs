using System.Collections.Immutable;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

public sealed class LayoutCompressionProgressCleanupTests
{
    [AvaloniaTheory]
    [InlineData("configure")]
    [InlineData("replace")]
    [InlineData("edit")]
    [InlineData("stop")]
    public async Task RevocationAfterAcceptedProgress_OnlySettlesStillOwnedDocumentChanges(string change)
    {
        var canvas = new DesignCanvasViewModel();
        var first = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var second = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        first.PhysicalX = 500; first.PhysicalY = 300;
        second.PhysicalX = 900; second.PhysicalY = 300;
        canvas.Components.Add(new ComponentViewModel(first));
        canvas.Components.Add(new ComponentViewModel(second));
        var wire = new WaveguideConnection { StartPin = first.PhysicalPins[1], EndPin = second.PhysicalPins[0] };
        var start = wire.StartPin.GetAbsolutePosition();
        var end = wire.EndPin.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(start.x, start.y, end.x, end.y, 0));
        wire.RestoreCachedPath(path);
        canvas.RestoreConnectionAndViewModel(wire);
        canvas.ConnectionManager.Connections.ShouldContain(wire);
        var runner = new ProgressRunner();
        await using var panel = new CompressLayoutViewModel(runner: runner);
        panel.Configure(canvas);
        var running = panel.CompressLayoutCommand.ExecuteAsync(null);
        try
        {
            await runner.Barrier.AwaitStartAsync(running);
            Dispatcher.UIThread.RunJobs();
            first.PhysicalX.ShouldBe(400, "the progress frame must actually have reached the document");
            Task stopping = Task.CompletedTask;
            if (change == "configure") panel.Configure(canvas);
            else if (change == "replace") canvas.Document.ReplaceDocument();
            else if (change == "edit")
            {
                second.PhysicalX += 10;
                canvas.Components[1].X = second.PhysicalX;
            }
            else stopping = panel.StopAsync();
            var expectedStatus = panel.StatusText;
            runner.Barrier.Release.TrySetResult();
            await Task.WhenAll(running, stopping).WaitAsync(TimeSpan.FromSeconds(20));
            wire.RoutedPath.ShouldNotBeNull();
            wire.RoutedPath.Segments.ShouldNotBeEmpty();
            if (change == "configure")
            {
                wire.RoutedPath.Segments[0].StartPoint.ShouldBe(wire.StartPin.GetAbsolutePosition());
                wire.RoutedPath.Segments[^1].EndPoint.ShouldBe(wire.EndPin.GetAbsolutePosition());
            }
            else wire.RoutedPath.ShouldBeSameAs(path, "revoked document ownership must forbid route cleanup writes");
            panel.ResultText.ShouldBeEmpty();
            panel.StatusText.ShouldBe(expectedStatus);
        }
        finally
        {
            runner.Barrier.Release.TrySetResult();
            await running;
            await panel.StopAsync();
            await canvas.Document.RetireAsync();
            canvas.DisposeDocumentBindings();
        }
    }

    private sealed class ProgressRunner : ILayoutCompressionRunner
    {
        internal RetirementBarrier Barrier { get; } = new();
        public async Task<LayoutCompressionFrame> RunAsync(LayoutCompressionSnapshot snapshot,
            CancellationToken cancellationToken, IProgress<LayoutCompressionFrame>? progress)
        {
            progress?.Report(new(5, ImmutableArray.Create(new LayoutPosition(400, 300), new LayoutPosition(900, 300))));
            await Barrier.WaitAsync(cancellationToken);
            return snapshot.Solve(default);
        }
    }
}

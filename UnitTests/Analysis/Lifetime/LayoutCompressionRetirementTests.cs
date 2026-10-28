using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using Shouldly;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

public sealed class LayoutCompressionRetirementTests
{
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("remove")]
    [InlineData("configure")]
    public async Task ObsoleteCalculation_CannotMoveComponentsOrPublishFeedback(string change)
    {
        await using var fixture = new Fixture();
        var running = fixture.Panel.CompressLayoutCommand.ExecuteAsync(null);
        await fixture.Runner.Barrier.AwaitStartAsync(running);
        fixture.Target.Component.PhysicalX.ShouldBe(500);
        if (change == "replace") fixture.Canvas.Document.ReplaceDocument();
        else if (change == "remove") fixture.Canvas.Components.Remove(fixture.Target);
        else fixture.Panel.Configure(fixture.Canvas);
        var status = fixture.Panel.StatusText;
        await fixture.Runner.Barrier.FinishAsync(running);
        Dispatcher.UIThread.RunJobs();
        fixture.Target.Component.PhysicalX.ShouldBe(500);
        fixture.Panel.ResultText.ShouldBeEmpty();
        fixture.Panel.StatusText.ShouldBe(status);
    }

    [AvaloniaFact]
    public async Task Retirement_WaitsForActualWorkerAndRejectsLaterWork()
    {
        await using var fixture = new Fixture();
        var running = fixture.Panel.CompressLayoutCommand.ExecuteAsync(null);
        await fixture.Runner.Barrier.AwaitStartAsync(running);
        var stopping = fixture.Panel.StopAsync();
        await fixture.Runner.Barrier.AssertDrainingAsync(stopping);
        await fixture.Runner.Barrier.FinishAsync(running, stopping);
        await fixture.Panel.CompressLayoutCommand.ExecuteAsync(null);
        fixture.Runner.Calls.ShouldBe(1);
        fixture.Target.Component.PhysicalX.ShouldBe(500);
    }

    [AvaloniaFact]
    public async Task SuccessfulRun_AppliesRealCalculationOnDispatcherAndCompletesFeedback()
    {
        await using var fixture = new Fixture();
        var dispatcherAccess = new List<bool>();
        fixture.Target.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is "X" or "Y") dispatcherAccess.Add(Dispatcher.UIThread.CheckAccess());
        };
        var running = fixture.Panel.CompressLayoutCommand.ExecuteAsync(null);
        await fixture.Runner.Barrier.AwaitStartAsync(running);
        fixture.Target.X.ShouldBe(500);
        await fixture.Runner.Barrier.FinishAsync(running);
        Dispatcher.UIThread.RunJobs();
        fixture.Target.X.ShouldBe(50);
        fixture.Target.Component.PhysicalX.ShouldBe(50);
        dispatcherAccess.ShouldNotBeEmpty();
        dispatcherAccess.ShouldAllBe(value => value);
        fixture.Panel.ResultText.ShouldContain("Layout compressed successfully");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal DesignCanvasViewModel Canvas { get; } = new();
        internal ComponentViewModel Target { get; }
        internal HeldRunner Runner { get; } = new();
        internal CompressLayoutViewModel Panel { get; }
        internal Fixture()
        {
            var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
            component.PhysicalX = 500;
            component.PhysicalY = 300;
            Target = new ComponentViewModel(component);
            Canvas.Components.Add(Target);
            Panel = new CompressLayoutViewModel(runner: Runner);
            Panel.Configure(Canvas);
        }
        public async ValueTask DisposeAsync()
        {
            Runner.Barrier.Release.TrySetResult();
            await Panel.StopAsync();
            await Canvas.Document.RetireAsync();
            Canvas.DisposeDocumentBindings();
        }
    }

    private sealed class HeldRunner : ILayoutCompressionRunner
    {
        internal RetirementBarrier Barrier { get; } = new();
        internal int Calls { get; private set; }
        public async Task<LayoutCompressionFrame> RunAsync(LayoutCompressionSnapshot snapshot,
            CancellationToken cancellationToken, IProgress<LayoutCompressionFrame>? progress)
        {
            Calls++;
            await Barrier.WaitAsync(cancellationToken);
            return await Task.Run(() => snapshot.Solve(default, progress));
        }
    }
}

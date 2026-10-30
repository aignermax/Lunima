using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class TransientNestedPublicationTests
{
    [AvaloniaFact]
    public async Task ImmediateRunStartedByConfigureReset_RetainsLeaseUntilQueuedPlotCompletes()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.Components.Add(new ComponentViewModel(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins()));
        var runner = new ImmediateRunner();
        await using var panel = new TimeDomainViewModel(runner: runner);
        Task? running = null;
        var started = false;
        NotifyCollectionChangedEventHandler observer = (_, change) =>
        {
            if (started || change.Action != NotifyCollectionChangedAction.Reset) return;
            started = true;
            running = panel.RunTransientCommand.ExecuteAsync(null);
        };
        panel.Series.CollectionChanged += observer;
        try
        {
            panel.Configure(canvas);
            started.ShouldBeTrue();
            running.ShouldNotBeNull();
            await running.WaitAsync(TimeSpan.FromSeconds(5));

            runner.Calls.ShouldBe(1);
            panel.HasResult.ShouldBeTrue();
            panel.Series.Count.ShouldBe(2, "the accepted nested run must publish its queued plot before releasing its lease");
            panel.PlotModel.Series.Count.ShouldBe(2);
            panel.ResultText.ShouldNotBeEmpty();
            panel.IsRunning.ShouldBeFalse();
        }
        finally
        {
            panel.Series.CollectionChanged -= observer;
            await panel.StopAsync();
            await canvas.Document.RetireAsync();
            canvas.DisposeDocumentBindings();
        }
    }

    private sealed class ImmediateRunner : ITransientSimulationRunner
    {
        internal int Calls { get; private set; }
        public Task<TransientSimulationOutcome> RunAsync(TransientSimulationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            var result = new TimeDomainResult([0, 1e-12, 2e-12], new()
            {
                [Guid.NewGuid()] = [0, 1, 0],
                [Guid.NewGuid()] = [0, 0.5, 0]
            });
            return Task.FromResult(new TransientSimulationOutcome(result, []));
        }
    }
}

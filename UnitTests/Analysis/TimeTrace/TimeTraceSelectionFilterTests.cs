using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using OxyPlot.Series;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.TimeTrace;

/// <summary>Checks that the canvas selection decides which transient curves are plotted.</summary>
public class TimeTraceSelectionFilterTests
{
    /// <summary>Without a selection every curve is plotted.</summary>
    [AvaloniaFact]
    public async Task NoSelection_ShowsAllCurves()
    {
        await using var fixture = await RunAsync();
        fixture.Vm.Series.Count.ShouldBe(2);
        fixture.Vm.Series.ShouldAllBe(s => s.IsVisible);
        VisibleSeriesCount(fixture.Vm).ShouldBe(2);
        fixture.Vm.SelectionHint.ShouldBe(Translate("Analysis.TimeDomain.SelectionAll"));
    }

    /// <summary>A selection restricts the plot to the selected components' curves.</summary>
    [AvaloniaFact]
    public async Task Selection_ShowsOnlySelectedComponentCurves()
    {
        await using var fixture = await RunAsync();
        fixture.Canvas.Selection.SelectSingle(fixture.First);

        VisibleSeriesCount(fixture.Vm).ShouldBe(1);
        fixture.Vm.Series.Single(s => s.IsVisible).PinId.ShouldBe(PinOf(fixture.First));
        fixture.Vm.SelectionHint.ShouldBe(string.Format(Translate("Analysis.TimeDomain.SelectionFiltered"), 1));
    }

    /// <summary>Changing or clearing the selection after a run re-filters without simulating again.</summary>
    [AvaloniaFact]
    public async Task SelectionChangeAfterRun_RefiltersWithoutRerunning()
    {
        await using var fixture = await RunAsync();
        fixture.Canvas.Selection.SelectSingle(fixture.First);
        fixture.Canvas.Selection.SelectSingle(fixture.Second);
        fixture.Vm.Series.Single(s => s.IsVisible).PinId.ShouldBe(PinOf(fixture.Second));

        fixture.Canvas.Selection.ClearSelection();
        VisibleSeriesCount(fixture.Vm).ShouldBe(2);
        fixture.Runner.Runs.ShouldBe(1);
    }

    private static int VisibleSeriesCount(TimeDomainViewModel vm) =>
        vm.PlotModel.Series.Cast<LineSeries>().Count(s => s.IsVisible);

    private static string Translate(string key) =>
        CAP.Avalonia.Services.Localization.LocalizationService.Instance.Translate(key);

    private static Guid PinOf(ComponentViewModel component) =>
        component.Component.PhysicalPins.First(p => p.LogicalPin != null).LogicalPin!.IDOutFlow;

    private static async Task<Fixture> RunAsync()
    {
        var canvas = new DesignCanvasViewModel();
        var first = new ComponentViewModel(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        var second = new ComponentViewModel(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        canvas.Components.Add(first);
        canvas.Components.Add(second);
        var traces = new Dictionary<Guid, double[]>
        {
            [PinOf(first)] = new[] { 0.0, 1.0, 0.0 },
            [PinOf(second)] = new[] { 0.0, 0.5, 0.0 }
        };
        var runner = new CountingRunner(new TimeDomainResult(new[] { 0.0, 1e-12, 2e-12 }, traces));
        var vm = new TimeDomainViewModel(runner: runner);
        vm.Configure(canvas);
        await vm.RunTransientCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
        return new Fixture(vm, canvas, first, second, runner);
    }

    private sealed class Fixture(TimeDomainViewModel vm, DesignCanvasViewModel canvas,
        ComponentViewModel first, ComponentViewModel second, CountingRunner runner) : IAsyncDisposable
    {
        public TimeDomainViewModel Vm => vm;
        public DesignCanvasViewModel Canvas => canvas;
        public ComponentViewModel First => first;
        public ComponentViewModel Second => second;
        public CountingRunner Runner => runner;

        public async ValueTask DisposeAsync()
        {
            await vm.StopAsync();
            await canvas.Document.RetireAsync();
            canvas.DisposeDocumentBindings();
        }
    }

    private sealed class CountingRunner(TimeDomainResult result) : ITransientSimulationRunner
    {
        public int Runs { get; private set; }

        public Task<TransientSimulationOutcome> RunAsync(TransientSimulationRequest request, CancellationToken cancellationToken)
        {
            Runs++;
            return Task.FromResult(new TransientSimulationOutcome(result, []));
        }
    }
}

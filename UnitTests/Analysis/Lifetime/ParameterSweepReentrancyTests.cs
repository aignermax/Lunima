using System.Collections.Specialized;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using CAP_Core.Grid;
using Shouldly;

namespace UnitTests.Analysis.Lifetime;

public sealed class ParameterSweepReentrancyTests
{
    [AvaloniaFact]
    public async Task NestedSelectionFromRangeObserver_KeepsBothNewRangeEndpoints()
    {
        var canvas = new DesignCanvasViewModel();
        var first = new ComponentViewModel(TestComponentHelper.CreateComponentWithSlider(2, 3, 2.5));
        var second = new ComponentViewModel(TestComponentHelper.CreateComponentWithSlider(10, 20, 15));
        canvas.Components.Add(first);
        canvas.Components.Add(second);
        await using var panel = new ParameterSweepViewModel();
        var nested = false;
        panel.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName != nameof(panel.StartValue) || nested) return;
            nested = true;
            panel.ConfigureForComponent(second, canvas);
        };
        try
        {
            panel.ConfigureForComponent(first, canvas);
            nested.ShouldBeTrue();
            panel.StartValue.ShouldBe(second.SliderMin);
            panel.EndValue.ShouldBe(second.SliderMax);
        }
        finally
        {
            await canvas.Document.RetireAsync();
            canvas.DisposeDocumentBindings();
        }
    }

    [AvaloniaFact]
    public async Task SelectionChangedByClearingResults_DoesNotAdmitAnObsoleteWorker()
    {
        var runner = new CountingRunner();
        await using var fixture = new ParameterSweepLifetimeFixture(runner: runner);
        fixture.Panel.ResultText = "previous result";
        fixture.Panel.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(fixture.Panel.ResultText) && fixture.Panel.IsSweeping)
                fixture.Panel.ConfigureForComponent(null, fixture.Canvas);
        };
        await fixture.RunAsync();
        runner.Calls.ShouldBe(0);
        fixture.Panel.StatusText.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ErrorObserverReconfiguresPanel_OldFailureDoesNotOverwriteNewStatus()
    {
        var runner = new CountingRunner { Fail = true };
        await using var fixture = new ParameterSweepLifetimeFixture(runner: runner);
        ((INotifyCollectionChanged)fixture.Errors.Entries).CollectionChanged += (_, _) =>
            fixture.Panel.ConfigureForComponent(null, fixture.Canvas);
        await fixture.RunAsync();
        runner.Calls.ShouldBe(1);
        fixture.Errors.Entries.Count.ShouldBe(1);
        fixture.Panel.StatusText.ShouldBeEmpty();
    }

    private sealed class CountingRunner : IParameterSweepRunner
    {
        internal bool Fail { get; init; }
        internal int Calls { get; private set; }
        public Task<List<SweepDataPoint>> RunAsync(SweepConfiguration configuration, GridManager grid,
            IExternalPortManager ports, CancellationToken cancellationToken, IProgress<int>? progress = null)
        {
            Calls++;
            return Fail ? Task.FromException<List<SweepDataPoint>>(new InvalidOperationException("controlled failure"))
                : Task.FromResult(new List<SweepDataPoint>());
        }
    }
}

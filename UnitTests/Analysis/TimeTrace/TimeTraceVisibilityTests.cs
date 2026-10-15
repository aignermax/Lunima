using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CAP.Avalonia.ViewModels.Analysis;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using OxyPlot;
using OxyPlot.Series;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.TimeTrace;

/// <summary>Checks transient visibility updates without replacing chart state.</summary>
public class TimeTraceVisibilityTests
{
    /// <summary>Hiding and restoring a trace retain the user's viewport and data buffers.</summary>
    [Fact]
    public void TogglingTrace_PreservesZoomAndExistingPointBuffers()
    {
        var result = new TimeDomainResult(new[] { 0.0, 1e-12, 2e-12 },
            new Dictionary<Guid, double[]>
            {
                [Guid.NewGuid()] = new[] { 0.0, 1.0, 0.0 },
                [Guid.NewGuid()] = new[] { 0.0, 0.5, 0.0 }
            });
        var vm = new TimeDomainViewModel();
        typeof(TimeDomainViewModel).GetField("_lastResult", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(vm, result);
        typeof(TimeDomainViewModel).GetMethod("BuildPlot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, new object[] { result });
        var model = vm.PlotModel;
        var traces = model.Series.Cast<LineSeries>().ToArray();
        model.Axes[0].Zoom(0.25, 1.25);

        vm.Series[0].IsVisible = false;

        vm.PlotModel.ShouldBeSameAs(model);
        ((IPlotModel)model).Update(false);
        model.Axes[0].ActualMinimum.ShouldBe(0.25);
        model.Axes[0].ActualMaximum.ShouldBe(1.25);
        model.Series.Where(s => s.IsVisible).Single().ShouldBeSameAs(traces[1]);
        vm.Series[0].IsVisible = true;
        model.Series.Where(s => s.IsVisible).Count().ShouldBe(2);
        model.Series[0].ShouldBeSameAs(traces[0]);
        traces[0].Points[1].Y.ShouldBe(1.0);
    }
}


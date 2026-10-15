using System.Diagnostics;
using System.Reflection;
using CAP.Avalonia.Controls.Plotting;
using OxyPlot;
using OxyPlot.Axes;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis.TimeTrace;

/// <summary>Checks tracker values and lookup cost on long traces.</summary>
public class XTrackingLineSeriesTests
{
    /// <summary>Sample growth must not produce proportional pointer latency.</summary>
    [Fact]
    public void TrackingLongTrace_DoesNotScaleLinearlyWithSampleCount()
    {
        var small = CreateSeries(1000);
        var large = CreateSeries(200000);
        Measure(small);
        Measure(large);
        var smallTicks = Measure(small);
        var largeTicks = Measure(large);
        largeTicks.ShouldBeLessThan(smallTicks * 20,
            "200 times more samples must not require a linear scan for each pointer update");
    }

    /// <summary>Preserves interpolation and endpoint behavior.</summary>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(1.25, 2.5)]
    [InlineData(3, 6)]
    [InlineData(4, 6)]
    public void Tracker_InterpolatesAndClamps(double x, double expected)
    {
        var series = CreateSeries(4);
        series.GetNearestPoint(series.Transform(new DataPoint(x, 0)), true)
            .DataPoint.Y.ShouldBe(expected, 1e-9);
    }

    /// <summary>Data replacement invalidates the optimized lookup eligibility.</summary>
    [Fact]
    public void ChangedToUnsortedData_UsesFirstMatchingSegment()
    {
        var series = CreateSeries(4);
        series.Points[1] = new DataPoint(2, 20);
        series.Points[2] = new DataPoint(1, 50);
        ((IPlotModel)series.PlotModel).Update(true);

        series.GetNearestPoint(series.Transform(new DataPoint(1.5, 0)), true)
            .DataPoint.Y.ShouldBe(15, 1e-9);
    }

    /// <summary>Repeated coordinates retain the original first-segment tie break.</summary>
    [Fact]
    public void DuplicateCoordinates_PreserveFirstMatchingSegment()
    {
        var series = CreateSeries(4);
        series.Points[2] = new DataPoint(1, 50);
        ((IPlotModel)series.PlotModel).Update(true);

        series.GetNearestPoint(series.Transform(new DataPoint(1, 0)), true)
            .DataPoint.Y.ShouldBe(2, 1e-9);
    }

    private static XTrackingLineSeries CreateSeries(int count)
    {
        var series = new XTrackingLineSeries();
        for (int i = 0; i < count; i++)
            series.Points.Add(new DataPoint(i, i * 2));
        var model = new PlotModel();
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom });
        model.Axes.Add(new LinearAxis { Position = AxisPosition.Left });
        model.Series.Add(series);
        ((IPlotModel)model).Update(true);
        foreach (var axis in model.Axes)
            typeof(Axis).GetMethod("UpdateTransform", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(axis, new object[] { new OxyRect(0, 0, 400, 200) });
        return series;
    }

    private static long Measure(XTrackingLineSeries series)
    {
        var point = series.Transform(new DataPoint(series.Points.Count - 2.5, 0));
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 2000; i++)
            series.GetNearestPoint(point, true);
        return watch.ElapsedTicks;
    }
}


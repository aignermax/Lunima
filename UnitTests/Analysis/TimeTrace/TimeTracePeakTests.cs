using System.Globalization;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.TimeTrace;
using CAP_Core.LightCalculation.TimeDomainSimulation;
using Shouldly;

namespace UnitTests.Analysis.TimeTrace;

/// <summary>Peak metadata preserves tie, nonfinite, ordering, and table semantics.</summary>
public class TimeTracePeakTests
{
    /// <summary>The first maximum supplies the peak time, including NaN-prefixed traces.</summary>
    [Theory]
    [InlineData(0.0, 2.0, 2.0, 2.0, 1)]
    [InlineData(double.NaN, 2.0, 2.0, 2.0, 1)]
    [InlineData(0.0, double.PositiveInfinity, 2.0, double.PositiveInfinity, 1)]
    [InlineData(double.NaN, double.NaN, double.NaN, double.NaN, 0)]
    public void Peak_PreservesFirstMaximum(double a, double b, double c, double peak, int index)
    {
        var result = Result((Guid.NewGuid(), new[] { a, b, c }));
        var summary = TimeTracePeak.Build(result).Single();
        summary.Power.ShouldBe(peak);
        summary.Index.ShouldBe(index);
    }

    /// <summary>Legend sort remains stable; table retains input order and first-peak time.</summary>
    [Fact]
    public void SharedPeaks_PreserveLegendAndTableOrder()
    {
        var weak = Guid.NewGuid();
        var strong = Guid.NewGuid();
        var tied = Guid.NewGuid();
        var result = Result((weak, new[] { 0.0, 0.5, 0.5 }),
            (strong, new[] { 0.0, 2.0, 0.0 }), (tied, new[] { 0.0, 2.0, 0.0 }));
        var peaks = TimeTracePeak.Build(result);
        var items = TimeTracePlotBuilder.BuildSeriesItems(result, _ => null, peaks);
        items.Select(i => i.PinId).ShouldBe(new[] { strong, tied, weak });
        var text = TimeDomainResultFormatter.FormatResult(result, peaks);
        var lines = text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[2].ShouldStartWith(weak.ToString()[..6]);
        lines[3].ShouldStartWith(strong.ToString()[..6]);
        lines[4].ShouldStartWith(tied.ToString()[..6]);
        lines[2].ShouldEndWith(1.0.ToString("F2", CultureInfo.CurrentCulture));
        lines[2].ShouldContain(0.5.ToString("F6", CultureInfo.CurrentCulture));
    }

    /// <summary>Empty, silent and all-NaN traces remain absent from the legend.</summary>
    [Fact]
    public void SharedPeaks_PreserveSignalFloorFiltering()
    {
        var visible = Guid.NewGuid();
        var result = Result((Guid.NewGuid(), Array.Empty<double>()),
            (Guid.NewGuid(), new[] { double.NaN }), (Guid.NewGuid(), new[] { 1e-13 }),
            (visible, new[] { 1e-12 }));
        TimeTracePlotBuilder.BuildSeriesItems(result, _ => null, TimeTracePeak.Build(result))
            .Single().PinId.ShouldBe(visible);
    }

    private static TimeDomainResult Result(params (Guid Id, double[] Trace)[] traces) =>
        new(new[] { 0.0, 1e-12, 2e-12 }, traces.ToDictionary(t => t.Id, t => t.Trace));
}

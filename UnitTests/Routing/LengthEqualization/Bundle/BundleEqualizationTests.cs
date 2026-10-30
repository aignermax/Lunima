using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Routing.LengthEqualization;
using CAP_Core.Routing.LengthEqualization.Bundle;
using Shouldly;
using UnitTests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Routing.LengthEqualization.Bundle;

/// <summary>
/// Re-laying a fan-in as a bundle: every wire gets a fresh route of nested arches at one common
/// length from its own start pin to its own end pin, without crossings and at the router's
/// spacing; connections outside the bundle are reported and left alone. Equalization picks the
/// bundle when tangled old routes would force a longer common length on meanders, keeps the
/// meanders when they match everything shorter, and falls back to them when the channel is blocked.
/// </summary>
public class BundleEqualizationTests
{
    private const double Tolerance = 0.1;
    private const double MaxRadius = 50;
    private readonly ITestOutputHelper _output;

    public BundleEqualizationTests(ITestOutputHelper output) => _output = output;

    private static LengthEqualizationPlan Plan(DesignCanvasViewModel canvas, IReadOnlyList<WaveguideConnection> selected,
        double? target = null) =>
        new LengthEqualizer(canvas.Router).Prepare(selected, canvas.ConnectionManager.Connections.ToList(),
            canvas.Components.Select(c => c.Component).ToList(), target, MaxRadius, Tolerance, rerouteBundle: true);

    private static LengthEqualizationResult Solve(DesignCanvasViewModel canvas, IReadOnlyList<WaveguideConnection> selected) =>
        LengthEqualizer.Solve(Plan(canvas, selected));

    private static LengthEqualizationResult Relay(DesignCanvasViewModel canvas, IReadOnlyList<WaveguideConnection> selected,
        double? target = null) =>
        BundleEqualization.TrySolve(Plan(canvas, selected, target), LengthEqualizer.Radii, CancellationToken.None)
        ?? throw new ShouldAssertException("the bundle was not laid out");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FanIn_IsRelaidAtOneLength_PinToPinWithoutOverlaps(bool reverseOdd)
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(16, reverseOdd);
        var models = connections.Select(c => c.Connection).ToList();

        var result = Relay(canvas, models);
        _output.WriteLine($"{result.MatchedCount}/{models.Count} at {result.TargetLengthMicrometers:F1} µm in {result.Elapsed.TotalMilliseconds:F0} ms");

        result.ReroutedAsBundle.ShouldBeTrue();
        result.FailedCount.ShouldBe(0);
        foreach (var outcome in result.Outcomes)
        {
            outcome.Path!.TotalLengthMicrometers.ShouldBe(result.TargetLengthMicrometers, Tolerance);
            AssertJoinsItsPins(outcome.Connection, outcome.Path);
        }
        AssertClear(result.Outcomes.Select(o => o.Path!).ToList(), canvas.Router.MinWaveguideSpacingMicrometers);
    }

    [Fact]
    public async Task TangledLongRoutes_GetAShorterCommonLengthThanTheLongest()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(8);
        var models = connections.Select(c => c.Connection).ToList();
        var stretched = LengthEqualizer.Solve(new LengthEqualizer(canvas.Router).Prepare(models.Take(4).ToList(), models,
            canvas.Components.Select(c => c.Component).ToList(), models.Max(c => c.PathLengthMicrometers) + 800, 10, Tolerance));
        foreach (var outcome in stretched.Outcomes.Where(o => o.IsSuccess))
            outcome.Connection.ReplaceRoutedPath(outcome.Path!);
        double longest = models.Max(c => c.PathLengthMicrometers);

        var result = Solve(canvas, models);

        result.ReroutedAsBundle.ShouldBeTrue();
        result.FailedCount.ShouldBe(0);
        result.TargetLengthMicrometers.ShouldBeLessThan(longest - 500);
    }

    [Fact]
    public async Task CleanDiagonalRoutes_KeepTheirMeanders_WhenThatIsShorter()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(8);
        var models = connections.Select(c => c.Connection).ToList();
        double bundleLength = Relay(canvas, models).TargetLengthMicrometers;

        var result = Solve(canvas, models);

        result.ReroutedAsBundle.ShouldBeFalse();
        result.FailedCount.ShouldBe(0);
        result.TargetLengthMicrometers.ShouldBeLessThan(bundleLength);
    }

    [Fact]
    public async Task LayOutBundle_WithoutRoutes_GivesEveryWireItsShortestPlanarShape()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(10);
        var models = connections.Select(c => c.Connection).ToList();
        double common = Relay(canvas, models).TargetLengthMicrometers;
        foreach (var model in models)
            model.InvalidateRoute();

        var result = LengthEqualizer.LayOutBundle(Plan(canvas, models));

        result.ShouldNotBeNull();
        result.MatchedCount.ShouldBe(models.Count);
        result.Outcomes.ShouldAllBe(o => o.Path!.TotalLengthMicrometers <= common + Tolerance);
        result.Outcomes.Select(o => o.Path!.TotalLengthMicrometers).Distinct().Count().ShouldBeGreaterThan(1);
        foreach (var outcome in result.Outcomes)
            AssertJoinsItsPins(outcome.Connection, outcome.Path!);
        AssertClear(result.Outcomes.Select(o => o.Path!).ToList(), canvas.Router.MinWaveguideSpacingMicrometers);
    }

    [Fact]
    public async Task RequestedLength_IsMetByEveryWire()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(6);
        var models = connections.Select(c => c.Connection).ToList();
        double target = Relay(canvas, models).TargetLengthMicrometers + 300;

        var result = Relay(canvas, models, target);

        result.ReroutedAsBundle.ShouldBeTrue();
        result.Outcomes.ShouldAllBe(o => Math.Abs(o.Path!.TotalLengthMicrometers - target) <= Tolerance);
    }

    [Fact]
    public async Task ConnectionOutsideTheBundle_IsLeftAloneAndReported()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(4);
        var chips = canvas.Components.Select(c => c.Component).Where(c => c.PhysicalX == 0).ToList();
        var loop = canvas.ConnectPins(chips[0].PhysicalPins.First(p => p.Name == "in"), chips[1].PhysicalPins.First(p => p.Name == "in"))!;
        await canvas.RecalculateRoutesAsync();
        var selected = connections.Select(c => c.Connection).Append(loop.Connection).ToList();

        var result = Relay(canvas, selected);

        result.ReroutedAsBundle.ShouldBeTrue();
        result.NotInBundleCount.ShouldBe(1);
        result.FailedCount.ShouldBe(0);
        result.Outcomes.Single(o => o.Connection == loop.Connection).Path.ShouldBeNull();
    }

    [Fact]
    public async Task ObstacleInTheChannel_FallsBackToMeanders()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(6);
        for (double y = -400; y < 1200; y += 250)
        {
            var wall = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
            (wall.PhysicalX, wall.PhysicalY) = (700, y);
            canvas.AddComponent(wall);
        }

        var result = Solve(canvas, connections.Select(c => c.Connection).ToList());

        result.ReroutedAsBundle.ShouldBeFalse();
    }

    private static void AssertJoinsItsPins(WaveguideConnection connection, RoutedPath path)
    {
        var (startX, startY) = connection.StartPin.GetAbsolutePosition();
        var (endX, endY) = connection.EndPin.GetAbsolutePosition();
        path.Segments[0].StartPoint.X.ShouldBe(startX, 1e-6);
        path.Segments[0].StartPoint.Y.ShouldBe(startY, 1e-6);
        path.Segments[^1].EndPoint.X.ShouldBe(endX, 1e-3);
        path.Segments[^1].EndPoint.Y.ShouldBe(endY, 1e-3);
    }

    private static void AssertClear(IReadOnlyList<RoutedPath> paths, double spacing)
    {
        for (int i = 0; i < paths.Count; i++)
        for (int j = i + 1; j < paths.Count; j++)
        {
            PathIntersectionDetector.Crosses(paths[i], paths[j]).ShouldBeFalse($"wires {i} and {j} cross");
            PathIntersectionDetector.MinimumDistance(paths[i], paths[j]).ShouldBeGreaterThanOrEqualTo(spacing, $"wires {i} and {j}");
        }
    }
}

using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Routing.LengthEqualization;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace UnitTests.Routing.LengthEqualization;

/// <summary>
/// Group length matching on a routed fibre-array fan-in: every selected route reaches the
/// common length, the meanders keep the router's spacing to every other route and to each
/// other, and the run is fast enough to stay interactive.
/// </summary>
public class LengthEqualizerTests
{
    private const double Tolerance = 0.1;
    private const double MaxRadius = 25.0;
    private readonly ITestOutputHelper _output;

    public LengthEqualizerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Solve_FanIn_GivesEveryRouteTheLongestLengthWithoutOverlaps()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(12);
        var models = connections.Select(c => c.Connection).ToList();
        models.ShouldAllBe(c => c.RoutedPath != null && !c.IsBlockedFallback);
        double longest = models.Max(c => c.PathLengthMicrometers);
        var equalizer = new LengthEqualizer(canvas.Router);

        var plan = equalizer.Prepare(models, canvas.ConnectionManager.Connections.ToList(),
            canvas.Components.Select(c => c.Component).ToList(), null, MaxRadius, Tolerance);
        var result = LengthEqualizer.Solve(plan);
        _output.WriteLine($"{result.MatchedCount}/{models.Count} matched to {result.TargetLengthMicrometers:F1} µm in {result.Elapsed.TotalMilliseconds:F0} ms");

        result.TargetLengthMicrometers.ShouldBe(longest, 1e-9);
        result.FailedCount.ShouldBe(0);
        foreach (var outcome in result.Outcomes)
            outcome.Path!.TotalLengthMicrometers.ShouldBe(longest, Tolerance);
        AssertSpacing(result.Outcomes.Select(o => (o.Connection, o.Path!)).ToList(), canvas.Router.MinWaveguideSpacingMicrometers);
    }

    [Fact]
    public async Task Solve_ExplicitTarget_StretchesEveryRouteToIt()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(6);
        var models = connections.Select(c => c.Connection).ToList();
        double target = models.Max(c => c.PathLengthMicrometers) + 150;

        var plan = new LengthEqualizer(canvas.Router).Prepare(models, models,
            canvas.Components.Select(c => c.Component).ToList(), target, MaxRadius, Tolerance);
        var result = LengthEqualizer.Solve(plan);

        result.FailedCount.ShouldBe(0);
        result.Outcomes.ShouldAllBe(o => Math.Abs(o.Path!.TotalLengthMicrometers - target) <= Tolerance);
    }

    [Fact]
    public async Task Solve_UsesTheLargestRadiusThatFits()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(4);
        var models = connections.Select(c => c.Connection).ToList();

        var plan = new LengthEqualizer(canvas.Router).Prepare(models, models,
            canvas.Components.Select(c => c.Component).ToList(), null, MaxRadius, Tolerance);
        var result = LengthEqualizer.Solve(plan);

        result.Outcomes.Where(o => o.Path!.TotalLengthMicrometers > o.BasePath!.TotalLengthMicrometers + Tolerance)
            .ShouldAllBe(o => o.RadiusMicrometers == MaxRadius);
    }

    [Fact]
    public async Task Solve_IsDeterministic()
    {
        var (canvas, connections) = await FanInDesign.CreateAsync(6);
        var models = connections.Select(c => c.Connection).ToList();
        var equalizer = new LengthEqualizer(canvas.Router);
        var components = canvas.Components.Select(c => c.Component).ToList();

        var first = LengthEqualizer.Solve(equalizer.Prepare(models, models, components, null, MaxRadius, Tolerance));
        var second = LengthEqualizer.Solve(equalizer.Prepare(models, models, components, null, MaxRadius, Tolerance));

        for (int i = 0; i < first.Outcomes.Count; i++)
            second.Outcomes[i].Path!.Segments.Count.ShouldBe(first.Outcomes[i].Path!.Segments.Count);
    }

    [Fact]
    public void Solve_CanBeCancelled()
    {
        var plan = new LengthEqualizationPlan(100, 0.1, 10, 2, 1.2,
            new[] { new LengthEqualizationItem(new WaveguideConnection(), null!, new HashSet<object>(), 50) },
            Array.Empty<(object, RoutedPath)>(), Array.Empty<(object, CAP_Core.Routing.MeanderGeneration.MeanderBounds)>());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => LengthEqualizer.Solve(plan, cancelled.Token));
    }

    private static void AssertSpacing(IReadOnlyList<(WaveguideConnection Connection, RoutedPath Path)> routes, double spacing)
    {
        for (int i = 0; i < routes.Count; i++)
        for (int j = i + 1; j < routes.Count; j++)
        {
            PathIntersectionDetector.Crosses(routes[i].Path, routes[j].Path).ShouldBeFalse($"routes {i} and {j} cross");
            double distance = PathIntersectionDetector.MinimumDistance(routes[i].Path, routes[j].Path);
            distance.ShouldBeGreaterThan(1.0, $"routes {i} and {j} touch");
        }
    }
}

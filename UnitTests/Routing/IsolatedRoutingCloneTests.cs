using System.Globalization;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.AStarPathfinder;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// The isolated routing clones the parallel ordering cascade routes on must present the same
/// non-waveguide obstacle state as the source grid — including cells a caller blocked by hand
/// (fenced foreign wires, exit lanes), which a rebuild from components alone loses.
/// </summary>
public class IsolatedRoutingCloneTests
{
    private const double BendRadius = 10.0;
    private const byte UncrossableCellState = 3;

    [Fact]
    public void Clone_CarriesHandBlockedCells_ButNotWaveguideCells()
    {
        var block = CreateTestComponent(100, 100, width: 40, height: 40);
        var router = new WaveguideRouter { MinBendRadiusMicrometers = BendRadius };
        router.InitializePathfindingGrid(0, 0, 400, 300, new[] { block });
        var grid = router.PathfindingGrid!;
        var fenced = FenceRectangle(grid, 200, 20, 220, 280);
        var wire = new RoutedPath();
        wire.Segments.Add(new StraightSegment(20, 250, 180, 250, 0));
        grid.AddWaveguideObstacle(Guid.NewGuid(), wire.Segments, 2.0);
        var (wireX, wireY) = grid.PhysicalToGrid(100, 250);
        grid.GetCellState(wireX, wireY).ShouldBe((byte)2, "fixture sanity: the wire is registered");

        var clone = router.CreateIsolatedRoutingClone().PathfindingGrid!;

        foreach (var (x, y) in fenced)
            clone.GetCellState(x, y).ShouldBe(UncrossableCellState, "a hand-fenced cell must stay fenced on the clone");
        clone.GetCellState(wireX, wireY).ShouldBe((byte)0, "waveguides are not part of a clone's start state");
        var (blockX, blockY) = grid.PhysicalToGrid(120, 120);
        clone.GetCellState(blockX, blockY).ShouldBe(grid.GetCellState(blockX, blockY), "component cells are rebuilt identically");
    }

    [Fact]
    public void ParallelCascade_RespectsHandFence_LikeTheSequentialCascade()
    {
        var parallel = CreateFencedPairFixture(useParallelCascade: true);
        var sequential = CreateFencedPairFixture(useParallelCascade: false);

        parallel.Manager.LastOrderingAttemptCount.ShouldBeGreaterThanOrEqualTo(2,
            "fixture sanity: the cascade must run past its first attempt");
        foreach (var (p, s) in parallel.Wires.Zip(sequential.Wires))
        {
            p.IsBlockedFallback.ShouldBe(s.IsBlockedFallback);
            Signature(p).ShouldBe(Signature(s), "the parallel cascade must route exactly like the sequential one");
        }
        foreach (var wire in parallel.Wires.Where(w => !w.IsBlockedFallback))
            CrossesCells(parallel.Grid, wire, parallel.Fenced).ShouldBeFalse("no route may run through a fenced cell");
    }

    private sealed record FencedPairFixture(
        WaveguideConnectionManager Manager,
        PathfindingGrid Grid,
        List<WaveguideConnection> Wires,
        HashSet<(int X, int Y)> Fenced);

    /// <summary>
    /// The contention pair of the ordering-cascade tests (B routes only when ordered before A,
    /// A then detours around the roof) with the area north of the roof fenced by hand, so
    /// the detour a clone without the fence would take is closed.
    /// </summary>
    private static FencedPairFixture CreateFencedPairFixture(bool useParallelCascade)
    {
        var aWest = CreateTestComponent(-50, 140, width: 50, height: 40);
        var aEast = CreateTestComponent(460, 140, width: 30, height: 40);
        var bEast = CreateTestComponent(450, 170, width: 30, height: 40);
        var roof = CreateTestComponent(60, 105, width: 280, height: 45);
        var floor = CreateTestComponent(60, 170, width: 280, height: 45);
        var router = new WaveguideRouter { MinBendRadiusMicrometers = BendRadius, MinWaveguideSpacingMicrometers = 2.0 };
        router.InitializePathfindingGrid(-100, -100, 600, 400, new[] { aWest, aEast, bEast, roof, floor });
        var grid = router.PathfindingGrid!;
        var fenced = FenceRectangle(grid, 150, -100, 250, 100);
        var manager = new WaveguideConnectionManager(router)
        {
            UseSequentialRouting = true,
            UseParallelOrderingCascade = useParallelCascade,
        };
        var connA = manager.AddConnection(CreatePin(aWest, 50, 20, 0), CreatePin(aEast, 0, 20, 180));
        var connB = manager.AddConnection(CreatePin(bEast, 0, 20, 180), CreatePin(roof, 140, 45, 90));
        return new FencedPairFixture(manager, grid, new List<WaveguideConnection> { connA, connB }, fenced);
    }

    private static HashSet<(int X, int Y)> FenceRectangle(PathfindingGrid grid, double x1, double y1, double x2, double y2)
    {
        var (gx1, gy1) = grid.PhysicalToGrid(x1, y1);
        var (gx2, gy2) = grid.PhysicalToGrid(x2, y2);
        var cells = new HashSet<(int X, int Y)>();
        for (int x = gx1; x <= gx2; x++)
        for (int y = gy1; y <= gy2; y++)
        {
            if (!grid.IsInBounds(x, y)) continue;
            grid.SetCellState(x, y, UncrossableCellState);
            cells.Add((x, y));
        }
        return cells;
    }

    private static bool CrossesCells(PathfindingGrid grid, WaveguideConnection wire, HashSet<(int X, int Y)> cells)
    {
        const double Step = 0.5;
        foreach (var segment in wire.RoutedPath!.Segments)
        {
            var points = segment is BendSegment bend
                ? ArcSampling.SamplePoints(bend, Step)
                : SampleLine(segment.StartPoint, segment.EndPoint, Step);
            if (points.Any(p => cells.Contains(grid.PhysicalToGrid(p.X, p.Y))))
                return true;
        }
        return false;
    }

    private static IEnumerable<(double X, double Y)> SampleLine((double X, double Y) a, (double X, double Y) b, double step)
    {
        double length = Math.Max(Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)), 1e-9);
        for (double t = 0; t <= length; t += step)
            yield return (a.X + (b.X - a.X) * t / length, a.Y + (b.Y - a.Y) * t / length);
        yield return b;
    }

    private static string Signature(WaveguideConnection connection)
    {
        static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        return string.Join("|", connection.RoutedPath!.Segments.Select(segment =>
            $"{segment.GetType().Name}({F(segment.StartPoint.X)},{F(segment.StartPoint.Y)},{F(segment.EndPoint.X)},{F(segment.EndPoint.Y)})"));
    }

    private static PhysicalPin CreatePin(Component component, double offsetX, double offsetY, double angle) => new()
    {
        Name = angle < 90 || angle > 270 ? "output" : "input",
        OffsetXMicrometers = offsetX,
        OffsetYMicrometers = offsetY,
        AngleDegrees = angle,
        ParentComponent = component,
    };

    private static Component CreateTestComponent(double x, double y, double width, double height)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        return new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"Test_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0)
        {
            WidthMicrometers = width,
            HeightMicrometers = height,
            PhysicalX = x,
            PhysicalY = y,
        };
    }
}

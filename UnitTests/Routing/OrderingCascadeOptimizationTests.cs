using System.Globalization;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Tests for the ordering-cascade bounds (issue #1296): when no ordering is fully valid,
/// the cascade keeps the best attempt's routes by restoring a snapshot instead of
/// re-routing the best ordering a second time, and it stops early after
/// <see cref="WaveguideConnectionManager.MaxNonImprovingOrderingAttempts"/> consecutive
/// attempts that don't lower the best failed count. The restore must be identical to the
/// re-route it replaces: same failed set, same segments.
/// </summary>
public class OrderingCascadeOptimizationTests
{
    private const double BendRadius = 10.0;

    [Fact]
    public void BestAttemptRestore_YieldsSameFailedSetAndSegments_AsRerouteOfBestOrder()
    {
        var (manager, router, wires) = CreateSingleFileCorridorFixture();
        var failedBefore = FailedIds(wires);
        failedBefore.ShouldNotBeEmpty("the corridor fits only one diagonal — the cascade must leave failures");
        manager.LastOrderingAttemptCount.ShouldBeGreaterThanOrEqualTo(2,
            "the fixture must drive the cascade past its first attempt so the restore path runs");

        var restoredSignatures = wires.ToDictionary(w => w.Id, SegmentSignature);

        // Re-route the kept best order exactly like the pre-#1296 cascade did: clear the
        // waveguide obstacles, re-route every connection in order, register obstacles and
        // count blocked fallbacks and sibling crossings as failures.
        var grid = router.PathfindingGrid!;
        grid.ClearAllWaveguideObstacles();
        var rerouteFailed = new List<Guid>();
        var routedSoFar = new List<WaveguideConnection>();
        foreach (var connection in manager.Connections.ToList())
        {
            connection.RecalculateTransmission(router);
            if (connection.IsPathValid && connection.RoutedPath != null)
            {
                grid.AddWaveguideObstacle(connection.Id, connection.RoutedPath.Segments, manager.ObstacleWidthFor(connection));
                if (connection.IsBlockedFallback || routedSoFar.Any(other =>
                        other.RoutedPath != null && PathIntersectionDetector.Crosses(connection.RoutedPath, other.RoutedPath)))
                    rerouteFailed.Add(connection.Id);
                routedSoFar.Add(connection);
            }
            else
            {
                rerouteFailed.Add(connection.Id);
            }
        }

        rerouteFailed.OrderBy(id => id).ShouldBe(failedBefore.OrderBy(id => id),
            "the restored best attempt must fail exactly the wires a re-route of the best order fails");
        foreach (var wire in wires)
        {
            SegmentSignature(wire).ShouldBe(restoredSignatures[wire.Id],
                "the restored path must be segment-identical to the re-routed best order");
        }
    }

    [Fact]
    public void NonImprovingAttempts_StopCascadeEarly_AndTimingsReportTheStop()
    {
        var (manager, _, _) = CreateSingleFileCorridorFixture();

        manager.LastOrderingAttemptCount.ShouldBe(3,
            "the corridor never improves: the initial attempt plus two non-improving ones, then the stop");
        manager.LastOrderingEarlyStopped.ShouldBeTrue();
        var timings = manager.LastRoutingPassTimings;
        timings.ShouldNotBeNull();
        timings.OrderingAttempts.ShouldBe(3);
        timings.OrderingEarlyStopped.ShouldBeTrue("the census must show the cascade stopped early");
    }

    [Fact]
    public void EarlyStopDisabled_RunsEveryOrdering_WithIdenticalResult()
    {
        var (stoppedManager, _, stoppedWires) = CreateSingleFileCorridorFixture();
        var (fullManager, _, fullWires) = CreateSingleFileCorridorFixture();
        fullManager.MaxNonImprovingOrderingAttempts = 100;
        fullManager.RecalculateAllTransmissions();

        fullManager.LastOrderingEarlyStopped.ShouldBeFalse();
        fullManager.LastOrderingAttemptCount.ShouldBeGreaterThan(stoppedManager.LastOrderingAttemptCount,
            "without the early stop every generated ordering runs");

        fullWires.Select(w => w.IsBlockedFallback).ShouldBe(stoppedWires.Select(w => w.IsBlockedFallback),
            "the early stop must not change which wires end blocked");
        foreach (var (stopped, full) in stoppedWires.Zip(fullWires))
        {
            SegmentSignature(full).ShouldBe(SegmentSignature(stopped),
                "the early stop must not change the kept routes");
        }
    }

    [Fact]
    public void ImprovingAttempt_ResetsTheNonImprovingCounter()
    {
        // A resolvable contention pair (B routes only when ordered before A, then A detours
        // north around the roof) plus the single-file corridor trio, whose wires stay
        // blocked under every ordering. The initial attempt fails 4 wires (B + trio); the
        // first retry (reverse order) routes B before A and improves to 3, which resets
        // the non-improving counter — so the cascade must NOT stop after the initial
        // attempt plus two retries, but one improvement plus two non-improving retries.
        var aWest = CreateTestComponent(-50, 140, width: 50, height: 40);
        var aEast = CreateTestComponent(460, 140, width: 30, height: 40);
        var bEast = CreateTestComponent(450, 170, width: 30, height: 40);
        var roof = CreateTestComponent(60, 105, width: 280, height: 45);
        var floor = CreateTestComponent(60, 170, width: 280, height: 45);
        var corridor = CreateCorridorComponents();

        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = BendRadius,
            MinWaveguideSpacingMicrometers = 2.0
        };
        router.InitializePathfindingGrid(-100, -100, 600, 400,
            new[] { aWest, aEast, bEast, roof, floor }.Concat(corridor.All).ToArray());
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var connA = manager.AddConnection(CreatePin(aWest, 50, 20, 0), CreatePin(aEast, 0, 20, 180));
        // B is added LAST so the final cascade's initial attempt routes A first and fails
        // B — the reverse retry then improves (B routes, A detours), resetting the counter.
        var corridorWires = AddCorridorWires(manager, corridor);
        var connB = manager.AddConnection(CreatePin(bEast, 0, 20, 180), CreatePin(roof, 140, 45, 90));

        connA.IsBlockedFallback.ShouldBeFalse("the pair resolves once B is ordered before A");
        connB.IsBlockedFallback.ShouldBeFalse();
        corridorWires.Count(w => w.IsBlockedFallback).ShouldBe(3,
            "the corridor wires stay blocked under every ordering — the failure count can never drop below 3");
        manager.LastOrderingAttemptCount.ShouldBe(4,
            "initial attempt (4 failures) + improving retry (3 failures, counter reset) "
            + "+ two non-improving retries, then the early stop");
        manager.LastOrderingEarlyStopped.ShouldBeTrue();
    }

    [Fact]
    public void ParallelCascade_KeepsSequentialResult_RoutesOrderAndCounters()
    {
        var (parallelManager, _, parallelWires) = CreateSingleFileCorridorFixture(useParallelCascade: true);
        var (sequentialManager, _, sequentialWires) = CreateSingleFileCorridorFixture(useParallelCascade: false);

        parallelManager.LastOrderingEarlyStopped.ShouldBeTrue("fixture sanity: the corridor never improves");
        sequentialManager.LastOrderingAttemptCount.ShouldBe(parallelManager.LastOrderingAttemptCount,
            "the replay reports exactly the attempts the sequential cascade would have run");
        sequentialManager.LastOrderingEarlyStopped.ShouldBe(parallelManager.LastOrderingEarlyStopped);
        parallelWires.Select(w => parallelManager.Connections.IndexOf(w))
            .ShouldBe(sequentialWires.Select(w => sequentialManager.Connections.IndexOf(w)),
                "the kept ordering must be the same permutation");
        sequentialWires.Select(w => w.IsBlockedFallback).ShouldBe(parallelWires.Select(w => w.IsBlockedFallback));
        foreach (var (parallel, sequential) in parallelWires.Zip(sequentialWires))
        {
            SegmentSignature(sequential).ShouldBe(SegmentSignature(parallel),
                "the parallel cascade must keep the exact routes the sequential cascade keeps");
        }
    }

    [Fact]
    public void ParallelCascade_ImprovingAttempt_MatchesSequentialResult()
    {
        var parallel = CreatePairAndCorridorFixture(useParallelCascade: true);
        var sequential = CreatePairAndCorridorFixture(useParallelCascade: false);

        parallel.Manager.LastOrderingAttemptCount.ShouldBe(4,
            "sanity: initial attempt + improving retry + two non-improving retries, then the early stop");
        sequential.Manager.LastOrderingAttemptCount.ShouldBe(parallel.Manager.LastOrderingAttemptCount);
        sequential.Manager.LastOrderingEarlyStopped.ShouldBe(parallel.Manager.LastOrderingEarlyStopped);
        sequential.ConnA.IsBlockedFallback.ShouldBe(parallel.ConnA.IsBlockedFallback);
        sequential.ConnB.IsBlockedFallback.ShouldBe(parallel.ConnB.IsBlockedFallback);
        SegmentSignature(sequential.ConnA).ShouldBe(SegmentSignature(parallel.ConnA));
        SegmentSignature(sequential.ConnB).ShouldBe(SegmentSignature(parallel.ConnB));
        foreach (var (p, s) in parallel.CorridorWires.Zip(sequential.CorridorWires))
        {
            s.IsBlockedFallback.ShouldBe(p.IsBlockedFallback);
            SegmentSignature(s).ShouldBe(SegmentSignature(p));
        }
    }

    [Fact]
    public void ParallelCascade_FullyResolvedByRetry_KeepsTheSequentialWinningOrdering()
    {
        var parallel = CreateResolvablePairFixture(useParallelCascade: true);
        var sequential = CreateResolvablePairFixture(useParallelCascade: false);

        parallel.ConnA.IsBlockedFallback.ShouldBeFalse("the pair resolves once B is ordered before A");
        parallel.ConnB.IsBlockedFallback.ShouldBeFalse();
        parallel.Manager.LastOrderingAttemptCount.ShouldBe(2,
            "the first retry ordering routes both wires — the cascade returns at the first clean attempt");
        sequential.Manager.LastOrderingAttemptCount.ShouldBe(2);
        new[]
        {
            parallel.Manager.Connections.IndexOf(parallel.ConnA),
            parallel.Manager.Connections.IndexOf(parallel.ConnB),
        }.ShouldBe(new[]
        {
            sequential.Manager.Connections.IndexOf(sequential.ConnA),
            sequential.Manager.Connections.IndexOf(sequential.ConnB),
        }, "the winning ordering must be the same permutation");
        SegmentSignature(sequential.ConnA).ShouldBe(SegmentSignature(parallel.ConnA));
        SegmentSignature(sequential.ConnB).ShouldBe(SegmentSignature(parallel.ConnB));
    }

    private sealed record PairFixture(
        WaveguideConnectionManager Manager,
        WaveguideConnection ConnA,
        WaveguideConnection ConnB,
        List<WaveguideConnection> CorridorWires);

    /// <summary>
    /// The improving-attempt fixture of <see cref="ImprovingAttempt_ResetsTheNonImprovingCounter"/>
    /// (a resolvable contention pair plus the never-improving corridor trio), parameterized
    /// on the cascade path so the parallel and sequential cascades can be pinned against each
    /// other.
    /// </summary>
    private static PairFixture CreatePairAndCorridorFixture(bool useParallelCascade)
    {
        var (aWest, aEast, bEast, roof, floor) = CreateContentionPairComponents();
        var corridor = CreateCorridorComponents();

        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = BendRadius,
            MinWaveguideSpacingMicrometers = 2.0
        };
        router.InitializePathfindingGrid(-100, -100, 600, 400,
            new[] { aWest, aEast, bEast, roof, floor }.Concat(corridor.All).ToArray());
        var manager = new WaveguideConnectionManager(router)
        {
            UseSequentialRouting = true,
            UseParallelOrderingCascade = useParallelCascade,
        };

        var connA = manager.AddConnection(CreatePin(aWest, 50, 20, 0), CreatePin(aEast, 0, 20, 180));
        // B is added LAST so the final cascade's initial attempt routes A first and fails
        // B — the reverse retry then improves (B routes, A detours), resetting the counter.
        var corridorWires = AddCorridorWires(manager, corridor);
        var connB = manager.AddConnection(CreatePin(bEast, 0, 20, 180), CreatePin(roof, 140, 45, 90));
        return new PairFixture(manager, connA, connB, corridorWires);
    }

    /// <summary>
    /// The contention pair alone: the initial ordering routes A first and blocks B, the
    /// reverse ordering routes both — a cascade that succeeds on its first retry.
    /// </summary>
    private static PairFixture CreateResolvablePairFixture(bool useParallelCascade)
    {
        var (aWest, aEast, bEast, roof, floor) = CreateContentionPairComponents();

        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = BendRadius,
            MinWaveguideSpacingMicrometers = 2.0
        };
        router.InitializePathfindingGrid(-100, -100, 600, 400,
            new[] { aWest, aEast, bEast, roof, floor });
        var manager = new WaveguideConnectionManager(router)
        {
            UseSequentialRouting = true,
            UseParallelOrderingCascade = useParallelCascade,
        };

        var connA = manager.AddConnection(CreatePin(aWest, 50, 20, 0), CreatePin(aEast, 0, 20, 180));
        var connB = manager.AddConnection(CreatePin(bEast, 0, 20, 180), CreatePin(roof, 140, 45, 90));
        return new PairFixture(manager, connB, connA, new List<WaveguideConnection>());
    }

    /// <summary>
    /// The pair geometry: A crosses the 20 µm channel between roof and floor at y 160, and
    /// B must dive through the same channel into the roof's downward pin — B routes only
    /// when ordered before A (A then detours north around the roof).
    /// </summary>
    private static (Component AWest, Component AEast, Component BEast, Component Roof, Component Floor)
        CreateContentionPairComponents() =>
        (CreateTestComponent(-50, 140, width: 50, height: 40),
         CreateTestComponent(460, 140, width: 30, height: 40),
         CreateTestComponent(450, 170, width: 30, height: 40),
         CreateTestComponent(60, 105, width: 280, height: 45),
         CreateTestComponent(60, 170, width: 280, height: 45));

    /// <summary>
    /// Three wires in a 20 µm corridor sealed by full-width walls: one straight, two
    /// crossing diagonals. The corridor is so tight that every wire stays blocked under
    /// every ordering (all failures are contention — no pin is sealed by a footprint), so
    /// the failure count can never improve and no attempt is fully valid.
    /// </summary>
    private static (WaveguideConnectionManager Manager, WaveguideRouter Router, List<WaveguideConnection> Wires)
        CreateSingleFileCorridorFixture(bool useParallelCascade = true)
    {
        var corridor = CreateCorridorComponents();
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = BendRadius,
            MinWaveguideSpacingMicrometers = 2.0
        };
        router.InitializePathfindingGrid(60, 245, 340, 355, corridor.All);
        var manager = new WaveguideConnectionManager(router)
        {
            UseSequentialRouting = true,
            UseParallelOrderingCascade = useParallelCascade,
        };
        var wires = AddCorridorWires(manager, corridor);
        return (manager, router, wires);
    }

    private static List<WaveguideConnection> AddCorridorWires(
        WaveguideConnectionManager manager, CorridorComponents c)
    {
        return new List<WaveguideConnection>
        {
            manager.AddConnection(CreatePin(c.WestA, 8, 1.5, 0), CreatePin(c.EastA, 0, 1.5, 180)),
            manager.AddConnection(CreatePin(c.WestB, 8, 1.5, 0), CreatePin(c.EastB, 0, 1.5, 180)),
            manager.AddConnection(CreatePin(c.WestC, 8, 1.5, 0), CreatePin(c.EastC, 0, 1.5, 180)),
        };
    }

    /// <summary>
    /// Corridor y 286..306 between full-width walls, with three pin pairs: straight at
    /// y 290, diagonal down y 294 → 302, diagonal up y 302 → 294. The two diagonals cross
    /// each other, so the blocked one's fallback always crosses a routed sibling. The
    /// down-diagonal ends further west, so all three lengths differ and the length-sorted
    /// orderings survive de-duplication (the early-stop tests need several orderings).
    /// </summary>
    private static CorridorComponents CreateCorridorComponents()
    {
        var c = new CorridorComponents(
            Roof: CreateTestComponent(60, 245, width: 280, height: 41),
            Floor: CreateTestComponent(60, 306, width: 280, height: 49),
            WestA: CreateTestComponent(62, 288.5, width: 8, height: 3),
            EastA: CreateTestComponent(324, 288.5, width: 8, height: 3),
            WestB: CreateTestComponent(62, 292.5, width: 8, height: 3),
            EastB: CreateTestComponent(292, 300.5, width: 8, height: 3),
            WestC: CreateTestComponent(62, 300.5, width: 8, height: 3),
            EastC: CreateTestComponent(324, 292.5, width: 8, height: 3));
        return c;
    }

    private sealed record CorridorComponents(
        Component Roof, Component Floor,
        Component WestA, Component EastA,
        Component WestB, Component EastB,
        Component WestC, Component EastC)
    {
        public Component[] All => new[] { Roof, Floor, WestA, EastA, WestB, EastB, WestC, EastC };
    }

    private static List<Guid> FailedIds(List<WaveguideConnection> wires) =>
        wires.Where(w => w.IsBlockedFallback).Select(w => w.Id).ToList();

    private static string SegmentSignature(WaveguideConnection connection)
    {
        static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        return string.Join("|", connection.RoutedPath!.Segments.Select(segment => segment switch
        {
            StraightSegment s => $"S({F(s.StartPoint.X)},{F(s.StartPoint.Y)},{F(s.EndPoint.X)},{F(s.EndPoint.Y)},{F(s.StartAngleDegrees)})",
            BendSegment b => $"B({F(b.Center.X)},{F(b.Center.Y)},{F(b.RadiusMicrometers)},{F(b.StartAngleDegrees)},{F(b.SweepAngleDegrees)})",
            _ => segment.GetType().Name
        }));
    }

    private static PhysicalPin CreatePin(Component comp, double offsetX, double offsetY, double angle)
    {
        return new PhysicalPin
        {
            Name = angle < 90 || angle > 270 ? "output" : "input",
            OffsetXMicrometers = offsetX,
            OffsetYMicrometers = offsetY,
            AngleDegrees = angle,
            ParentComponent = comp
        };
    }

    private static Component CreateTestComponent(double x, double y, double width = 50, double height = 50)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"Test_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0
        );

        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        component.PhysicalX = x;
        component.PhysicalY = y;

        return component;
    }
}

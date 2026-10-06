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
/// Inverted repro for issue #1412 (fix verified per issue #1418): the RAM word cell (issue
/// #1400 floorplan) used to stamp the cell-spanning select trunk CSEL0R.Y2 → CSEL0_0.A
/// blocked — a degraded straight crossing five routed siblings — although the word cell's
/// row 0 is an empty highway the trunk could detour through. This fixture rebuilds that
/// trunk's geometry with plain box components at the coordinates <c>RamWordCellBuilder</c>
/// emits (column pitch 1200, row pitch 400, origin (400,100), gate bodies 320×60), routes
/// it through the real <see cref="WaveguideConnectionManager"/> pipeline, and pins the
/// fixed behaviour: the trunk routes WITHOUT <see cref="RoutedPath.IsBlockedFallback"/>
/// and crosses no routed sibling, while a hand-constructed detour through the empty
/// highway row remains collision-free on the routing grid.
/// <para>
/// The A* node budgets are scaled down from the defaults so the test meets its time
/// budget. The failure mode this fixture pinned was budget-independent: the flat search
/// flooded the huge free region between the gate rows and exhausted its node budget
/// before it ever reached the empty highway row. The fix (issue #1418) retries the A* on
/// a coarser grid before degrading to the blocked fallback — factor² fewer cells let the
/// same budget reach the highway, and the coarse result is only accepted after smoothing
/// and a collision check on the fine grid.
/// </para>
/// </summary>
public class ContentionRepairDetourReproTests
{
    private const double BendRadius = 10.0;

    /// <summary>Gate body size matching the RAM gate templates (pin offsets reach +320/+32).</summary>
    private const double GateWidth = 320;
    private const double GateHeight = 60;

    /// <summary>Generous enough that a slow CI runner never cuts the repair short.</summary>
    private static readonly TimeSpan UnboundedRepairBudget = TimeSpan.FromMinutes(5);

    [Fact]
    public void SelectTrunk_FreeHighwayDetourExists_RouterFindsDetour()
    {
        var scene = SelectTrunkScene.Build();

        scene.Manager.RecalculateAllTransmissions();

        // Pin the fixed behavior (issue #1418): the trunk detours instead of degrading to
        // the blocked straight — the coarse-grid retry reaches the empty highway row the
        // flooded fine search never did.
        scene.Trunk.IsBlockedFallback.ShouldBeFalse(
            "the trunk must detour through the free highway lane, not degrade to a blocked straight " +
            "(issue #1412, fixed via #1418)");
        scene.Trunk.FailureReason.ShouldBe(RoutingFailureReason.None);
        scene.Trunk.RoutedPath.ShouldNotBeNull();
        foreach (var sibling in scene.Siblings)
        {
            sibling.IsBlockedFallback.ShouldBeFalse(
                "the short sibling hops route cleanly — only the cell-spanning trunk was at risk");
        }

        // The routed trunk must not cross any routed sibling — the same crossing check
        // the detection half of this fixture applied to the hand-built detour.
        foreach (var sibling in scene.Siblings)
        {
            if (sibling.RoutedPath == null || !sibling.IsPathValid)
                continue;
            PathIntersectionDetector.Crosses(scene.Trunk.RoutedPath, sibling.RoutedPath).ShouldBeFalse(
                "the routed trunk must not cross any routed sibling");
        }

        // The hand-constructed detour through the empty highway row: east out of the start
        // pin, north into row 0, across the cell on the highway, south onto the target pin.
        // The trunk's own registered obstacle is lifted first — the detour must clear
        // every OTHER obstacle only. This keeps the detour's existence pinned so the test
        // above can never silently pass on an empty scene.
        var detour = SelectTrunkScene.BuildHighwayDetour(scene);
        var grid = scene.Router.PathfindingGrid!;
        grid.RemoveWaveguideObstacle(scene.Trunk.Id);
        try
        {
            AssertDetourFreeOnGrid(detour, grid, scene.TrunkStart, scene.TrunkEnd);
        }
        finally
        {
            grid.AddWaveguideObstacle(
                scene.Trunk.Id, scene.Trunk.RoutedPath!.Segments, scene.Manager.ObstacleWidthFor(scene.Trunk));
        }
        foreach (var sibling in scene.Siblings)
        {
            if (sibling.RoutedPath == null || !sibling.IsPathValid)
                continue;
            PathIntersectionDetector.Crosses(detour, sibling.RoutedPath).ShouldBeFalse(
                "the highway detour must not cross any routed sibling");
        }
    }

    /// <summary>
    /// Samples the hand-built detour on the routing grid the way
    /// <see cref="WaveguideRouter.IsPathBlocked(IEnumerable{PathSegment})"/>
    /// does (half-cell steps, one-cell margins at the segment ends) and requires every
    /// sample to be free — waveguide-blocked cells (state 2) always count, component cells
    /// (state 1) count unless they lie inside the detour's own pin-escape corridors, which
    /// the router legitimately punches through its own gate's padding band.
    /// </summary>
    private static void AssertDetourFreeOnGrid(
        RoutedPath detour, PathfindingGrid grid, PhysicalPin startPin, PhysicalPin endPin)
    {
        var corridors = grid.GetPinCorridorCells(new[] { startPin, endPin });
        double step = grid.CellSizeMicrometers * 0.5;
        double margin = grid.CellSizeMicrometers;
        foreach (var segment in detour.Segments)
        {
            double dx = segment.EndPoint.X - segment.StartPoint.X;
            double dy = segment.EndPoint.Y - segment.StartPoint.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.001)
                continue;
            dx /= length;
            dy /= length;
            for (double t = margin; t < length - margin; t += step)
            {
                var (gx, gy) = grid.PhysicalToGrid(
                    segment.StartPoint.X + dx * t, segment.StartPoint.Y + dy * t);
                byte state = grid.GetCellState(gx, gy);
                bool free = state == 0 || (state == 1 && corridors.Contains((gx, gy)));
                free.ShouldBeTrue(
                    $"detour leg ({segment.StartPoint.X:F0},{segment.StartPoint.Y:F0})->" +
                    $"({segment.EndPoint.X:F0},{segment.EndPoint.Y:F0}) hits grid state {state} " +
                    $"at cell ({gx},{gy}) — the highway lane must be collision-free");
            }
        }
    }

    /// <summary>
    /// The trunk scene at real word-cell coordinates: the select trunk
    /// (CSEL0R.Y2@(720,526) ang 0 → CSEL0_0.A@(13600,1724) ang 180) plus the five sibling
    /// wires its blocked straight crosses in the routed word cell, and the gate bodies
    /// lining the corridor. Wire order mirrors <c>RamWordCellBuilder</c>: the short hops
    /// first, the cell-spanning trunk last.
    /// </summary>
    internal sealed class SelectTrunkScene
    {
        public required WaveguideRouter Router { get; init; }
        public required WaveguideConnectionManager Manager { get; init; }
        public required WaveguideConnection Trunk { get; init; }
        public required List<WaveguideConnection> Siblings { get; init; }
        public required PhysicalPin TrunkStart { get; init; }
        public required PhysicalPin TrunkEnd { get; init; }

        public static SelectTrunkScene Build() => Build(sealTrunkEnd: false);

        /// <summary>
        /// The trunk scene with the trunk's END pin buried inside its gate body
        /// (issue #1426): the pin corridor punches only 3·radius into a body, so a
        /// pin 160 µm deep has no physical route — every detour (highway or inter-row)
        /// fails and the wire ends blocked. The perf-guard measurement uses this
        /// variant to time the retry's wasted cost on a genuinely unroutable wire.
        /// </summary>
        public static SelectTrunkScene BuildSealedTrunk() => Build(sealTrunkEnd: true);

        private static SelectTrunkScene Build(bool sealTrunkEnd)
        {
            // Gates at RamWordCellBuilder coordinates, each with the pins the wires need
            // (registered on the component so the rasterizer carves their escape corridors).
            var csel0R = Gate(400, 500, (320, 26, 0));
            var csel0_0 = sealTrunkEnd
                ? Gate(13600, 1700, (160, 24, 180))
                : Gate(13600, 1700, (0, 24, 180));
            var en0 = Gate(1600, 500);
            var cpea0 = Gate(4000, 500);
            var iw0 = Gate(5200, 500);
            var cpeb0_0 = Gate(5200, 900, (320, 26, 0));
            var cpi0_0 = Gate(7600, 500, (320, 22, 0), (320, 26, 0));
            var cpeb0_1 = Gate(8800, 900);
            var cpi0_1 = Gate(8800, 1300, (0, 24, 180), (320, 22, 0));
            var cpeb0_2 = Gate(8800, 2500, (0, 24, 180));
            var cpi0_2 = Gate(8800, 2900, (0, 24, 180));
            var le01 = Gate(10000, 1700, (320, 24, 0));
            var reg01 = Gate(11200, 1300, (0, 32, 180));
            var le00 = Gate(10000, 900, (0, 32, 180));
            var h03 = Gate(10000, 2900);

            var components = new Component[]
            {
                csel0R, csel0_0, en0, cpea0, iw0, cpeb0_0, cpi0_0, cpeb0_1,
                cpi0_1, cpeb0_2, cpi0_2, le01, reg01, le00, h03,
            };
            var router = new WaveguideRouter
            {
                MinBendRadiusMicrometers = BendRadius,
                MinWaveguideSpacingMicrometers = 2.0,
                // Scaled-down search budgets (see the class doc): the flood-and-exhaust
                // failure is budget-independent, these only keep the repro fast.
                Phase1MaxNodes = 15_000,
                Phase2MaxNodes = 30_000,
            };
            router.InitializePathfindingGrid(0, 0, 14420, 3800, components);
            var manager = new WaveguideConnectionManager(router)
            {
                UseSequentialRouting = true,
                ContentionRepairTimeBudget = UnboundedRepairBudget,
                // One ordering pass: the contention stamp under test forms identically
                // without the five extra full-cascade permutations.
                MaxRoutingAttempts = 1,
            };

            // Four of the five siblings the trunk's blocked straight crosses in the routed
            // cell (coordinates from the routed word cell, issue #1400 floorplan); the fifth
            // (a second cell-spanning wire that also fails) is left out so the trunk is the
            // only blocked wire and the repro stays inside its time budget. Deferred adds
            // keep the wire order without paying a full re-route per connection.
            var siblings = new List<WaveguideConnection>
            {
                manager.AddConnectionDeferred(cpi0_1.PhysicalPins[1], le00.PhysicalPins[0]),
                manager.AddConnectionDeferred(le01.PhysicalPins[0], reg01.PhysicalPins[0]),
                manager.AddConnectionDeferred(cpeb0_0.PhysicalPins[0], cpeb0_2.PhysicalPins[0]),
                manager.AddConnectionDeferred(cpi0_0.PhysicalPins[0], cpi0_1.PhysicalPins[0]),
            };
            var trunk = manager.AddConnectionDeferred(csel0R.PhysicalPins[0], csel0_0.PhysicalPins[0]);

            return new SelectTrunkScene
            {
                Router = router,
                Manager = manager,
                Trunk = trunk,
                Siblings = siblings,
                TrunkStart = csel0R.PhysicalPins[0],
                TrunkEnd = csel0_0.PhysicalPins[0],
            };
        }

        /// <summary>
        /// The detour the router declines: out of CSEL0R.Y2 heading east, north into the
        /// empty highway row 0 (y = 300, above every gate), across the cell, then south
        /// onto CSEL0_0.A — all waypoints clear of gates and wires.
        /// </summary>
        public static RoutedPath BuildHighwayDetour(SelectTrunkScene scene)
        {
            var (sx, sy) = scene.TrunkStart.GetAbsolutePosition();
            var (ex, ey) = scene.TrunkEnd.GetAbsolutePosition();
            const double highwayY = 300.0;
            const double exitX = 1000.0;
            const double entryX = 13400.0;
            var path = new RoutedPath();
            AddStraight(path, sx, sy, exitX, sy);
            AddStraight(path, exitX, sy, exitX, highwayY);
            AddStraight(path, exitX, highwayY, entryX, highwayY);
            AddStraight(path, entryX, highwayY, entryX, ey);
            AddStraight(path, entryX, ey, ex, ey);
            return path;
        }

        private static void AddStraight(RoutedPath path, double x1, double y1, double x2, double y2)
        {
            double heading = Math.Atan2(y2 - y1, x2 - x1) * 180.0 / Math.PI;
            path.Segments.Add(new StraightSegment(x1, y1, x2, y2, heading));
        }

        private static Component Gate(double x, double y, params (double X, double Y, double Angle)[] pins)
        {
            var physicalPins = pins.Select((p, i) => new PhysicalPin
            {
                Name = $"p{i}",
                OffsetXMicrometers = p.X,
                OffsetYMicrometers = p.Y,
                AngleDegrees = p.Angle,
            }).ToList();
            var parts = new Part[1, 1];
            parts[0, 0] = new Part(new List<Pin>());
            var component = new Component(
                laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
                sliders: new List<Slider>(),
                nazcaFunctionName: "test",
                nazcaFunctionParams: "",
                parts: parts,
                typeNumber: 0,
                identifier: $"Gate_{x}_{y}",
                rotationCounterClock: DiscreteRotation.R0,
                physicalPins: physicalPins);
            component.WidthMicrometers = GateWidth;
            component.HeightMicrometers = GateHeight;
            component.PhysicalX = x;
            component.PhysicalY = y;
            return component;
        }
    }
}

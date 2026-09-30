using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Tests for the reason-aware blocked-path messages (issue #1236): Design Checks must
/// say <em>why</em> a wire is blocked — a pin sealed in by a component footprint (move
/// the component) vs. no free lane (other wires occupy the corridor). Reuses the routing
/// fixtures of <c>EndpointBlockedRetryTests</c>.
/// </summary>
public class DesignValidatorBlockedReasonTests
{
    private const double BendRadius = 10.0;

    private readonly DesignValidator _validator = new();

    [Fact]
    public void EndpointBlockedWire_ReportsPinSealedByFootprint()
    {
        var source = CreateTestComponent(0, 200);
        var target = CreateTestComponent(300, 200);
        var blocker = CreateTestComponent(255, 205, width: 55, height: 40);

        var router = CreateRouter(-100, -100, 500, 500, source, target, blocker);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var blocked = manager.AddConnection(
            CreatePin(source, 50, 25, 0), CreatePin(target, 0, 25, 180));
        blocked.FailureReason.ShouldBe(RoutingFailureReason.EndpointBlocked);

        var issues = _validator.Validate(manager.Connections);

        var issue = issues.Where(i => i.Type == DesignIssueType.BlockedPath)
            .ShouldHaveSingleItem();
        issue.Description.ShouldContain("a pin is sealed in by a component footprint");
        issue.Description.ShouldContain("move the component");
    }

    [Fact]
    public void ContentionBlockedWire_ReportsNoFreeLane()
    {
        // The corridor fixture from EndpointBlockedRetryTests, but with a single routing
        // attempt so the contention failure of wire B is never retried away.
        var aWest = CreateTestComponent(-50, 140, width: 50, height: 40);
        var aEast = CreateTestComponent(460, 140, width: 30, height: 40);
        var bEast = CreateTestComponent(450, 170, width: 30, height: 40);
        var roof = CreateTestComponent(60, 105, width: 280, height: 45);
        var floor = CreateTestComponent(60, 170, width: 280, height: 45);

        var router = CreateRouter(-100, -100, 600, 300, aWest, aEast, bEast, roof, floor);
        var manager = new WaveguideConnectionManager(router)
        {
            UseSequentialRouting = true,
            MaxRoutingAttempts = 1
        };

        manager.AddConnection(CreatePin(aWest, 50, 20, 0), CreatePin(aEast, 0, 20, 180));
        var connB = manager.AddConnection(
            CreatePin(bEast, 0, 20, 180), CreatePin(roof, 140, 45, 90));
        connB.FailureReason.ShouldBe(RoutingFailureReason.Contention);

        var issues = _validator.Validate(manager.Connections);

        var issue = issues.Where(i => i.Type == DesignIssueType.BlockedPath)
            .ShouldHaveSingleItem();
        issue.Description.ShouldContain("no free lane");
        issue.Description.ShouldContain("other waveguides occupy the corridor");
    }

    [Fact]
    public void CleanDesign_ReportsNoBlockedPathIssue()
    {
        var source = CreateTestComponent(0, 200);
        var target = CreateTestComponent(300, 200);

        var router = CreateRouter(-100, -100, 500, 500, source, target);
        var manager = new WaveguideConnectionManager(router) { UseSequentialRouting = true };

        var clean = manager.AddConnection(
            CreatePin(source, 50, 25, 0), CreatePin(target, 0, 25, 180));
        clean.IsBlockedFallback.ShouldBeFalse();

        var issues = _validator.Validate(manager.Connections);

        issues.ShouldNotContain(i => i.Type == DesignIssueType.BlockedPath);
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

    private static WaveguideRouter CreateRouter(
        double minX, double minY, double maxX, double maxY, params Component[] components)
    {
        var router = new WaveguideRouter
        {
            MinBendRadiusMicrometers = BendRadius,
            MinWaveguideSpacingMicrometers = 2.0
        };
        router.InitializePathfindingGrid(minX, minY, maxX, maxY, components);
        return router;
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

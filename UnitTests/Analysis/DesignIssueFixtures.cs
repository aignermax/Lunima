using CAP_Core.Analysis;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using UnitTests.Helpers;

namespace UnitTests.Analysis;

/// <summary>
/// Builds, through the real validators and checkers, exactly one <see cref="DesignIssue"/>
/// of any requested <see cref="DesignIssueType"/> — the production fixture backing
/// <see cref="DesignIssueLocalizationTests"/>, so every issue type's localization
/// contract is exercised at its actual creation site.
/// </summary>
internal static class DesignIssueFixtures
{
    private const double CouplerWidth = 100;
    private const double CouplerHeight = 19;
    private const double PinY = 9.5;
    private const double WavelengthNm = 1550;

    /// <summary>Produces one issue of the requested type via the owning checker.</summary>
    public static DesignIssue Produce(DesignIssueType type) => type switch
    {
        DesignIssueType.InvalidGeometry => ProduceConnectionIssue(type, p => p.IsInvalidGeometry = true),
        DesignIssueType.BlockedPath => ProduceConnectionIssue(type, p => p.IsBlockedFallback = true),
        DesignIssueType.BendRadiusBelowProcessMinimum =>
            ProduceConnectionIssue(type, p => p.ViolatesProcessMinBendRadius = true),
        DesignIssueType.StyledRouteThroughComponent =>
            ProduceConnectionIssue(type, p => p.PassesThroughComponent = true),
        DesignIssueType.OutOfBounds => ProduceOutOfBoundsIssue(),
        DesignIssueType.PdkProcessMismatch => ProducePdkMismatchIssue(),
        DesignIssueType.OverlappingPaths => ProduceOverlapIssue(),
        DesignIssueType.UnconnectedPin => ProduceUnconnectedPinIssue(),
        DesignIssueType.PinMismatch => ProducePinMismatchIssue(),
        DesignIssueType.WaveguideSpacingViolation => ProduceSpacingIssue(),
        DesignIssueType.WaveguideBelowMinWidth => ProduceMinWidthIssue(),
        DesignIssueType.ComponentFootprintOverlap => ProduceFootprintOverlapIssue(),
        DesignIssueType.WaveguideCrossing => ProduceCrossingIssue(),
        DesignIssueType.ChipletInterfaceNotFacing => ProduceChipletIssue(type),
        DesignIssueType.ChipletInterfaceLateralOffset => ProduceChipletIssue(type),
        DesignIssueType.ChipletInterfaceOffEdge => ProduceChipletIssue(type),
        DesignIssueType.ChipletInterfaceGapLoss => ProduceChipletIssue(type),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unhandled issue type"),
    };

    /// <summary>Produces a blocked-path issue with the given router failure classification.</summary>
    public static DesignIssue ProduceBlockedPath(RoutingFailureReason reason) =>
        ProduceConnectionIssue(DesignIssueType.BlockedPath, p =>
        {
            p.IsBlockedFallback = true;
            p.FailureReason = reason;
        });

    private static DesignIssue ProduceConnectionIssue(DesignIssueType type, Action<RoutedPath> mark)
    {
        var connection = CreateConnection(0, 0, 100, 0);
        mark(connection.RoutedPath!);

        return new DesignValidator().Validate(new[] { connection }).Single(i => i.Type == type);
    }

    private static DesignIssue ProduceOutOfBoundsIssue()
    {
        var comp = CreateComponent("oob", x: -10, y: 0, width: 250, height: 250);

        return new DesignValidator()
            .ValidateComponentBounds(new[] { comp }, 5000, 5000)
            .Single();
    }

    private static DesignIssue ProducePdkMismatchIssue()
    {
        var comp = CreateComponent("locked", 0, 0, 100, 100);
        comp.HumanReadableName = "LockedComp";
        var sources = new Dictionary<Component, string?> { [comp] = "LockedLib" };

        return new DesignValidator()
            .ValidateComponentPdkCompatibility(
                new[] { comp }, sources, Array.Empty<string>(), Array.Empty<string>())
            .Single();
    }

    private static DesignIssue ProduceOverlapIssue()
    {
        // A conn×conn crossing is owned by ConnectionCrossingDetector, so the overlap
        // fixture crosses a connection with a frozen group path instead.
        var conn = CreateConnection(0, 50, 100, 50);
        var group = CreateGroupWithFrozenPath(50, 0, 50, 100);

        return new WaveguideOverlapDetector()
            .DetectOverlaps(new[] { conn }, new[] { group })
            .Single();
    }

    private static ComponentGroup CreateGroupWithFrozenPath(
        double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        var group = new ComponentGroup("TestGroup");
        group.InternalPaths.Add(new FrozenWaveguidePath { Path = path });
        return group;
    }

    private static DesignIssue ProduceUnconnectedPinIssue()
    {
        var comp = CreateComponent("dangling", 0, 0, 100, 100);
        AddPin(comp, "unused");

        return new DesignValidator()
            .ValidateUnconnectedPins(new[] { comp }, Array.Empty<WaveguideConnection>())
            .Single();
    }

    private static DesignIssue ProducePinMismatchIssue()
    {
        var connection = CreateConnection(0, 0, 100, 0);
        connection.StartPin.WaveguideWidthMicrometers = 0.5;
        connection.EndPin.WaveguideWidthMicrometers = 1.2;

        return new DesignValidator()
            .Validate(new[] { connection })
            .Single(i => i.Type == DesignIssueType.PinMismatch);
    }

    private static DesignIssue ProduceSpacingIssue()
    {
        var conn1 = CreateConnection(0, 0, 100, 0);
        var conn2 = CreateConnection(0, 2.0, 100, 2.0);

        return new WaveguideSpacingDetector()
            .DetectViolations(new[] { conn1, conn2 }, Array.Empty<ComponentGroup>(), 5.0)
            .Single();
    }

    private static DesignIssue ProduceMinWidthIssue()
    {
        var connection = CreateConnection(0, 0, 100, 0);
        connection.StartPin.Layer = 3;
        connection.EndPin.Layer = 3;
        connection.StartPin.WaveguideWidthMicrometers = 0.2;
        var rule = new WaveguideMinWidthRule(0.45, new[] { 3 }, "strip", "test.drc");

        return new WaveguideMinWidthChecker()
            .CheckConnections(new[] { connection }, new[] { rule })
            .Single();
    }

    private static DesignIssue ProduceFootprintOverlapIssue()
    {
        var first = CreateComponent("AND0", 0, 0, 100, 50);
        var second = CreateComponent("NOT0", 80, 20, 100, 50);

        return new ComponentFootprintOverlapChecker()
            .DetectOverlaps(new[] { first, second })
            .Single();
    }

    private static DesignIssue ProduceCrossingIssue()
    {
        var conn1 = CreateConnection(0, 50, 100, 50);
        var conn2 = CreateConnection(50, 0, 50, 100);

        return new DesignValidator()
            .Validate(new[] { conn1, conn2 })
            .Single(i => i.Type == DesignIssueType.WaveguideCrossing);
    }

    private static DesignIssue ProduceChipletIssue(DesignIssueType type)
    {
        var link = BuildChipletLink(out var chipletB);
        switch (type)
        {
            case DesignIssueType.ChipletInterfaceNotFacing:
                link.EndPin.AngleDegrees = 90;
                break;
            case DesignIssueType.ChipletInterfaceLateralOffset:
                chipletB.MoveGroup(0, 2.0);
                break;
            case DesignIssueType.ChipletInterfaceOffEdge:
                MoveEndCouplerInside(link, inset: 1.5);
                break;
            case DesignIssueType.ChipletInterfaceGapLoss:
                chipletB.MoveGroup(20, 0);
                break;
        }

        return new ChipletInterfaceChecker()
            .Check(new[] { link }, WavelengthNm)
            .Single(i => i.Type == type);
    }

    private static WaveguideConnection BuildChipletLink(out ComponentGroup chipletB)
    {
        var startCoupler = CreateEdgeCoupler("a_ec", 0, 0, CouplerWidth, PinY, 0);
        var endCoupler = CreateEdgeCoupler("b_ec", CouplerWidth, 0, 0, PinY, 180);
        var chipletA = new ComponentGroup("Chiplet A");
        chipletA.AddChild(startCoupler);
        chipletB = new ComponentGroup("Chiplet B");
        chipletB.AddChild(endCoupler);
        return new WaveguideConnection
        {
            StartPin = startCoupler.PhysicalPins[0],
            EndPin = endCoupler.PhysicalPins[0],
        };
    }

    private static void MoveEndCouplerInside(WaveguideConnection link, double inset)
    {
        var endCoupler = link.EndPin.ParentComponent;
        var chipletB = (ComponentGroup)endCoupler.ParentGroup!;
        double dieEdgeX = endCoupler.PhysicalX;
        endCoupler.PhysicalX += inset;
        var spacer = CreateEdgeCoupler("b_spacer", dieEdgeX, 0, 0, PinY, 180);
        spacer.WidthMicrometers = 1;
        chipletB.AddChild(spacer);
    }

    private static Component CreateEdgeCoupler(
        string identifier, double x, double y, double pinOffsetX, double pinOffsetY, double pinAngle)
    {
        var pin = new PhysicalPin
        {
            Name = "fiber",
            OffsetXMicrometers = pinOffsetX,
            OffsetYMicrometers = pinOffsetY,
            AngleDegrees = pinAngle,
        };
        return new Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "demo.io",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            identifier,
            new DiscreteRotation(),
            new List<PhysicalPin> { pin })
        {
            WidthMicrometers = CouplerWidth,
            HeightMicrometers = CouplerHeight,
            PhysicalX = x,
            PhysicalY = y,
            TemplateName = "Edge Coupler",
        };
    }

    private static Component CreateComponent(
        string identifier, double x, double y, double width, double height)
    {
        var component = TestComponentFactory.CreateStraightWaveGuide();
        component.Identifier = identifier;
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        return component;
    }

    private static PhysicalPin AddPin(Component component, string name)
    {
        var pin = new PhysicalPin
        {
            Name = name,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = component,
        };
        component.PhysicalPins.Add(pin);
        return pin;
    }

    private static WaveguideConnection CreateConnection(double x1, double y1, double x2, double y2)
    {
        var comp1 = CreateComponent($"start_{x1}_{y1}", x1, y1, 1, 1);
        var comp2 = CreateComponent($"end_{x2}_{y2}", x2, y2, 1, 1);
        var connection = new WaveguideConnection
        {
            StartPin = AddPin(comp1, "out"),
            EndPin = AddPin(comp2, "in"),
        };

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        connection.RestoreCachedPath(path);
        return connection;
    }
}

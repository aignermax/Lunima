using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Unit tests for connection×connection waveguide crossing detection
/// (<see cref="ConnectionCrossingDetector"/> via <see cref="DesignValidator"/>):
/// properly crossing connections are reported as one error naming both, parallel
/// connections and pairs sharing an endpoint pin are not.
/// </summary>
public class ConnectionCrossingDetectorTests
{
    private readonly DesignValidator _validator = new();

    [Fact]
    public void Validate_CrossingConnections_ReturnsOneIssueNamingBoth()
    {
        var conn1 = CreateConnection("compA", "out", "compB", "in", 0, 50, 100, 50);
        var conn2 = CreateConnection("compC", "out", "compD", "in", 50, 0, 50, 100);

        var result = _validator.Validate(new[] { conn1, conn2 });

        var crossings = result.Where(i => i.Type == DesignIssueType.WaveguideCrossing).ToList();
        crossings.Count.ShouldBe(1);
        crossings[0].Description.ShouldContain("compA.out");
        crossings[0].Description.ShouldContain("compB.in");
        crossings[0].Description.ShouldContain("compC.out");
        crossings[0].Description.ShouldContain("compD.in");
    }

    [Fact]
    public void Validate_CrossingConnections_CarriesLocalizationKeyAndArgs()
    {
        var conn1 = CreateConnection("compA", "out", "compB", "in", 0, 50, 100, 50);
        var conn2 = CreateConnection("compC", "out", "compD", "in", 50, 0, 50, 100);

        var result = _validator.Validate(new[] { conn1, conn2 });

        var crossing = result.Single(i => i.Type == DesignIssueType.WaveguideCrossing);
        crossing.LocalizationKey.ShouldBe(ConnectionCrossingDetector.CrossingLocalizationKey);
        crossing.LocalizationArgs.ShouldNotBeNull();
        crossing.LocalizationArgs.Count.ShouldBe(2);
        crossing.Connection.ShouldBe(conn1);
    }

    [Fact]
    public void Validate_ParallelNonCrossingConnections_ReturnsNoCrossingIssue()
    {
        var conn1 = CreateConnection("compA", "out", "compB", "in", 0, 0, 100, 0);
        var conn2 = CreateConnection("compC", "out", "compD", "in", 0, 10, 100, 10);

        var result = _validator.Validate(new[] { conn1, conn2 });

        result.ShouldNotContain(i => i.Type == DesignIssueType.WaveguideCrossing);
    }

    [Fact]
    public void Validate_ConnectionsSharingEndpointPin_ReturnsNoCrossingIssue()
    {
        // Both connections start at the same pin; the second one's routed path is
        // forced across the first one's path — the shared pin must suppress the report.
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.Identifier = "compA";
        var sharedPin = AddPin(comp1, "out");
        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.Identifier = "compB";
        var pin2 = AddPin(comp2, "in");
        var comp3 = TestComponentFactory.CreateStraightWaveGuide();
        comp3.Identifier = "compC";
        var pin3 = AddPin(comp3, "in");

        var conn1 = new WaveguideConnection { StartPin = sharedPin, EndPin = pin2 };
        conn1.RestoreCachedPath(StraightPath(0, 50, 100, 50));
        var conn2 = new WaveguideConnection { StartPin = sharedPin, EndPin = pin3 };
        conn2.RestoreCachedPath(StraightPath(50, 0, 50, 100));

        var result = _validator.Validate(new[] { conn1, conn2 });

        result.ShouldNotContain(i => i.Type == DesignIssueType.WaveguideCrossing);
    }

    [Fact]
    public void Validate_ConnectionsTouchingAtEndpointsOnly_ReturnsNoCrossingIssue()
    {
        // Two distinct pin pairs whose routes meet end-to-end at (50,50) — a touch,
        // not a proper crossing.
        var conn1 = CreateConnection("compA", "out", "compB", "in", 0, 50, 50, 50);
        var conn2 = CreateConnection("compC", "out", "compD", "in", 50, 50, 50, 100);

        var result = _validator.Validate(new[] { conn1, conn2 });

        result.ShouldNotContain(i => i.Type == DesignIssueType.WaveguideCrossing);
    }

    [Fact]
    public void Validate_ConnectionWithoutPath_ReturnsNoCrossingIssue()
    {
        var conn1 = CreateConnection("compA", "out", "compB", "in", 0, 50, 100, 50);
        var conn2 = CreateConnectionPair("compC", "compD"); // no routed path

        var result = _validator.Validate(new[] { conn1, conn2 });

        result.ShouldNotContain(i => i.Type == DesignIssueType.WaveguideCrossing);
    }

    [Fact]
    public void Validate_WithGroups_CrossingPairReportedOnceNotTwice()
    {
        // The full aggregation (with frozen-path overlap detection) must report a
        // proper conn×conn crossing exactly once — as WaveguideCrossing, not also as
        // OverlappingPaths.
        var conn1 = CreateConnection("compA", "out", "compB", "in", 0, 50, 100, 50);
        var conn2 = CreateConnection("compC", "out", "compD", "in", 50, 0, 50, 100);

        var result = _validator.Validate(new[] { conn1, conn2 }, Array.Empty<ComponentGroup>());

        result.Count(i => i.Type == DesignIssueType.WaveguideCrossing).ShouldBe(1);
        result.ShouldNotContain(i => i.Type == DesignIssueType.OverlappingPaths);
    }

    [Fact]
    public void DetectCrossings_NullConnections_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            new ConnectionCrossingDetector().DetectCrossings(null!));
    }

    [Fact]
    public void DetectCrossings_ThreeConnectionsOneCrossesTwo_ReturnsTwoIssues()
    {
        var conn1 = CreateConnection("compA", "out", "compB", "in", 0, 50, 100, 50);
        var conn2 = CreateConnection("compC", "out", "compD", "in", 0, 70, 100, 70);
        var conn3 = CreateConnection("compE", "out", "compF", "in", 50, 0, 50, 100);

        var result = new ConnectionCrossingDetector().DetectCrossings(new[] { conn1, conn2, conn3 });

        result.Count(i => i.Type == DesignIssueType.WaveguideCrossing).ShouldBe(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static WaveguideConnection CreateConnection(
        string startComponent, string startPin,
        string endComponent, string endPin,
        double x1, double y1, double x2, double y2)
    {
        var connection = CreateConnectionPair(startComponent, endComponent, startPin, endPin);
        connection.RestoreCachedPath(StraightPath(x1, y1, x2, y2));
        return connection;
    }

    private static RoutedPath StraightPath(double x1, double y1, double x2, double y2)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y1, x2, y2, 0));
        return path;
    }

    private static WaveguideConnection CreateConnectionPair(
        string startComponentId, string endComponentId,
        string startPinName = "out", string endPinName = "in")
    {
        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.Identifier = startComponentId;
        var pin1 = AddPin(comp1, startPinName);

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.Identifier = endComponentId;
        var pin2 = AddPin(comp2, endPinName);

        return new WaveguideConnection { StartPin = pin1, EndPin = pin2 };
    }

    private static PhysicalPin AddPin(Component component, string name)
    {
        var pin = new PhysicalPin
        {
            Name = name,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = 0,
            ParentComponent = component
        };
        component.PhysicalPins.Add(pin);
        return pin;
    }
}

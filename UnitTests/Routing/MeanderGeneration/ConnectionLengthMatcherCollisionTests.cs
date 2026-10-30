using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Routing.MeanderGeneration;
using Shouldly;
using Xunit;

namespace UnitTests.Routing.MeanderGeneration;

/// <summary>
/// Length matching on a live design: the meander keeps clear of neighboring routes, uses the
/// requested bend radius, survives styled-route recalculation, and a target the geometry no
/// longer meets is re-established or reported by the design checks.
/// </summary>
public class ConnectionLengthMatcherCollisionTests
{
    private const double Tolerance = 1.0;
    private const double Target = 450.0;

    [Fact]
    public void ApplyTargetLength_NeighborRouteOnPreferredSide_MeanderKeepsClearOfIt()
    {
        var (components, connection) = CreateStraightDesign();
        var neighbor = FreeRoute(100, 55, 250, 55);

        var result = new ConnectionLengthMatcher().ApplyTargetLength(
            connection, components, Target, Tolerance, new[] { connection, neighbor });

        result.IsSuccess.ShouldBeTrue(result.FailureMessage);
        connection.PathLengthMicrometers.ShouldBe(Target, Tolerance);
        PathIntersectionDetector.Crosses(connection.RoutedPath!, neighbor.RoutedPath!).ShouldBeFalse();
        PathIntersectionDetector.MinimumDistance(connection.RoutedPath!, neighbor.RoutedPath!)
            .ShouldBeGreaterThanOrEqualTo(PhotonicConstants.DefaultMinWaveguideSpacingMicrometers);
    }

    [Fact]
    public void ApplyTargetLength_NeighborsOnBothSides_ReportsCollisionAndLeavesRouteUntouched()
    {
        var (components, connection) = CreateStraightDesign();
        var routeBefore = connection.RoutedPath;
        var above = FreeRoute(100, 33, 250, 33);
        var below = FreeRoute(100, 17, 250, 17);

        var result = new ConnectionLengthMatcher().ApplyTargetLength(
            connection, components, Target, Tolerance, new[] { connection, above, below });

        result.FailureReason.ShouldBe(MeanderFailureReason.CollidesWithRoutes);
        connection.RoutedPath.ShouldBeSameAs(routeBefore);
        connection.TargetLengthMicrometers.ShouldBeNull();
    }

    [Fact]
    public void ApplyTargetLength_WithRadius_BendsUseItAndConnectionRemembersIt()
    {
        var (components, connection) = CreateStraightDesign();

        var result = new ConnectionLengthMatcher().ApplyTargetLength(
            connection, components, Target, Tolerance, new[] { connection }, meanderRadiusMicrometers: 15);

        result.IsSuccess.ShouldBeTrue(result.FailureMessage);
        connection.BendRadiusMicrometers.ShouldBe(15);
        connection.RoutedPath!.Segments.OfType<BendSegment>().ShouldAllBe(bend => Math.Abs(bend.RadiusMicrometers - 15) < 1e-9);
        connection.PathLengthMicrometers.ShouldBe(Target, Tolerance);
    }

    [Fact]
    public void ApplyTargetLength_RadiusBelowProcessFloor_UsesTheFloor()
    {
        var (components, connection) = CreateStraightDesign();
        var router = new WaveguideRouter { ProcessMinBendRadiusMicrometers = 12 };

        var matcher = new ConnectionLengthMatcher(router);
        matcher.ResolveProcessFloor(connection).ShouldBe(12);
        matcher.ApplyTargetLength(connection, components, Target, Tolerance, new[] { connection },
            meanderRadiusMicrometers: 5).IsSuccess.ShouldBeTrue();

        connection.RoutedPath!.Segments.OfType<BendSegment>()
            .ShouldAllBe(bend => bend.RadiusMicrometers >= 12 - 1e-9);
    }

    [Fact]
    public void ReapplyUnmetTargets_RouterReplacedTheMeander_StretchesItBack()
    {
        var (components, connection) = CreateStraightDesign();
        connection.TargetLengthMicrometers = Target;
        connection.LengthToleranceMicrometers = Tolerance;
        ConnectionLengthMatcher.MeetsTarget(connection).ShouldBeFalse();

        var changed = new ConnectionLengthMatcher().ReapplyUnmetTargets(new[] { connection }, components);

        changed.ShouldBe(new[] { connection });
        ConnectionLengthMatcher.MeetsTarget(connection).ShouldBeTrue();
        connection.IsRouteFrozen.ShouldBeTrue();
    }

    [Fact]
    public void RecalculateTransmission_StyledLengthMatchedRoute_KeepsTheMeander()
    {
        var (components, connection) = CreateStraightDesign();
        connection.Type = WaveguideType.SBend;
        new ConnectionLengthMatcher().ApplyTargetLength(connection, components, Target, Tolerance)
            .IsSuccess.ShouldBeTrue();

        connection.RecalculateTransmission(new WaveguideRouter());

        connection.PathLengthMicrometers.ShouldBe(Target, Tolerance);
    }

    private static WaveguideConnection FreeRoute(double x1, double y, double x2, double _)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(x1, y, x2, y, 0));
        var connection = new WaveguideConnection();
        connection.RestoreCachedPath(path);
        return connection;
    }

    private static (List<Component> Components, WaveguideConnection Connection) CreateStraightDesign()
    {
        var source = CreateComponent("src", 0, 0, 100, 50);
        var startPin = new PhysicalPin
        {
            Name = "out", ParentComponent = source, OffsetXMicrometers = 100, OffsetYMicrometers = 25, AngleDegrees = 0
        };
        source.PhysicalPins.Add(startPin);

        var destination = CreateComponent("dst", 250, 0, 100, 50);
        var endPin = new PhysicalPin
        {
            Name = "in", ParentComponent = destination, OffsetXMicrometers = 0, OffsetYMicrometers = 25, AngleDegrees = 180
        };
        destination.PhysicalPins.Add(endPin);

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(100, 25, 250, 25, 0));
        var connection = new WaveguideConnection { StartPin = startPin, EndPin = endPin };
        connection.RestoreCachedPath(path);
        return (new List<Component> { source, destination }, connection);
    }

    private static Component CreateComponent(string identifier, double x, double y, double width, double height)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());
        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: $"test_{identifier.ToLower()}",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: identifier,
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>());
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;
        return component;
    }
}

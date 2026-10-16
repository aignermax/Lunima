using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.Routing;
using CAP_Core.Routing.InterconnectRouting;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

public class SiNEdgeCouplerDirectionTests
{
    [Fact]
    public void BendWithObliquePins_QuantizedFallbackDoesNotRepeatSearches()
    {
        var first = Create(5066.870, -389.927);
        var second = Create(4939.153, -454.705);
        var start = first.PhysicalPins.Single(p => p.Name == "o2");
        var end = second.PhysicalPins.Single(p => p.Name == "o2");
        start.AngleDegrees = end.AngleDegrees = 22.5;
        var router = new WaveguideRouter();
        router.InitializePathfindingGrid(4500, 200, 5300, 800, Array.Empty<Component>());
        var manager = new WaveguideConnectionManager(router);
        var connection = manager.AddConnectionDeferred(start, end);
        connection.Type = WaveguideType.Bend;
        manager.RecalculateAllTransmissions();
        connection.IsRouteFrozen.ShouldBeFalse("no arc primitive can make the requested U-turn");
        connection.IsBlockedFallback.ShouldBeFalse();
        int searched = 0;
        manager.RecalculateAllTransmissions(() => searched++);
        searched.ShouldBe(0);
    }

    [Fact]
    public void HeadingRepair_DoesNotInvalidateQuantizedAStarFallbackOrManualEdits()
    {
        var first = Create(5066.870, -389.927);
        var second = Create(4939.153, -454.705);
        var connection = new WaveguideConnection {
            StartPin = first.PhysicalPins.Single(p => p.Name == "o2"),
            EndPin = second.PhysicalPins.Single(p => p.Name == "o2"), Type = WaveguideType.Bend };
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(4866.870, 389.927, 4739.153, 454.705, 157.5));
        connection.ReplaceRoutedPath(path);
        GeneratedRoutePinDirections.NeedsRepair(connection).ShouldBeFalse(
            "an unfrozen Bend result is an A* fallback, whose quantized headings are not styled-arc tangents");
        connection.IsRouteFrozen = true;
        connection.BendRadiusOverrides[0] = 20;
        GeneratedRoutePinDirections.NeedsRepair(connection).ShouldBeFalse("manual geometry remains owned by its author");
        connection.BendRadiusOverrides.Clear();
        connection.Type = WaveguideType.Auto;
        path.IsDirectStyledRoute = true;
        GeneratedRoutePinDirections.NeedsRepair(connection).ShouldBeFalse(
            "a sampled sine chord is not an analytic arc tangent");
    }

    [Theory]
    [InlineData(WaveguideType.Auto)]
    [InlineData(WaveguideType.Bend)]
    public void OnRealDie_RouteArrivesFromOutsideAndStaysStable(WaveguideType type)
    {
        var first = Create(5066.870, -389.927);
        var second = Create(4939.153, -454.705);
        var die = new ComponentGroup("Die 1");
        die.AddChildren(new[] { first, second });
        die.PhysicalX = 66.9;
        die.PhysicalY = 9.1;
        die.Die = new CAP_Core.Components.Dies.DieDefinition(5000, 5000);
        var router = new WaveguideRouter();
        router.InitializePathfindingGrid(4500, 200, 5300, 800, new[] { die });
        var manager = new WaveguideConnectionManager(router);
        var connection = manager.AddConnectionDeferred(first.PhysicalPins.Single(p => p.Name == "o2"),
            second.PhysicalPins.Single(p => p.Name == "o2"));
        connection.Type = type;
        manager.RecalculateAllTransmissions();
        var path = connection.RoutedPath!;
        path.IsBlockedFallback.ShouldBeFalse();
        path.Segments.Last(s => s.LengthMicrometers > 1e-4).EndAngleDegrees.ShouldBe(0, 1e-6);
        router.IsPathBlockedByComponents(path.Segments, new[] { connection.StartPin, connection.EndPin }).ShouldBeFalse();
        manager.RecalculateAllTransmissions();
        connection.RoutedPath!.Segments.Last(s => s.LengthMicrometers > 1e-4).EndAngleDegrees.ShouldBe(0, 1e-6);
        router.IsPathBlockedByComponents(connection.RoutedPath.Segments,
            new[] { connection.StartPin, connection.EndPin }).ShouldBeFalse();
        int searched = 0;
        manager.RecalculateAllTransmissions(() => searched++);
        searched.ShouldBe(0, "settled routes must not repeat searches due to heading validation");
    }

    [Fact]
    public void BendStyle_SameFacingPins_DoesNotBuildWrongWayS()
    {
        var first = Create(5066.870, -389.927);
        var second = Create(4939.153, -454.705);
        var path = ConnectionStyleRouteBuilder.Build(first.PhysicalPins.Single(p => p.Name == "o2"),
            second.PhysicalPins.Single(p => p.Name == "o2"), WaveguideType.Bend);
        path.ShouldBeNull("an S keeps its initial heading and cannot make this 180 degree arrival turn");
    }

    [Theory]
    [InlineData(WaveguideType.Bend)]
    [InlineData(WaveguideType.Auto)]
    public void ExistingGeneratedWrongWayS_IsRepairedOnOrdinaryPass(WaveguideType type)
    {
        var first = Create(5066.870, -389.927);
        var second = Create(4939.153, -454.705);
        var start = first.PhysicalPins.Single(p => p.Name == "o2");
        var end = second.PhysicalPins.Single(p => p.Name == "o2");
        var router = new WaveguideRouter();
        // Isolate heading validation from the component-body collision check.
        router.InitializePathfindingGrid(4500, 200, 5300, 800, Array.Empty<Component>());
        var manager = new WaveguideConnectionManager(router);
        var connection = manager.AddConnectionDeferred(start, end);
        connection.Type = type;
        var old = new RoutedPath { IsDirectStyledRoute = type == WaveguideType.Auto };
        old.Segments.AddRange(SBendGeometry.BuildSymmetricS(4866.870, 389.927, 180, 127.717, -64.778)!);
        connection.ReplaceRoutedPath(old);
        connection.IsRouteFrozen = type == WaveguideType.Bend;
        manager.RecalculateAllTransmissions();
        connection.RoutedPath.ShouldNotBeSameAs(old);
        connection.RoutedPath!.Segments.Last(s => s.LengthMicrometers > 1e-4).EndAngleDegrees.ShouldBe(0, 1e-6);
    }

    [Theory]
    [InlineData(true, 10)]
    [InlineData(true, 30)]
    [InlineData(false, 10)]
    [InlineData(true, 50)]
    [InlineData(false, 50)]
    public void RotatedRealCouplers_KeepArrivalOutsideBody(bool direct, double radius)
    {
        var first = Create(5066.870, -389.927);
        var second = Create(4939.153, -454.705);
        var start = first.PhysicalPins.Single(p => p.Name == "o2");
        var end = second.PhysicalPins.Single(p => p.Name == "o2");
        start.GetAbsoluteAngle().ShouldBe(180);
        end.GetAbsoluteAngle().ShouldBe(180);
        end.GetAbsolutePosition().x.ShouldBe(second.PhysicalX);
        var router = new WaveguideRouter { PreferDirectStyledRoutes = direct, MinBendRadiusMicrometers = radius };
        router.InitializePathfindingGrid(4500, 200, 5300, 800, new[] { first, second });
        var manager = new WaveguideConnectionManager(router);
        var connection = manager.AddConnectionDeferred(start, end);
        connection.BendRadiusMicrometers = radius;
        connection.RecalculateTransmission(router);
        connection.RoutedPath!.Segments.Last(s => s.LengthMicrometers > 1e-4).EndAngleDegrees.ShouldBe(0, 1e-6);
        manager.RecalculateAllTransmissions();
        var path = connection.RoutedPath!;
        path.IsBlockedFallback.ShouldBeFalse();
        path.Segments.Last(s => s.LengthMicrometers > 1e-4).EndAngleDegrees.ShouldBe(0, 1e-6);
        router.IsPathBlockedByComponents(path.Segments, new[] { start, end }).ShouldBeFalse();
    }

    private static Component Create(double exportX, double exportY)
    {
        var template = TestPdkLoader.LoadFromPdk("cornerstone-sin-pdk.json").Single(t => t.Name == "SiN Edge Coupler");
        var c = ComponentTemplates.CreateFromTemplate(template, 0, 0);
        ComponentPoseTransform.Rotate90CounterClockwise(c);
        ComponentPoseTransform.Rotate90CounterClockwise(c);
        var origin = NazcaCoordinateMapper.GetCellPlacement(c, null);
        c.PhysicalX = exportX - origin.X;
        c.PhysicalY = origin.Y - exportY;
        var placement = NazcaCoordinateMapper.GetCellPlacement(c, null);
        placement.X.ShouldBe(exportX, 1e-6);
        placement.Y.ShouldBe(exportY, 1e-6);
        placement.RotationDegrees.ShouldBe(-180);
        return c;
    }

}

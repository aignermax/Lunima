using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.BendLoss;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.LightCalculation.BendLoss;

/// <summary>
/// The radius model is applied wherever a connection's transmission is computed: routed
/// connections (inheriting the endpoint PDK's model), frozen group paths and the router's
/// bend upsizing — a tight bend costs light, a gentle one does not.
/// </summary>
public class ConnectionBendLossIntegrationTests
{
    [Fact]
    public void TightCornerConnection_TransmitsLessThanGentleCorner()
    {
        var tight = ConnectionWithCachedPath(Corner(5));
        var gentle = ConnectionWithCachedPath(Corner(50));

        tight.TransmissionCoefficient.Magnitude.ShouldBeLessThan(gentle.TransmissionCoefficient.Magnitude);
        tight.LossBreakdown.BendLossDb.ShouldBeGreaterThan(gentle.LossBreakdown.BendLossDb * 100);
        tight.TotalLossDb.ShouldBe(tight.LossBreakdown.TotalLossDb);
        tight.TransmissionCoefficient.Magnitude.ShouldBeLessThanOrEqualTo(1.0);
    }

    [Fact]
    public void SameGeometry_LowContrastPlatformLosesMore()
    {
        var soi = ConnectionWithCachedPath(Corner(15), BendLossPresets.SoiStrip);
        var nitride = ConnectionWithCachedPath(Corner(15), BendLossPresets.SiliconNitride);

        nitride.TotalLossDb.ShouldBeGreaterThan(soi.TotalLossDb + 0.1);
    }

    [Fact]
    public void Manager_InheritsTheEndpointPdkModel()
    {
        var (start, end) = TwoComponents();
        start.WaveguideBendLoss = BendLossPresets.SiliconNitride;
        var manager = new WaveguideConnectionManager(new WaveguideRouter());

        var connection = manager.AddConnectionWithCachedRoute(
            start.PhysicalPins.Single(p => p.Name == "out"), end.PhysicalPins.Single(p => p.Name == "in"), Corner(30));

        connection.BendLoss.ShouldBeSameAs(BendLossPresets.SiliconNitride);
        connection.LossBreakdown.Family.ShouldBe(WaveguideFamily.SiliconNitride);
    }

    [Fact]
    public void Manager_WithoutPdkModel_UsesItsDefault()
    {
        var (start, end) = TwoComponents();
        var manager = new WaveguideConnectionManager(new WaveguideRouter());

        var connection = manager.AddConnectionWithCachedRoute(
            start.PhysicalPins.Single(p => p.Name == "out"), end.PhysicalPins.Single(p => p.Name == "in"), Corner(10));

        connection.BendLoss.ShouldBeSameAs(BendLossPresets.Generic);
    }

    [Fact]
    public void FrozenPath_KeepsTheConnectionLoss_AndResolvesThePdkModelAfterReload()
    {
        var (start, end) = TwoComponents();
        start.WaveguideBendLoss = BendLossPresets.InpDeepEtch;
        var connection = new WaveguideConnection
        {
            StartPin = start.PhysicalPins.Single(p => p.Name == "out"),
            EndPin = end.PhysicalPins.Single(p => p.Name == "in"),
            BendLoss = BendLossPresets.InpDeepEtch,
        };
        connection.RestoreCachedPath(Corner(12));

        var frozen = new FrozenWaveguidePath { Path = Corner(12), StartPin = connection.StartPin, EndPin = connection.EndPin };
        frozen.CaptureSettingsFrom(connection);
        frozen.TransmissionCoefficient.Real.ShouldBe(connection.TransmissionCoefficient.Real, 1e-12);

        frozen.BendLoss = null; // a .lun round-trip does not persist the model
        frozen.ResolveBendLoss().ShouldBeSameAs(BendLossPresets.InpDeepEtch);
        frozen.TransmissionCoefficient.Real.ShouldBe(connection.TransmissionCoefficient.Real, 1e-12);
    }

    [Fact]
    public async Task Router_PrefersGentleBends_WhenFreeSpaceAllows()
    {
        var canvas = new DesignCanvasViewModel();
        canvas.InitializeAStarRouting(0, 0, 3000, 3000);
        var a = AddWaveguide(canvas, 100, 100);
        var b = AddWaveguide(canvas, 900, 700);

        var vm = canvas.ConnectPins(a.PhysicalPins.Single(p => p.Name == "out"), b.PhysicalPins.Single(p => p.Name == "in"));
        await canvas.RecalculateRoutesAsync();

        var loss = vm!.Connection.LossBreakdown;
        loss.BendCount.ShouldBeGreaterThan(0);
        loss.MinBendRadiusMicrometers!.Value.ShouldBeGreaterThanOrEqualTo(20,
            "with hundreds of µm of free space the router must not stay at the 10 µm minimum");
        loss.BendLossDb.ShouldBeLessThan(loss.BendCount * BendLossPresets.Generic.RadiationLossDb(10, 90));
    }

    private static WaveguideConnection ConnectionWithCachedPath(RoutedPath path, BendLossModel? model = null)
    {
        var connection = new WaveguideConnection { BendLoss = model ?? BendLossPresets.Generic };
        connection.RestoreCachedPath(path);
        return connection;
    }

    private static (Component Start, Component End) TwoComponents()
    {
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        end.PhysicalX = 600;
        return (start, end);
    }

    private static Component AddWaveguide(DesignCanvasViewModel canvas, double x, double y)
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.WidthMicrometers = 250;
        component.HeightMicrometers = 250;
        component.PhysicalX = x;
        component.PhysicalY = y;
        canvas.AddComponent(component);
        return component;
    }

    private static RoutedPath Corner(double radius)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        path.Segments.Add(new BendSegment(100, radius, radius, 0, 90));
        path.Segments.Add(new StraightSegment(100 + radius, radius, 100 + radius, 200, 90));
        return path;
    }
}

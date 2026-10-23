using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace UnitTests.Routing.Snapshots;

internal sealed class RoutingSnapshotFixture
{
    internal Component[] Components { get; }
    internal WaveguideRouter Router { get; } = new()
    {
        UseDiagonalRouting = false,
        Phase1MaxNodes = 2000,
        Phase2MaxNodes = 5000,
        ProcessMinBendRadiusMicrometers = 12,
        MetalProcessMinBendRadiusMicrometers = 45
    };
    internal WaveguideConnectionManager Manager { get; }
    internal WaveguideConnection First => Manager.Connections[0];

    internal RoutingSnapshotFixture(bool sequential = true)
    {
        Components = [ComponentAt(0, 0), ComponentAt(200, 0), ComponentAt(0, 100), ComponentAt(200, 100)];
        Manager = new WaveguideConnectionManager(Router)
        {
            UseSequentialRouting = sequential,
            MaxRoutingAttempts = 2,
            MetalTraceWidthMicrometers = 8,
            WaveguideWidthMicrometers = 3,
            DefaultPropagationLossDbPerCm = 1.7,
            DefaultExcessBendLossDbPer90Deg = 0.2,
            EnableCoherentPropagationPhase = true
        };
        // Intentionally reverse geometric order: order is user-owned data, not a sort key.
        Manager.AddExistingConnection(new WaveguideConnection
            { StartPin = Components[2].PhysicalPins[1], EndPin = Components[3].PhysicalPins[0] });
        Manager.AddExistingConnection(new WaveguideConnection
            { StartPin = Components[0].PhysicalPins[1], EndPin = Components[1].PhysicalPins[0] });
        Router.InitializePathfindingGrid(-40, -40, 260, 160, Components, 4);
    }

    internal static RoutedPath Straight(WaveguideConnection connection, double offsetY = 0)
    {
        var a = connection.StartPin.GetAbsolutePosition();
        var b = connection.EndPin.GetAbsolutePosition();
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(a.x, a.y + offsetY, b.x, b.y + offsetY, 0));
        return path;
    }

    private static Component ComponentAt(double x, double y)
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = component.HeightMicrometers = 10;
        component.PhysicalPins[0].OffsetXMicrometers = 0;
        component.PhysicalPins[1].OffsetXMicrometers = 10;
        component.PhysicalPins[0].OffsetYMicrometers = component.PhysicalPins[1].OffsetYMicrometers = 5;
        component.PhysicalPins[0].AngleDegrees = 180;
        component.PhysicalPins[1].AngleDegrees = 0;
        return component;
    }
}

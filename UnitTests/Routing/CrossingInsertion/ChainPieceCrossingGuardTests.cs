using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using CAP_Core.Routing.CrossingInsertion;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Routing.CrossingInsertion;

/// <summary>
/// A crossing chain may cross other wires only through its placed crossings: a piece that
/// cuts through another wire anywhere else would export as overlapping geometry.
/// </summary>
public class ChainPieceCrossingGuardTests
{
    private readonly CAP_Core.Components.Core.Component _host = TestComponentFactory.CreateBasicComponent();

    [Fact]
    public void PieceCuttingThroughAnotherWire_IsCaught()
    {
        var (manager, _) = ManagerWithVerticalWire();
        var piece = Piece((0, 50), (200, 50));

        ChainPieceCrossingGuard.CrossesAnotherWire(new[] { piece }, Array.Empty<WaveguideConnection>(), manager)
            .ShouldBeTrue();
    }

    [Fact]
    public void PieceBesideAnotherWire_Passes()
    {
        var (manager, _) = ManagerWithVerticalWire();
        var piece = Piece((0, 50), (80, 50));

        ChainPieceCrossingGuard.CrossesAnotherWire(new[] { piece }, Array.Empty<WaveguideConnection>(), manager)
            .ShouldBeFalse();
    }

    [Fact]
    public void WireTheChainReplaces_IsNotChecked()
    {
        var (manager, vertical) = ManagerWithVerticalWire();
        var piece = Piece((0, 50), (200, 50));

        ChainPieceCrossingGuard.CrossesAnotherWire(new[] { piece }, new[] { vertical }, manager)
            .ShouldBeFalse("the crossed wire is split into its own docked pieces");
    }

    private (WaveguideConnectionManager Manager, WaveguideConnection Vertical) ManagerWithVerticalWire()
    {
        var manager = new WaveguideConnectionManager(new WaveguideRouter());
        var (vertical, path) = Piece((100, 0), (100, 100));
        vertical.RestoreCachedPath(path);
        manager.Connections.Add(vertical);
        return (manager, vertical);
    }

    private (WaveguideConnection Connection, RoutedPath Path) Piece((double X, double Y) from, (double X, double Y) to)
    {
        var connection = new WaveguideConnection
        {
            StartPin = TestComponentFactory.CreateRoutingPin(_host, from.X, from.Y, 0),
            EndPin = TestComponentFactory.CreateRoutingPin(_host, to.X, to.Y, 180),
        };
        var path = new RoutedPath { Segments = { new StraightSegment(from.X, from.Y, to.X, to.Y, 0) } };
        return (connection, path);
    }
}

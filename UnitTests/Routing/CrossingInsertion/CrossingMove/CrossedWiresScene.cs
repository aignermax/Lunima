using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace UnitTests.Routing.CrossingInsertion.CrossingMove;

/// <summary>
/// A crossing at (300, 300) with four straight pieces docked on it: the horizontal wire
/// left (50, 300) → west port, east port → right (550, 300); the vertical wire top (300, 50)
/// → north port, south port → bottom (300, 550). Built on a canvas with a 600 × 600 µm
/// routing grid, so moves can re-route the pieces.
/// </summary>
internal sealed class CrossedWiresScene
{
    public const double ChipSize = 600;
    public const double Center = 300;

    private CrossedWiresScene(DesignCanvasViewModel canvas, Component crossing,
        WaveguideConnection west, WaveguideConnection east, WaveguideConnection north, WaveguideConnection south)
    {
        Canvas = canvas;
        Crossing = crossing;
        West = west;
        East = east;
        North = north;
        South = south;
    }

    public DesignCanvasViewModel Canvas { get; }
    public Component Crossing { get; }
    public WaveguideConnection West { get; }
    public WaveguideConnection East { get; }
    public WaveguideConnection North { get; }
    public WaveguideConnection South { get; }

    public IReadOnlyList<WaveguideConnection> Pieces => new[] { West, East, North, South };

    /// <summary>Builds the scene onto <paramref name="canvas"/> (a fresh one when null).</summary>
    public static CrossedWiresScene Build(DesignCanvasViewModel? canvas = null, bool routerPlaced = false)
    {
        canvas ??= new DesignCanvasViewModel();
        if (canvas.Router.PathfindingGrid == null)
            canvas.InitializeAStarRouting(0, 0, ChipSize, ChipSize);

        var left = AddTerminal(canvas, "left", 40, 295, 0);
        var right = AddTerminal(canvas, "right", 550, 295, 180);
        var top = AddTerminal(canvas, "top", 295, 40, 90);
        var bottom = AddTerminal(canvas, "bottom", 295, 550, 270);

        var crossing = CrossingTestCircuit.CreateCrossingComponent();
        double half = CrossingTestCircuit.CrossingEdgeMicrometers / 2.0;
        crossing.PhysicalX = Center - half;
        crossing.PhysicalY = Center - half;
        crossing.IsInsertedCrossing = routerPlaced;
        canvas.AddComponent(crossing, "Crossing 4-Port");

        return new CrossedWiresScene(canvas, crossing,
            Connect(canvas, left, Port(crossing, "port 1")),
            Connect(canvas, Port(crossing, "port 2"), right),
            Connect(canvas, top, Port(crossing, "port 3")),
            Connect(canvas, Port(crossing, "port 4"), bottom));
    }

    public static PhysicalPin Port(Component crossing, string name) => crossing.PhysicalPins.Single(p => p.Name == name);

    /// <summary>Independent copies of a piece's segment end points, for exact comparisons.</summary>
    public static List<((double X, double Y) Start, (double X, double Y) End)> Geometry(WaveguideConnection piece) =>
        piece.GetPathSegments().Select(s => (s.StartPoint, s.EndPoint)).ToList();

    private static PhysicalPin AddTerminal(DesignCanvasViewModel canvas, string name, double x, double y, double angle)
    {
        var terminal = CrossingTestCircuit.CreateTerminal(name, x, y, angle);
        canvas.AddComponent(terminal.Component, "Terminal");
        return terminal.PhysicalPin;
    }

    private static WaveguideConnection Connect(DesignCanvasViewModel canvas, PhysicalPin from, PhysicalPin to)
    {
        var (x1, y1) = from.GetAbsolutePosition();
        var (x2, y2) = to.GetAbsolutePosition();
        double angle = Math.Atan2(y2 - y1, x2 - x1) * 180.0 / Math.PI;
        var route = new RoutedPath();
        route.Segments.Add(new StraightSegment(x1, y1, x2, y2, angle));
        return canvas.ConnectPinsWithCachedRoute(from, to, route)!.Connection;
    }
}

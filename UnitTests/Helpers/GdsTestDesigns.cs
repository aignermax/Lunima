using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.Export;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;

namespace UnitTests.Helpers;

/// <summary>
/// Provides reference designs for GDS ground-truth testing.
/// The designs mirror <c>scripts/generate_reference_nazca.py</c> exactly so
/// exported Nazca coordinates can be compared against a known-correct baseline.
///
/// Reference design layout:
///   Component 1: 100×50 µm box at physical (0, 0)
///   Component 2: 100×50 µm box at physical (300, 0)
///   Waveguide  : straight 200 µm from comp1.out → comp2.in
/// </summary>
public static class GdsTestDesigns
{
    /// <summary>
    /// Creates the reference design and returns it together with the expected
    /// physical (editor) coordinates, matching NazcaReferenceGenerator constants.
    /// </summary>
    /// <returns>
    /// A tuple of the populated canvas and a dictionary of ground-truth
    /// coordinate names mapped to (X, Y) physical µm positions.
    /// </returns>
    public static (DesignCanvasViewModel Canvas, Dictionary<string, (double X, double Y)> ExpectedCoords)
        CreateReferenceDesign()
    {
        var canvas = new DesignCanvasViewModel();

        var comp1 = CreateReferenceComponent("ref_comp_1",
            NazcaReferenceGenerator.Component1X,
            NazcaReferenceGenerator.Component1Y);

        var comp2 = CreateReferenceComponent("ref_comp_2",
            NazcaReferenceGenerator.Component2X,
            NazcaReferenceGenerator.Component2Y);

        canvas.AddComponent(comp1, "ReferenceComponent");
        canvas.AddComponent(comp2, "ReferenceComponent");

        var outPin = comp1.PhysicalPins.First(p => p.Name == "out");
        var inPin  = comp2.PhysicalPins.First(p => p.Name == "in");
        AddStraightConnection(canvas, outPin, inPin);

        var expectedCoords = new NazcaReferenceGenerator().GetExpectedCoordinates();
        return (canvas, expectedCoords);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a 100×50 µm reference component with 'out' and 'in' physical pins.
    /// Uses <c>NazcaReferenceGenerator</c> constants so dimensions stay in sync.
    /// </summary>
    private static Component CreateReferenceComponent(string identifier, double x, double y)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "reference_component",
            nazcaFunctionParams: string.Empty,
            parts: parts,
            typeNumber: 0,
            identifier: identifier,
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>()
        );

        component.PhysicalX          = x;
        component.PhysicalY          = y;
        component.WidthMicrometers   = NazcaReferenceGenerator.ComponentWidth;
        component.HeightMicrometers  = NazcaReferenceGenerator.ComponentHeight;
        component.NazcaOriginOffsetX = 0;
        component.NazcaOriginOffsetY = NazcaReferenceGenerator.ComponentHeight; // Y-flip offset

        component.PhysicalPins.Add(new PhysicalPin
        {
            Name               = "out",
            ParentComponent    = component,
            OffsetXMicrometers = NazcaReferenceGenerator.PinOffsetX,
            OffsetYMicrometers = NazcaReferenceGenerator.PinOffsetY,
            AngleDegrees       = 0
        });

        component.PhysicalPins.Add(new PhysicalPin
        {
            Name               = "in",
            ParentComponent    = component,
            OffsetXMicrometers = 0,
            OffsetYMicrometers = NazcaReferenceGenerator.PinOffsetY,
            AngleDegrees       = 180
        });

        return component;
    }

    /// <summary>
    /// Adds a straight-segment waveguide connection between two physical pins.
    /// Uses <c>ConnectPinsWithCachedRoute</c> so the connection appears in
    /// <c>canvas.Connections</c> and is exported by <c>SimpleNazcaExporter</c>.
    /// </summary>
    private static void AddStraightConnection(
        DesignCanvasViewModel canvas, PhysicalPin startPin, PhysicalPin endPin)
    {
        var (startX, startY) = startPin.GetAbsolutePosition();
        var (endX,   endY)   = endPin.GetAbsolutePosition();

        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(
            startX, startY, endX, endY, startPin.GetAbsoluteAngle()));

        canvas.ConnectPinsWithCachedRoute(startPin, endPin, path);
    }
}

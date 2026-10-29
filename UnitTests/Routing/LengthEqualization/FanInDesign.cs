using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using UnitTests.Helpers;

namespace UnitTests.Routing.LengthEqualization;

/// <summary>
/// Synthetic fibre-array fan-in: a column of couplers at the standard 127 µm fibre pitch on the
/// right, routed by the real router into a denser column of chip inputs on the left. The routes
/// differ in length by up to several hundred µm — the situation group length matching exists for.
/// </summary>
internal static class FanInDesign
{
    public const double FiberPitchMicrometers = 127.0;
    private const double ChipPitchMicrometers = 40.0;
    private const double ColumnGapMicrometers = 1500.0;
    private const double ChipOffsetYMicrometers = 200.0;

    /// <summary>Builds and routes <paramref name="count"/> fan-in connections on a fresh canvas.</summary>
    /// <param name="count">Number of channels.</param>
    /// <param name="reverseOdd">Start every odd connection at the coupler instead of the chip.</param>
    public static async Task<(DesignCanvasViewModel Canvas, List<WaveguideConnectionViewModel> Connections)> CreateAsync(
        int count, bool reverseOdd = false)
    {
        var canvas = new DesignCanvasViewModel();
        var connections = new List<WaveguideConnectionViewModel>();
        for (int i = 0; i < count; i++)
        {
            var chip = Place(canvas, 0, ChipOffsetYMicrometers + i * ChipPitchMicrometers);
            var coupler = Place(canvas, ColumnGapMicrometers, i * FiberPitchMicrometers);
            var chipPin = chip.PhysicalPins.First(p => p.Name == "out");
            var couplerPin = coupler.PhysicalPins.First(p => p.Name == "in");
            var connection = reverseOdd && i % 2 == 1
                ? canvas.ConnectPins(couplerPin, chipPin)
                : canvas.ConnectPins(chipPin, couplerPin);
            connections.Add(connection!);
        }
        await canvas.RecalculateRoutesAsync();
        return (canvas, connections);
    }

    private static Component Place(DesignCanvasViewModel canvas, double x, double y)
    {
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.PhysicalX = x;
        component.PhysicalY = y;
        canvas.AddComponent(component);
        return component;
    }
}

using CAP_Core;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using System.Numerics;

namespace UnitTests;

/// <summary>
/// Provides helper methods for creating test components with sliders.
/// </summary>
public static class TestComponentHelper
{
    /// <summary>
    /// Creates a 1x1 straight waveguide component with a single slider
    /// that controls coupling via a simple linear formula.
    /// </summary>
    public static Component CreateComponentWithSlider(
        double minValue = 0,
        double maxValue = 1,
        double initialValue = 0.5)
    {
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>
        {
            new("west0", 0, MatterType.Light, RectSide.Left),
            new("east0", 1, MatterType.Light, RectSide.Right)
        });

        var leftIn = parts[0, 0].GetPinAt(RectSide.Left).IDInFlow;
        var rightOut = parts[0, 0].GetPinAt(RectSide.Right).IDOutFlow;
        var rightIn = parts[0, 0].GetPinAt(RectSide.Right).IDInFlow;
        var leftOut = parts[0, 0].GetPinAt(RectSide.Left).IDOutFlow;

        var allPins = Component.GetAllPins(parts)
            .SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList();

        var slider = new Slider(Guid.NewGuid(), 0, initialValue, maxValue, minValue);
        var sliderTuples = new List<(Guid, double)> { (slider.ID, initialValue) };

        var matrixRed = new SMatrix(allPins, sliderTuples);
        matrixRed.SetValues(new Dictionary<(Guid, Guid), Complex>
        {
            { (leftIn, rightOut), initialValue },
            { (rightIn, leftOut), initialValue },
        });

        var connections = new Dictionary<int, SMatrix>
        {
            { StandardWaveLengths.RedNM, matrixRed },
            { StandardWaveLengths.GreenNM, matrixRed },
            { StandardWaveLengths.BlueNM, matrixRed },
        };

        return new Component(
            connections,
            new List<Slider> { slider },
            "placeCell_TestSlider",
            "",
            parts,
            0,
            "TestSlider",
            DiscreteRotation.R0);
    }
}

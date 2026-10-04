using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation;

/// <summary>
/// Sub-nm sweep path of <see cref="SystemMatrixBuilder"/> (#1349): the double
/// overload must interpolate component S-matrices between their integer-nm stops,
/// and an integral double must reproduce the integer overload bit-for-bit so
/// all-integer sweeps (and the regression goldens) are unchanged.
/// </summary>
public class SystemMatrixBuilderSubNmTests
{
    private const double LowStopTransmission = 0.2;
    private const double HighStopTransmission = 0.8;

    private static SMatrix CreateTwoPinMatrix(Guid pinIn, Guid pinOut, double transmission)
    {
        var matrix = new SMatrix(new List<Guid> { pinIn, pinOut }, new List<(Guid, double)>());
        matrix.SMat[0, 1] = new Complex(transmission, 0);
        matrix.SMat[1, 0] = new Complex(transmission, 0);
        return matrix;
    }

    private static (SystemMatrixBuilder Builder, Guid PinIn, Guid PinOut) CreateGrid()
    {
        var pinIn = new Pin("in", 0, MatterType.Light, RectSide.Left);
        var pinOut = new Pin("out", 1, MatterType.Light, RectSide.Right);
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin> { pinIn, pinOut });

        var wavelengthMap = new Dictionary<int, SMatrix>
        {
            { 1550, CreateTwoPinMatrix(pinIn.IDInFlow, pinOut.IDOutFlow, LowStopTransmission) },
            { 1560, CreateTwoPinMatrix(pinIn.IDInFlow, pinOut.IDOutFlow, HighStopTransmission) },
        };

        var physicalPins = new List<PhysicalPin>
        {
            new() { Name = "in", OffsetXMicrometers = 0, OffsetYMicrometers = 5, AngleDegrees = 180, LogicalPin = pinIn },
            new() { Name = "out", OffsetXMicrometers = 30, OffsetYMicrometers = 5, AngleDegrees = 0, LogicalPin = pinOut },
        };
        var component = new Component(
            wavelengthMap, new List<Slider>(), "dut", "", parts, 0, "Dut_1",
            DiscreteRotation.R0, physicalPins);

        var tileManager = new ComponentListTileManager();
        tileManager.AddComponent(component);

        var portManager = new PhysicalExternalPortManager();
        portManager.AddLightSource(
            new ExternalInput("src", LaserType.Red, 0, new Complex(1.0, 0)), pinIn.IDInFlow);

        var grid = GridManager.CreateForSimulation(
            tileManager, new WaveguideConnectionManager(new WaveguideRouter()), portManager);
        return (new SystemMatrixBuilder(grid), pinIn.IDInFlow, pinOut.IDOutFlow);
    }

    [Fact]
    public void GetSystemSMatrix_FractionalWavelength_InterpolatesComponentMatrix()
    {
        var (builder, pinIn, pinOut) = CreateGrid();

        var matrix = builder.GetSystemSMatrix(1555.5);

        // t = (1555.5 - 1550) / (1560 - 1550) = 0.55 → 0.2 + 0.55 · (0.8 - 0.2) = 0.53
        double expected = LowStopTransmission + 0.55 * (HighStopTransmission - LowStopTransmission);
        matrix.GetNonNullValues()[(pinOut, pinIn)].Real.ShouldBe(expected, 1e-12,
            "the component S-matrix must be linearly interpolated at the exact sub-nm wavelength");
    }

    [Fact]
    public void GetSystemSMatrix_IntegralDouble_IsBitIdenticalToIntegerOverload()
    {
        var (builder, _, _) = CreateGrid();

        // Both an exact stop (1550) and an interpolated integral target (1555)
        // must be unaffected by routing the call through the double overload.
        foreach (double wl in new[] { 1550.0, 1555.0 })
        {
            var viaInt = builder.GetSystemSMatrix((int)wl).GetNonNullValues();
            var viaDouble = builder.GetSystemSMatrix(wl).GetNonNullValues();

            viaDouble.Keys.ShouldBe(viaInt.Keys);
            foreach (var key in viaInt.Keys)
                viaDouble[key].ShouldBe(viaInt[key],
                    $"the double overload must be bit-identical at {wl} nm");
        }
    }

    [Fact]
    public void GetSystemSMatrix_FractionalWavelength_DiffersFromRoundedIntegerGrid()
    {
        var (builder, pinIn, pinOut) = CreateGrid();

        var atFraction = builder.GetSystemSMatrix(1555.5).GetNonNullValues();
        var atRounded = builder.GetSystemSMatrix(1555).GetNonNullValues();
        var atRoundedUp = builder.GetSystemSMatrix(1556).GetNonNullValues();

        atFraction[(pinOut, pinIn)].ShouldNotBe(atRounded[(pinOut, pinIn)],
            "rounding the sweep grid to integer nm loses the sub-nm sample — the defect this fixes");
        atFraction[(pinOut, pinIn)].ShouldNotBe(atRoundedUp[(pinOut, pinIn)]);
    }
}

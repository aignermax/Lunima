using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.Routing;
using Moq;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Model-level tests for the coherent propagation phase of <see cref="FrozenWaveguidePath"/>
/// (issue #1343): a frozen group-internal path must accumulate the same
/// exp(-i·2π·n_eff(λ)·L/λ) phase as a routed connection, taking n_eff(λ) from the
/// captured dispersion model, falling back to the endpoint component's PDK dispersion
/// (the state after a .lun round-trip, which does not persist the captured model) and
/// finally to <see cref="WaveguideConnection.DefaultEffectiveIndex"/> — exactly like
/// routed connections do.
/// </summary>
public class FrozenWaveguidePathCoherentPhaseTests
{
    private const double PathLengthMicrometers = 100.0;
    private const double WavelengthNm = 1550.0;
    private const double ModelIndex = 3.1;
    private const double Tolerance = 1e-9;

    [Fact]
    public void GetCoherentTransmission_WithoutDispersion_UsesDefaultEffectiveIndex()
    {
        var frozenPath = CreateFrozenPath();

        var coherent = frozenPath.GetCoherentTransmission(WavelengthNm);

        AssertPhaseAndMagnitude(frozenPath, coherent, WaveguideConnection.DefaultEffectiveIndex);
    }

    [Fact]
    public void GetCoherentTransmission_WithCapturedDispersion_UsesModelIndex()
    {
        var frozenPath = CreateFrozenPath();
        frozenPath.DispersionModel = CreateDispersionModel(ModelIndex);

        var coherent = frozenPath.GetCoherentTransmission(WavelengthNm);

        AssertPhaseAndMagnitude(frozenPath, coherent, ModelIndex);
    }

    [Fact]
    public void GetEffectiveIndex_AfterRoundTrip_FallsBackToEndpointComponentDispersion()
    {
        // A .lun round-trip does not persist the captured DispersionModel; the frozen
        // path must then resolve n_eff from its start pin's component — the same PDK
        // dispersion a freshly routed connection would inherit.
        var component = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        component.WaveguideDispersion = CreateDispersionModel(ModelIndex);
        var frozenPath = CreateFrozenPath();
        frozenPath.StartPin = component.PhysicalPins[0];

        frozenPath.GetEffectiveIndex(WavelengthNm).ShouldBe(ModelIndex);
    }

    [Fact]
    public void GetEffectiveIndex_WithoutAnyDispersion_FallsBackToDefaultIndex()
    {
        var frozenPath = CreateFrozenPath();

        frozenPath.GetEffectiveIndex(WavelengthNm)
            .ShouldBe(WaveguideConnection.DefaultEffectiveIndex);
    }

    [Fact]
    public void GetCoherentTransmission_EmptyPath_ReturnsOne()
    {
        var frozenPath = new FrozenWaveguidePath { Path = new RoutedPath() };

        frozenPath.GetCoherentTransmission(WavelengthNm).ShouldBe(System.Numerics.Complex.One);
    }

    [Fact]
    public void TransmissionCoefficient_WithBend_IncludesBendLossLikeTheConnectionDid()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 50, 0, 0));
        path.Segments.Add(new BendSegment(centerX: 50, centerY: 10, radius: 10, startAngle: 180, sweepAngle: 90));
        var frozenPath = new FrozenWaveguidePath { Path = path };

        // Radius model: radiation of one 90° arc at R = 10 µm plus its two junctions
        // (straight→arc, arc→the end component's straight port).
        double expectedLossDb = frozenPath.PropagationLossDbPerCm
            * (path.TotalLengthMicrometers / 10_000.0)
            + CAP_Core.LightCalculation.BendLoss.BendLossPresets.Generic.CornerLossDb(10, 90);
        double expectedAmplitude = Math.Pow(10.0, -expectedLossDb / 20.0);

        frozenPath.TransmissionCoefficient.Real.ShouldBe(expectedAmplitude, Tolerance);
        frozenPath.TransmissionCoefficient.Imaginary.ShouldBe(0.0);

        var connection = new CAP_Core.Components.Connections.WaveguideConnection();
        connection.RestoreCachedPath(path);
        frozenPath.TransmissionCoefficient.Real.ShouldBe(connection.TransmissionCoefficient.Real, Tolerance,
            "freezing a connection must not change its loss model");
    }

    private static FrozenWaveguidePath CreateFrozenPath()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, PathLengthMicrometers, 0, 0));
        return new FrozenWaveguidePath { Path = path };
    }

    private static IDispersionModel CreateDispersionModel(double effectiveIndex)
    {
        var model = new Mock<IDispersionModel>();
        model.Setup(m => m.NEffAt(It.IsAny<double>())).Returns(effectiveIndex);
        return model.Object;
    }

    private static void AssertPhaseAndMagnitude(
        FrozenWaveguidePath frozenPath, System.Numerics.Complex coherent, double expectedIndex)
    {
        coherent.Magnitude.ShouldBe(frozenPath.TransmissionCoefficient.Magnitude, Tolerance);

        double expectedPhase = -2.0 * Math.PI * expectedIndex
            * PathLengthMicrometers / (WavelengthNm / 1000.0);
        double normalizedPhase = Math.Atan2(Math.Sin(expectedPhase), Math.Cos(expectedPhase));
        coherent.Phase.ShouldBe(normalizedPhase, Tolerance);
    }
}

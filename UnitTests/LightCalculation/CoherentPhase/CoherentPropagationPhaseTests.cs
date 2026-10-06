using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation.CoherentPhase;

/// <summary>
/// Tests for the opt-in coherent propagation-phase mode (issue #1319): a routed
/// waveguide connection can carry t = a·exp(-i·2π·n_eff(λ)·L/λ) instead of the
/// loss-only real amplitude. Default mode stays real-valued; the coherent value
/// keeps the loss-only magnitude and adds exactly the propagation phase.
/// </summary>
public class CoherentPropagationPhaseTests
{
    private const double WavelengthNm = 1550.0;
    private const double TestNEff = 2.4;

    [Fact]
    public void GetCoherentTransmission_MagnitudeEqualsLossOnlyAmplitude()
    {
        var connection = CreateRoutedConnection();

        var coherent = connection.GetCoherentTransmission(WavelengthNm);

        coherent.Magnitude.ShouldBe(connection.TransmissionCoefficient.Magnitude, 1e-9);
    }

    [Fact]
    public void GetCoherentTransmission_PhaseMatchesPropagationFormula()
    {
        var connection = CreateRoutedConnection();
        connection.DispersionModel = new ConstantDispersion(nEff: TestNEff);

        var coherent = connection.GetCoherentTransmission(WavelengthNm);

        double expectedPhase = ExpectedPhaseRadians(TestNEff, connection.PathLengthMicrometers, WavelengthNm);
        PhaseDifferenceRadians(coherent.Phase, expectedPhase).ShouldBe(0, 1e-9);
    }

    [Fact]
    public void GetCoherentTransmission_NoDispersionModel_UsesDefaultEffectiveIndex()
    {
        var connection = CreateRoutedConnection();
        connection.DispersionModel.ShouldBeNull();

        var coherent = connection.GetCoherentTransmission(WavelengthNm);

        double expectedPhase = ExpectedPhaseRadians(
            WaveguideConnection.DefaultEffectiveIndex, connection.PathLengthMicrometers, WavelengthNm);
        PhaseDifferenceRadians(coherent.Phase, expectedPhase).ShouldBe(0, 1e-9);
    }

    [Fact]
    public void GetCoherentTransmission_PhaseVariesWithWavelength()
    {
        var connection = CreateRoutedConnection();

        var at1500 = connection.GetCoherentTransmission(1500.0);
        var at1600 = connection.GetCoherentTransmission(1600.0);

        Math.Abs(at1500.Phase - at1600.Phase).ShouldBeGreaterThan(1e-6);
    }

    [Fact]
    public void GetConnectionTransfers_DefaultMode_TransfersStayRealValued()
    {
        var (manager, connection) = CreateManagerWithConnection();

        manager.EnableCoherentPropagationPhase.ShouldBeFalse("the coherent mode is opt-in");
        var transfers = manager.GetConnectionTransfers(wavelengthNm: WavelengthNm);

        transfers.Values.ShouldAllBe(t => t.Imaginary == 0);
        transfers.Values.ShouldAllBe(t => t == connection.TransmissionCoefficient);
    }

    [Fact]
    public void GetConnectionTransfers_CoherentMode_AppliesPropagationPhase()
    {
        var (manager, connection) = CreateManagerWithConnection();
        manager.EnableCoherentPropagationPhase = true;

        var transfers = manager.GetConnectionTransfers(wavelengthNm: WavelengthNm);

        var expected = connection.GetCoherentTransmission(WavelengthNm);
        transfers.Values.ShouldAllBe(t => t == expected);
        transfers.Values.ShouldAllBe(t => t.Imaginary != 0,
            "a routed connection of non-trivial length must carry a phase");
    }

    [Fact]
    public void GetConnectionTransfers_CoherentMode_SkipsElectricalConnections()
    {
        var (manager, connection) = CreateManagerWithConnection(MatterType.Electricity);
        manager.EnableCoherentPropagationPhase = true;

        var transfers = manager.GetConnectionTransfers(wavelengthNm: WavelengthNm);

        connection.IsElectrical.ShouldBeTrue();
        transfers.Values.ShouldAllBe(t => t.Imaginary == 0,
            "metal traces must not pick up a waveguide propagation phase");
    }

    private static double ExpectedPhaseRadians(double nEff, double lengthMicrometers, double wavelengthNm) =>
        -2.0 * Math.PI * nEff * lengthMicrometers / (wavelengthNm / 1000.0);

    private static double PhaseDifferenceRadians(double actual, double expected)
    {
        double difference = (actual - expected) % (2.0 * Math.PI);
        if (difference > Math.PI) difference -= 2.0 * Math.PI;
        if (difference < -Math.PI) difference += 2.0 * Math.PI;
        return Math.Abs(difference);
    }

    private static (WaveguideConnectionManager Manager, WaveguideConnection Connection)
        CreateManagerWithConnection(MatterType matterType = MatterType.Light)
    {
        var connection = CreateRoutedConnection(matterType);
        var manager = new WaveguideConnectionManager(new WaveguideRouter());
        manager.AddExistingConnection(connection);
        return (manager, connection);
    }

    private static WaveguideConnection CreateRoutedConnection(MatterType matterType = MatterType.Light)
    {
        var startComponent = CreateTestComponent(0, 0, out var startPin, matterType);
        var endComponent = CreateTestComponent(100, 0, out var endPin, matterType);
        var connection = new WaveguideConnection
        {
            StartPin = startPin,
            EndPin = endPin,
            PropagationLossDbPerCm = 2.0,
            BendLossDbPer90Deg = 0.05
        };
        connection.RecalculateTransmission(new WaveguideRouter());
        connection.PathLengthMicrometers.ShouldBeGreaterThan(0);
        return connection;
    }

    private static Component CreateTestComponent(
        double x, double y, out PhysicalPin physicalPin, MatterType matterType)
    {
        bool isOutput = x == 0;
        var logicalPin = new Pin(
            isOutput ? "output" : "input", 0, matterType,
            isOutput ? RectSide.Right : RectSide.Left);
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin> { logicalPin });

        physicalPin = new PhysicalPin
        {
            Name = logicalPin.Name,
            OffsetXMicrometers = isOutput ? 50 : 0,
            OffsetYMicrometers = 25,
            AngleDegrees = isOutput ? 0 : 180,
            LogicalPin = logicalPin
        };

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "test",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: $"TestComponent_{x}_{y}",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin> { physicalPin });
        component.WidthMicrometers = 50;
        component.HeightMicrometers = 50;
        component.PhysicalX = x;
        component.PhysicalY = y;
        return component;
    }
}

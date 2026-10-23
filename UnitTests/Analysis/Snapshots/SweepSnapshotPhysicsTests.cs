using System.Numerics;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.MaterialDispersion;
using CAP_Core.Routing;
using Shouldly;
using static UnitTests.Simulation.Snapshots.SnapshotPhysicsOracle;

namespace UnitTests.Analysis.Snapshots;

public sealed class SweepSnapshotPhysicsTests
{
    [Theory]
    [InlineData("constant")]
    [InlineData("polynomial")]
    [InlineData("table")]
    public async Task SubNanometerInterference_PreservesLegacyMaterialAndPhaseEvaluation(string kind)
    {
        var split = CreateComponent(3, (0, 1, Math.Sqrt(0.5)), (0, 2, Math.Sqrt(0.5)));
        var combine = CreateComponent(3, (1, 0, Math.Sqrt(0.5)), (2, 0, Math.Sqrt(0.5)));
        IDispersionModel material = kind switch
        {
            "polynomial" => new PolynomialDispersion(1550, 2.4, -0.001, 0.000002),
            "table" => new TabulatedDispersion([(1548, 2.402), (1550, 2.4), (1552, 2.395)], null, [(1550, 0.5)]),
            _ => new ConstantDispersion()
        };
        var shortArm = Wire(split, 1, combine, 1, 100);
        var longArm = Wire(split, 2, combine, 2, 147);
        shortArm.DispersionModel = longArm.DispersionModel = material;
        var tiles = new ComponentListTileManager();
        tiles.AddComponent(split);
        tiles.AddComponent(combine);
        var wires = new WaveguideConnectionManager(new WaveguideRouter()) { EnableCoherentPropagationPhase = true };
        wires.AddExistingConnection(shortArm);
        wires.AddExistingConnection(longArm);
        var ports = new PhysicalExternalPortManager();
        ports.AddLightSource(new ExternalInput("source", LaserType.Red, 0, Complex.One), split.PhysicalPins[0].LogicalPin.IDInFlow);
        var grid = GridManager.CreateForSimulation(tiles, wires, ports);
        var config = new WavelengthSweepConfiguration(1549, 1551, 9);
        var expected = await new WavelengthSweeper(new SystemMatrixBuilder(grid), ports)
            .RunSweepAsync(config, grid).WaitAsync(TimeSpan.FromSeconds(10));
        var snapshot = WavelengthSweepSnapshot.Capture(config, grid, ports);
        var actual = await snapshot.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10));
        SweepSnapshotFixture.AssertEquivalent(actual, expected);
        actual.GetWavelengthValues()[1].ShouldBe(1549.25);
        var output = combine.PhysicalPins[0].LogicalPin.IDOutFlow;
        Math.Abs(actual.DataPoints[0].OutputPowers[output] - actual.DataPoints[^1].OutputPowers[output])
            .ShouldBeGreaterThan(1e-6, "the oracle exercises interference, not a phase-insensitive single path");
    }

    [Fact]
    public async Task PreCanceledSnapshot_DoesNotReturnACompletedEmptySweep()
    {
        var snapshot = new SweepSnapshotFixture().Capture();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => snapshot.SolveAsync(stop.Token));
    }

    [Fact]
    public async Task LegacyTileConnectivity_UsesCapturedAdjacentPinTransfers()
    {
        var fixture = new SweepSnapshotFixture();
        var grid = new GridManager(4, 4);
        grid.ComponentMover.PlaceComponent(0, 0, fixture.First);
        grid.ComponentMover.PlaceComponent(1, 0, fixture.Second);
        var snapshot = WavelengthSweepSnapshot.Capture(fixture.Configuration, grid, fixture.Ports);
        var expected = await new WavelengthSweeper(new SystemMatrixBuilder(grid), fixture.Ports)
            .RunSweepAsync(fixture.Configuration, grid).WaitAsync(TimeSpan.FromSeconds(10));
        expected.DataPoints[0].OutputPowers[fixture.Output].ShouldBeGreaterThan(0.5);
        grid.ComponentMover.UnregisterComponentAt(1, 0);
        var actual = await snapshot.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10));
        SweepSnapshotFixture.AssertEquivalent(actual, expected);
        actual.MonitoredPinIds.ShouldBe(expected.MonitoredPinIds, "legacy CSV column order must remain stable");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CrossDieFacetGeometryAndModeSizes_AreCapturedWithoutAddingWaveguidePhase(bool facing)
    {
        var first = CreateComponent(2, (0, 1, Complex.One));
        var second = CreateComponent(2, (0, 1, Complex.One));
        first.TemplateName = second.TemplateName = "Edge Coupler";
        first.ModeFieldDiameterMicrometers = 3;
        second.ModeFieldDiameterMicrometers = 4;
        first.PhysicalPins[1].AngleDegrees = 0;
        second.PhysicalPins[0].AngleDegrees = facing ? 180 : 0;
        var firstDie = new ComponentGroup("A") { Die = new DieDefinition(500, 500) };
        var secondDie = new ComponentGroup("B") { Die = new DieDefinition(500, 500) };
        firstDie.AddChild(first);
        secondDie.AddChild(second);
        secondDie.MoveGroup(5, 1);
        var wire = Wire(first, 1, second, 0, 50);
        wire.FacetGapMediumIndex = 1.5;
        wire.IsCrossChipletFacetLink.ShouldBeTrue();
        var tiles = new ComponentListTileManager();
        tiles.AddComponent(firstDie);
        tiles.AddComponent(secondDie);
        var wires = new WaveguideConnectionManager(new WaveguideRouter()) { EnableCoherentPropagationPhase = true };
        wires.AddExistingConnection(wire);
        var ports = new PhysicalExternalPortManager();
        ports.AddLightSource(new ExternalInput("source", LaserType.Red, 0, Complex.One), first.PhysicalPins[0].LogicalPin.IDInFlow);
        var grid = GridManager.CreateForSimulation(tiles, wires, ports);
        var configuration = new WavelengthSweepConfiguration(1549, 1551, 5);
        var snapshot = WavelengthSweepSnapshot.Capture(configuration, grid, ports);
        var expected = await new WavelengthSweeper(new SystemMatrixBuilder(grid), ports)
            .RunSweepAsync(configuration, grid).WaitAsync(TimeSpan.FromSeconds(10));
        secondDie.MoveGroup(200, 200);
        first.ModeFieldDiameterMicrometers = 20;
        wire.FacetGapMediumIndex = 2;
        var actual = await snapshot.SolveAsync().WaitAsync(TimeSpan.FromSeconds(10));
        SweepSnapshotFixture.AssertEquivalent(actual, expected);
        var outputPower = actual.DataPoints[0].OutputPowers[second.PhysicalPins[1].LogicalPin.IDOutFlow];
        if (facing) outputPower.ShouldBeInRange(0.01, 0.99);
        else outputPower.ShouldBe(0);
    }
}

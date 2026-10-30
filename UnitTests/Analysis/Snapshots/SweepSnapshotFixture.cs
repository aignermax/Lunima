using System.Numerics;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;

namespace UnitTests.Analysis.Snapshots;

internal sealed class SweepSnapshotFixture
{
    internal WavelengthSweepConfiguration Configuration { get; } = new(1549, 1551, 5);
    internal Component First { get; } = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
    internal Component Second { get; } = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
    internal WaveguideConnection Wire { get; }
    internal ComponentListTileManager Tiles { get; } = new();
    internal PhysicalExternalPortManager Ports { get; } = new();
    internal ExternalInput Source { get; }
    internal GridManager Grid { get; }
    internal Guid Input => First.PhysicalPins[0].LogicalPin.IDInFlow;
    internal Guid Output => Second.PhysicalPins[1].LogicalPin.IDOutFlow;

    internal SweepSnapshotFixture(bool coherent = false)
    {
        Tiles.AddComponent(First);
        Tiles.AddComponent(Second);
        Wire = new WaveguideConnection { StartPin = First.PhysicalPins[1], EndPin = Second.PhysicalPins[0] };
        Wire.RestoreCachedPath(Path(37));
        var manager = new WaveguideConnectionManager(new WaveguideRouter())
            { EnableCoherentPropagationPhase = coherent };
        manager.AddExistingConnection(Wire);
        Source = new ExternalInput("source", LaserType.Red, 0, new Complex(0.64, 0));
        Ports.AddLightSource(Source, Input);
        Grid = GridManager.CreateForSimulation(Tiles, manager, Ports);
    }

    internal WavelengthSweepSnapshot Capture() => WavelengthSweepSnapshot.Capture(Configuration, Grid, Ports);
    internal Task<WavelengthSweepResult> LegacyAsync() =>
        new WavelengthSweeper(new SystemMatrixBuilder(Grid), Ports)
            .RunSweepAsync(Configuration, Grid).WaitAsync(TimeSpan.FromSeconds(10));

    internal static RoutedPath Path(double length)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, length, 0, 0));
        return path;
    }

    internal static void AssertEquivalent(WavelengthSweepResult actual, WavelengthSweepResult expected)
    {
        actual.GetWavelengthValues().ShouldBe(expected.GetWavelengthValues());
        actual.Warnings.ShouldBe(expected.Warnings);
        actual.MonitoredPinIds.OrderBy(id => id).ShouldBe(expected.MonitoredPinIds.OrderBy(id => id));
        for (var index = 0; index < expected.DataPoints.Count; index++)
        {
            var point = actual.DataPoints[index];
            var oracle = expected.DataPoints[index];
            point.OutputPowers.Keys.OrderBy(id => id).ShouldBe(oracle.OutputPowers.Keys.OrderBy(id => id));
            foreach (var (pin, power) in oracle.OutputPowers)
            {
                point.OutputPowers[pin].ShouldBe(power, 1e-9);
                point.InsertionLossDb[pin].ShouldBe(oracle.InsertionLossDb[pin], 1e-8);
            }
        }
    }
}

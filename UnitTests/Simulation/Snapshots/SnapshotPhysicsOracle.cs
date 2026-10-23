using System.Numerics;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;
using CAP_Core.Routing;
using CAP_Core.Tiles;
using Shouldly;

namespace UnitTests.Simulation.Snapshots;

internal static class SnapshotPhysicsOracle
{
    internal static async Task<SimulationSnapshotResult> CompareAsync(Component[] components,
        WaveguideConnection[] connections, Pin source, bool coherent = false, Complex? field = null)
    {
        var inputField = field ?? Complex.One;
        // Capture first: the legacy builder is allowed to populate group matrix caches.
        var snapshot = SimulationSnapshotCapture.Capture(components, connections,
            [new(source.IDInFlow, 1550, inputField)], coherent);
        var tiles = new ComponentListTileManager();
        foreach (var component in components) tiles.AddComponent(component);
        var manager = new WaveguideConnectionManager(new WaveguideRouter())
            { EnableCoherentPropagationPhase = coherent };
        foreach (var connection in connections) manager.AddExistingConnection(connection);
        var ports = new PhysicalExternalPortManager();
        ports.AddLightSource(new ExternalInput("oracle", LaserType.Red, 0,
            Complex.FromPolarCoordinates(inputField.Magnitude * inputField.Magnitude, inputField.Phase)), source.IDInFlow);
        var grid = GridManager.CreateForSimulation(tiles, manager, ports);
        var calculator = new GridLightCalculator(new SystemMatrixBuilder(grid), grid);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var expected = await calculator.CalculateFieldPropagationAsync(cancellation, 1550)
            .WaitAsync(TimeSpan.FromSeconds(12));
        cancellation.Token.ThrowIfCancellationRequested();
        var actual = await SimulationSnapshotSolver.RunAsync(snapshot, cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(12));
        actual.Fields.Keys.OrderBy(id => id).ShouldBe(expected.Keys.OrderBy(id => id));
        foreach (var (id, value) in expected)
            (actual.Fields[id] - value).Magnitude.ShouldBeLessThan(1e-9, $"field mismatch at {id}");
        return actual;
    }

    internal static Component CreateComponent(int count, params (int From, int To, Complex Value)[] transfers)
    {
        var pins = Enumerable.Range(0, count).Select(index =>
            new Pin($"p{index}", index, MatterType.Light, index == 0 ? RectSide.Left : RectSide.Right)).ToList();
        var matrix = new SMatrix(pins.SelectMany(pin => new[] { pin.IDInFlow, pin.IDOutFlow }).ToList(), new());
        matrix.SetValues(transfers.ToDictionary(value =>
            (pins[value.From].IDInFlow, pins[value.To].IDOutFlow), value => value.Value));
        var parts = new Part[1, 1];
        parts[0, 0] = new Part(pins);
        var physical = pins.Select(pin => new PhysicalPin { Name = pin.Name, LogicalPin = pin }).ToList();
        return new Component(new() { [1550] = matrix }, [], "fixture", "", parts, 0,
            "physics-oracle", DiscreteRotation.R0, physical);
    }

    internal static WaveguideConnection Wire(Component from, int fromPin, Component to, int toPin, double length = 0)
    {
        var wire = new WaveguideConnection { StartPin = from.PhysicalPins[fromPin], EndPin = to.PhysicalPins[toPin] };
        if (length > 0) wire.RestoreCachedPath(Path(length));
        return wire;
    }

    internal static RoutedPath Path(double length)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, length, 0, 0));
        return path;
    }
}

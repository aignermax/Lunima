using System.Numerics;
using CAP_Core.Analysis;
using CAP_Core.LightCalculation;
using MathNet.Numerics.LinearAlgebra;
using Shouldly;

namespace UnitTests.Analysis;

public class StochasticSimulatorTests
{
    [Fact]
    public async Task RunAsync_SimpleSystem_ReturnsCorrectIterationCount()
    {
        var (matrix, pinIds) = CreatePassthroughSystem();
        var inputVector = CreateInputVector(matrix, pinIds[0]);
        var simulator = new StochasticSimulator(new SMatrixParameterPerturber());
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.05)
        };

        var result = await simulator.RunAsync(
            matrix, inputVector, variations, iterationCount: 20, seed: 42);

        result.IterationCount.ShouldBe(20);
    }

    [Fact]
    public async Task RunAsync_SimpleSystem_ReturnsStatisticsForAllPins()
    {
        var (matrix, pinIds) = CreatePassthroughSystem();
        var inputVector = CreateInputVector(matrix, pinIds[0]);
        var simulator = new StochasticSimulator(new SMatrixParameterPerturber());
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.05)
        };

        var result = await simulator.RunAsync(
            matrix, inputVector, variations, iterationCount: 10, seed: 42);

        result.PinStatistics.Count.ShouldBe(pinIds.Count);
        foreach (var stats in result.PinStatistics)
        {
            stats.Samples.Count.ShouldBe(10);
        }
    }

    [Fact]
    public async Task RunAsync_WithSeed_IsReproducible()
    {
        var (matrix, pinIds) = CreatePassthroughSystem();
        var inputVector = CreateInputVector(matrix, pinIds[0]);
        var simulator = new StochasticSimulator(new SMatrixParameterPerturber());
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.1)
        };

        var result1 = await simulator.RunAsync(
            matrix, inputVector, variations, iterationCount: 10, seed: 123);
        var result2 = await simulator.RunAsync(
            matrix, inputVector, variations, iterationCount: 10, seed: 123);

        for (int i = 0; i < result1.PinStatistics.Count; i++)
        {
            result1.PinStatistics[i].MeanPower.ShouldBe(
                result2.PinStatistics[i].MeanPower, tolerance: 1e-10);
        }
    }

    [Fact]
    public async Task RunAsync_ZeroVariation_AllSamplesIdentical()
    {
        var (matrix, pinIds) = CreatePassthroughSystem();
        var inputVector = CreateInputVector(matrix, pinIds[0]);
        var simulator = new StochasticSimulator(new SMatrixParameterPerturber());
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.0)
        };

        var result = await simulator.RunAsync(
            matrix, inputVector, variations, iterationCount: 5, seed: 42);

        var outputStats = result.PinStatistics.First(
            s => s.PinId == pinIds[1]);
        outputStats.StdDeviation.ShouldBe(0.0, tolerance: 1e-10);
    }

    [Fact]
    public async Task RunAsync_ZeroIterations_ThrowsArgumentOutOfRange()
    {
        var (matrix, pinIds) = CreatePassthroughSystem();
        var inputVector = CreateInputVector(matrix, pinIds[0]);
        var simulator = new StochasticSimulator(new SMatrixParameterPerturber());

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            () => simulator.RunAsync(
                matrix, inputVector, new List<ParameterVariation>(), 0));
    }

    [Fact]
    public async Task RunAsync_LargeVariation_OutputPowerVaries()
    {
        var (matrix, pinIds) = CreatePassthroughSystem();
        var inputVector = CreateInputVector(matrix, pinIds[0]);
        var simulator = new StochasticSimulator(new SMatrixParameterPerturber());
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.3)
        };

        var result = await simulator.RunAsync(
            matrix, inputVector, variations, iterationCount: 50, seed: 42);

        var outputStats = result.PinStatistics.First(
            s => s.PinId == pinIds[1]);
        outputStats.StdDeviation.ShouldBeGreaterThan(0.0);
    }

    [Fact]
    public async Task RunAsync_GetHistogram_ReturnsBuckets()
    {
        var (matrix, pinIds) = CreatePassthroughSystem();
        var inputVector = CreateInputVector(matrix, pinIds[0]);
        var simulator = new StochasticSimulator(new SMatrixParameterPerturber());
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.1)
        };

        var result = await simulator.RunAsync(
            matrix, inputVector, variations, iterationCount: 50, seed: 42);

        var histogram = result.GetHistogram(pinIds[1], bucketCount: 5);
        histogram.Count.ShouldBeGreaterThan(0);
        histogram.Sum(b => b.Count).ShouldBe(50);
    }

    private static (SMatrix Matrix, List<Guid> PinIds) CreatePassthroughSystem()
    {
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());
        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[1]), new Complex(0.9, 0) },
            { (pinIds[1], pinIds[0]), new Complex(0.9, 0) }
        };
        matrix.SetValues(transfers);
        return (matrix, pinIds);
    }

    private static Vector<Complex> CreateInputVector(
        SMatrix matrix, Guid inputPinId)
    {
        var vector = Vector<Complex>.Build.Dense(matrix.PinReference.Count);
        vector[matrix.PinReference[inputPinId]] = new Complex(1.0, 0);
        return vector;
    }
}

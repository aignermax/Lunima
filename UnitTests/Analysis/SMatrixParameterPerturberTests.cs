using System.Numerics;
using CAP_Core.Analysis;
using CAP_Core.LightCalculation;
using Shouldly;

namespace UnitTests.Analysis;

public class SMatrixParameterPerturberTests
{
    private readonly SMatrixParameterPerturber _perturber = new();

    [Fact]
    public void Perturb_ZeroVariation_PreservesOriginalValues()
    {
        var (matrix, pinIds) = CreateSimpleMatrix(0.5);
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.0)
        };

        var perturbed = _perturber.Perturb(matrix, variations, new Random(42));

        var values = perturbed.GetNonNullValues();
        values.Count.ShouldBe(2);
        foreach (var v in values.Values)
        {
            v.Magnitude.ShouldBe(0.5, tolerance: 1e-10);
        }
    }

    [Fact]
    public void Perturb_CouplingVariation_ChangesMagnitude()
    {
        var (matrix, pinIds) = CreateSimpleMatrix(0.7);
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.1)
        };

        var perturbed = _perturber.Perturb(matrix, variations, new Random(42));

        var values = perturbed.GetNonNullValues();
        bool anyChanged = values.Values.Any(v => Math.Abs(v.Magnitude - 0.7) > 1e-10);
        anyChanged.ShouldBeTrue("Coupling perturbation should change magnitudes");
    }

    [Fact]
    public void Perturb_PhaseVariation_ChangesPhase()
    {
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());
        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[1]), Complex.FromPolarCoordinates(0.8, Math.PI / 4) },
            { (pinIds[1], pinIds[0]), Complex.FromPolarCoordinates(0.8, Math.PI / 4) }
        };
        matrix.SetValues(transfers);

        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Phase, 0.1)
        };

        var perturbed = _perturber.Perturb(matrix, variations, new Random(42));

        var values = perturbed.GetNonNullValues();
        bool anyPhaseChanged = values.Values
            .Any(v => Math.Abs(v.Phase - Math.PI / 4) > 1e-10);
        anyPhaseChanged.ShouldBeTrue("Phase perturbation should change phases");
    }

    [Fact]
    public void Perturb_ClampsMagnitudeToUnitRange()
    {
        var (matrix, pinIds) = CreateSimpleMatrix(0.99);
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.5)
        };

        // Run many perturbations to increase chance of exceeding 1.0
        for (int i = 0; i < 100; i++)
        {
            var perturbed = _perturber.Perturb(matrix, variations, new Random(i));
            var values = perturbed.GetNonNullValues();
            foreach (var v in values.Values)
            {
                v.Magnitude.ShouldBeLessThanOrEqualTo(1.0);
                v.Magnitude.ShouldBeGreaterThanOrEqualTo(0.0);
            }
        }
    }

    [Fact]
    public void Perturb_DifferentSeeds_ProduceDifferentResults()
    {
        var (matrix, pinIds) = CreateSimpleMatrix(0.5);
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.1)
        };

        var perturbed1 = _perturber.Perturb(matrix, variations, new Random(1));
        var perturbed2 = _perturber.Perturb(matrix, variations, new Random(999));

        var v1 = perturbed1.GetNonNullValues().Values.First().Magnitude;
        var v2 = perturbed2.GetNonNullValues().Values.First().Magnitude;
        v1.ShouldNotBe(v2);
    }

    [Fact]
    public void Perturb_PreservesPinStructure()
    {
        var (matrix, pinIds) = CreateSimpleMatrix(0.5);
        var variations = new List<ParameterVariation>
        {
            new(ParameterType.Coupling, 0.05)
        };

        var perturbed = _perturber.Perturb(matrix, variations, new Random(42));

        perturbed.PinReference.Count.ShouldBe(matrix.PinReference.Count);
        foreach (var pinId in pinIds)
        {
            perturbed.PinReference.ShouldContainKey(pinId);
        }
    }

    [Fact]
    public void GaussianSample_ProducesDistributedValues()
    {
        var random = new Random(42);
        var samples = Enumerable.Range(0, 1000)
            .Select(_ => SMatrixParameterPerturber.GaussianSample(random))
            .ToList();

        double mean = samples.Average();
        double variance = samples.Select(s => (s - mean) * (s - mean)).Average();

        mean.ShouldBe(0.0, tolerance: 0.1);
        variance.ShouldBe(1.0, tolerance: 0.2);
    }

    private static (SMatrix Matrix, List<Guid> PinIds) CreateSimpleMatrix(
        double magnitude)
    {
        var pinIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var matrix = new SMatrix(pinIds, new List<(Guid, double)>());
        var transfers = new Dictionary<(Guid, Guid), Complex>
        {
            { (pinIds[0], pinIds[1]), new Complex(magnitude, 0) },
            { (pinIds[1], pinIds[0]), new Complex(magnitude, 0) }
        };
        matrix.SetValues(transfers);
        return (matrix, pinIds);
    }
}

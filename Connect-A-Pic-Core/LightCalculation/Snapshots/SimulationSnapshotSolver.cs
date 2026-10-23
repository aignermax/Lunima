using System.Collections.Immutable;
using System.Numerics;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.LightCalculation.LaserSpectrum;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>Solves captured physics without accessing document objects or caller-owned callbacks.</summary>
public static class SimulationSnapshotSolver
{
    public static async Task<SimulationSnapshotResult> RunAsync(SimulationInputSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var components = new List<Dictionary<int, SMatrix>>();
        foreach (var component in snapshot.Components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            components.Add(component.Rebuild());
        }
        var wavelengthFields = new List<Dictionary<Guid, Complex>>();
        SMatrix? diagnostic = null;
        foreach (var wavelength in snapshot.Sources.Select(source => source.WavelengthNm).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matrix = BuildMatrix(snapshot, components, wavelength);
            var input = MathNet.Numerics.LinearAlgebra.Vector<Complex>.Build.Dense(matrix.SMat.RowCount);
            foreach (var source in snapshot.Sources.Where(source => source.WavelengthNm == wavelength))
                if (matrix.PinReference.TryGetValue(source.FlowId, out var index)) input[index] = source.Field;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var fields = await matrix.CalcFieldAtPinsAfterStepsAsync(input, SMatrix.DefaultMaxIterations, stop)
                .ConfigureAwait(false);
            // The legacy low-level solver returns empty on cancellation; never publish that as a completed run.
            cancellationToken.ThrowIfCancellationRequested();
            wavelengthFields.Add(fields);
            diagnostic ??= BuildMatrix(snapshot, components, wavelength);
        }
        var merged = snapshot.CombineIncoherently ? IncoherentFieldCombiner.Combine(wavelengthFields) : Merge(wavelengthFields);
        cancellationToken.ThrowIfCancellationRequested();
        return new(merged.ToImmutableDictionary(), diagnostic);
    }

    private static SMatrix BuildMatrix(SimulationInputSnapshot snapshot,
        IReadOnlyList<Dictionary<int, SMatrix>> components, int wavelength)
    {
        var matrices = components.Select(component => WavelengthInterpolator.GetMatrix(component, wavelength, out _)).ToList();
        var transfers = snapshot.Transfers[wavelength];
        var ids = transfers.SelectMany(value => new[] { value.From, value.To })
            .Concat(snapshot.Sources.Select(source => source.FlowId)).Distinct().ToList();
        var wires = new SMatrix(ids, new());
        wires.SetValues(transfers.ToDictionary(value => (value.From, value.To), value => value.Value));
        matrices.Add(wires);
        return SMatrix.CreateSystemSMatrix(matrices);
    }

    private static Dictionary<Guid, Complex> Merge(IEnumerable<Dictionary<Guid, Complex>> fields)
    {
        var result = new Dictionary<Guid, Complex>();
        foreach (var wavelength in fields)
            foreach (var (id, field) in wavelength) result[id] = result.GetValueOrDefault(id) + field;
        return result;
    }
}

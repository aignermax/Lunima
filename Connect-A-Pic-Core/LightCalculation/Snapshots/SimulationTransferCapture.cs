using System.Collections.Immutable;
using System.Numerics;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>Resolves the finite run's connection physics into detached wavelength/flow values.</summary>
internal static class SimulationTransferCapture
{
    internal static ImmutableDictionary<int, ImmutableArray<SimulationTransfer>> Capture(
        IReadOnlyList<WaveguideConnection> wires, IReadOnlyList<FrozenWaveguidePath> paths,
        IEnumerable<int> wavelengths, bool coherent)
    {
        var result = ImmutableDictionary.CreateBuilder<int, ImmutableArray<SimulationTransfer>>();
        foreach (var wavelength in wavelengths)
        {
            // Preserve SystemMatrixBuilder's overwrite/order semantics: live connections first,
            // then recursive frozen paths. Nothing here is retained as a callback on the worker.
            var values = new Dictionary<(Guid, Guid), Complex>();
            foreach (var wire in wires)
            {
                if (wire.StartPin?.LogicalPin == null || wire.EndPin?.LogicalPin == null) continue;
                var coefficient = coherent && !wire.IsElectrical && !wire.IsCrossChipletFacetLink
                    ? wire.GetCoherentTransmission(wavelength) : wire.TransmissionCoefficient;
                coefficient *= ChipletEdgeCouplerCoupling.FieldFactor(wire, wavelength);
                Add(values, wire.StartPin.LogicalPin, wire.EndPin.LogicalPin, coefficient);
            }
            foreach (var path in paths)
            {
                if (path.StartPin?.LogicalPin == null || path.EndPin?.LogicalPin == null) continue;
                var coefficient = coherent ? path.GetCoherentTransmission(wavelength) : path.TransmissionCoefficient;
                Add(values, path.StartPin.LogicalPin, path.EndPin.LogicalPin, coefficient);
            }
            result[wavelength] = values.Select(pair =>
                new SimulationTransfer(pair.Key.Item1, pair.Key.Item2, pair.Value)).ToImmutableArray();
        }
        return result.ToImmutable();
    }

    private static void Add(Dictionary<(Guid, Guid), Complex> values, Pin start, Pin end, Complex coefficient)
    {
        values[(start.IDOutFlow, end.IDInFlow)] = coefficient;
        values[(end.IDOutFlow, start.IDInFlow)] = coefficient;
    }
}

using System.Numerics;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.LightCalculation.MaterialDispersion;

namespace CAP_Core.LightCalculation.Snapshots;

/// <summary>Connection physics captured once; wavelength sampling never visits a live route or pin.</summary>
internal sealed record OpticalConnectionSnapshot(Guid ForwardFrom, Guid ForwardTo,
    Guid ReverseFrom, Guid ReverseTo, Complex Amplitude, double PhaseLength,
    CapturedDispersion? Dispersion, FacetCouplingSnapshot? Facet)
{
    internal static OpticalConnectionSnapshot? Capture(WaveguideConnection wire, bool coherent)
    {
        if (wire.StartPin?.LogicalPin is not { } start || wire.EndPin?.LogicalPin is not { } end) return null;
        var phaseLength = coherent && !wire.IsElectrical && !wire.IsCrossChipletFacetLink
            ? wire.PathLengthMicrometers : 0;
        // The legacy sweep uses the existing route loss, not a newly recalculated loss at each sample.
        return Create(start, end, wire.TransmissionCoefficient, phaseLength,
            phaseLength != 0 ? CapturedDispersion.Capture(wire.DispersionModel) : null,
            FacetCouplingSnapshot.Capture(wire));
    }

    internal static OpticalConnectionSnapshot? Capture(FrozenWaveguidePath path, bool coherent)
    {
        if (path.StartPin?.LogicalPin is not { } start || path.EndPin?.LogicalPin is not { } end) return null;
        var phaseLength = coherent ? path.Path?.TotalLengthMicrometers ?? 0 : 0;
        var dispersion = phaseLength != 0 ? CapturedDispersion.Capture(path.DispersionModel
            ?? path.StartPin.ParentComponent?.WaveguideDispersion
            ?? path.EndPin.ParentComponent?.WaveguideDispersion) : null;
        // Resolve endpoint bend models and route loss while the document is still owned by the caller.
        return Create(start, end, path.TransmissionCoefficient, phaseLength, dispersion, null);
    }

    private static OpticalConnectionSnapshot Create(Pin start, Pin end, Complex amplitude,
        double phaseLength, CapturedDispersion? dispersion, FacetCouplingSnapshot? facet) =>
        new(start.IDOutFlow, end.IDInFlow, end.IDOutFlow, start.IDInFlow, amplitude, phaseLength, dispersion, facet);

    internal void AddTransfers(IDictionary<(Guid, Guid), Complex> transfers, double wavelengthNm)
    {
        var value = Amplitude;
        if (PhaseLength != 0)
        {
            var index = Dispersion?.EffectiveIndexAt(wavelengthNm) ?? WaveguideConnection.DefaultEffectiveIndex;
            var phase = -2.0 * Math.PI * index * PhaseLength / (wavelengthNm / 1000.0);
            value *= Complex.Exp(new Complex(0, phase));
        }
        if (Facet != null) value *= Facet.FieldFactor(wavelengthNm);
        transfers[(ForwardFrom, ForwardTo)] = value;
        transfers[(ReverseFrom, ReverseTo)] = value;
    }
}

/// <summary>
/// Closed set of immutable material values. The shipped sealed models own readonly scalars or
/// private copied tables, so sharing these values preserves exact evaluation without per-sample capture.
/// Arbitrary interface implementations are not assumed immutable and may not cross this boundary.
/// </summary>
internal sealed class CapturedDispersion
{
    private readonly IDispersionModel _value;
    private CapturedDispersion(IDispersionModel value) => _value = value;

    internal static CapturedDispersion? Capture(IDispersionModel? value) => value switch
    {
        null => null,
        ConstantDispersion or PolynomialDispersion or TabulatedDispersion => new(value),
        _ => throw new InvalidDataException($"Dispersion model '{value.GetType().Name}' requires a detached definition.")
    };

    internal double EffectiveIndexAt(double wavelengthNm) => _value.NEffAt(wavelengthNm);
}

internal sealed record FacetCouplingSnapshot(bool Facing, double WaistA, double WaistB,
    double LateralOffset, double Gap, double MediumIndex)
{
    internal static FacetCouplingSnapshot? Capture(WaveguideConnection wire)
    {
        if (!ChipletInterfaceChecker.TryGetFacet(wire.StartPin, out var start)
            || !ChipletInterfaceChecker.TryGetFacet(wire.EndPin, out var end)
            || ReferenceEquals(start.Chiplet, end.Chiplet)) return null;
        var angle = start.Pin.GetAbsoluteAngle();
        var facing = ChipletInterfaceChecker.AntiparallelDeviation(angle, end.Pin.GetAbsoluteAngle())
            <= ChipletInterfaceChecker.FacingToleranceDegrees;
        var (startX, startY) = start.Pin.GetAbsolutePosition();
        var (endX, endY) = end.Pin.GetAbsolutePosition();
        return new(facing, ChipletEdgeCouplerCoupling.WaistOf(start.Pin.ParentComponent),
            ChipletEdgeCouplerCoupling.WaistOf(end.Pin.ParentComponent),
            ChipletInterfaceChecker.LateralOffset(startX, startY, endX, endY, angle),
            ChipletInterfaceChecker.AxialGap(startX, startY, endX, endY, angle), wire.FacetGapMediumIndex);
    }

    internal double FieldFactor(double wavelengthNm) => Facing
        ? Math.Sqrt(ChipletEdgeCouplerCoupling.PowerCoupling(WaistA, WaistB, LateralOffset, Gap, wavelengthNm, MediumIndex))
        : 0;
}

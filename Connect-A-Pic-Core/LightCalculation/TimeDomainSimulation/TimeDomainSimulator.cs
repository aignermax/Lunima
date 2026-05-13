using System.Numerics;

namespace CAP_Core.LightCalculation.TimeDomainSimulation;

/// <summary>
/// Orchestrates circuit-level time-domain simulation via IFFT of S-parameters.
/// Phase 1: linear circuits only (nonlinear connections cause an exception).
/// </summary>
public class TimeDomainSimulator
{
    /// <summary>Default centre wavelength in nm.</summary>
    public const double DefaultCenterWavelengthNm = 1550;

    /// <summary>Default wavelength span in nm.</summary>
    public const double DefaultSpanNm = 100;

    /// <summary>Default number of frequency/time points.</summary>
    public const int DefaultNPoints = 256;

    private readonly ImpulseResponseBuilder _irBuilder;

    /// <summary>Initializes a new instance of <see cref="TimeDomainSimulator"/>.</summary>
    /// <param name="matrixBuilder">System S-matrix builder.</param>
    public TimeDomainSimulator(ISystemMatrixBuilder matrixBuilder)
    {
        if (matrixBuilder == null) throw new ArgumentNullException(nameof(matrixBuilder));
        _irBuilder = new ImpulseResponseBuilder(matrixBuilder);
    }

    /// <summary>
    /// Runs a time-domain simulation.
    /// </summary>
    /// <param name="inputSignals">
    /// Dictionary mapping each active inflow pin Guid to its real-valued time signal.
    /// Signals must have the same length as <paramref name="timeDef"/>.NSamples.
    /// </param>
    /// <param name="timeDef">
    /// Defines sample rate and duration (use <see cref="TimeSignalDefinition.FromWavelengthSweep"/>
    /// to derive these from the same wavelength parameters passed below).
    /// </param>
    /// <param name="centerWavelengthNm">Centre wavelength for the IFFT sweep (nm).</param>
    /// <param name="spanNm">Wavelength span for the IFFT sweep (nm).</param>
    /// <param name="nFreqPoints">Number of frequency sweep points.</param>
    /// <returns>
    /// A <see cref="TimeDomainResult"/> with per-output-pin intensity traces.
    /// Only output pins that receive signal from at least one active input are included.
    /// </returns>
    public TimeDomainResult Run(
        Dictionary<Guid, double[]> inputSignals,
        TimeSignalDefinition timeDef,
        double centerWavelengthNm = DefaultCenterWavelengthNm,
        double spanNm = DefaultSpanNm,
        int nFreqPoints = DefaultNPoints)
    {
        if (inputSignals == null) throw new ArgumentNullException(nameof(inputSignals));
        if (timeDef == null) throw new ArgumentNullException(nameof(timeDef));

        // Build impulse responses (also validates: no nonlinear connections)
        var impulseResponses = _irBuilder.Build(centerWavelengthNm, spanNm, nFreqPoints);

        var outputPinIds = impulseResponses
            .Select(ir => ir.OutputPinId)
            .Distinct()
            .ToList();

        var outputTraces = new Dictionary<Guid, double[]>();

        foreach (var outputPin in outputPinIds)
        {
            double[]? combinedField = null;

            foreach (var ir in impulseResponses.Where(r => r.OutputPinId == outputPin))
            {
                if (!inputSignals.TryGetValue(ir.InputPinId, out var inputSignal))
                    continue;

                // Convolve input signal with impulse response, get real-valued field output
                var fieldSamples = TimeDomainConvolver.Convolve(inputSignal, ir.Samples);
                var realField = ToRealField(fieldSamples, timeDef.NSamples);

                combinedField = combinedField == null
                    ? realField
                    : SumArrays(combinedField, realField);
            }

            if (combinedField != null)
                outputTraces[outputPin] = combinedField.Select(v => v * v).ToArray();
        }

        return new TimeDomainResult(timeDef.TimeAxis, outputTraces);
    }

    /// <summary>Extracts the real part of complex field samples, trimmed to <paramref name="length"/>.</summary>
    private static double[] ToRealField(Complex[] complexField, int length)
    {
        int len = Math.Min(length, complexField.Length);
        var result = new double[length];
        for (int i = 0; i < len; i++)
            result[i] = complexField[i].Real;
        return result;
    }

    private static double[] SumArrays(double[] a, double[] b)
    {
        int len = Math.Max(a.Length, b.Length);
        var result = new double[len];
        for (int i = 0; i < len; i++)
        {
            double va = i < a.Length ? a[i] : 0;
            double vb = i < b.Length ? b[i] : 0;
            result[i] = va + vb;
        }
        return result;
    }
}

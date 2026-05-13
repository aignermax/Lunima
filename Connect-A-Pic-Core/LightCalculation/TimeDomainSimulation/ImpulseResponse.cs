using System.Numerics;

namespace CAP_Core.LightCalculation.TimeDomainSimulation;

/// <summary>
/// Holds the time-domain impulse response h(t) for a single (input-pin → output-pin) connection.
/// Computed via IFFT of the frequency-swept complex S-parameter.
/// </summary>
public class ImpulseResponse
{
    /// <summary>Source (inflow) pin ID.</summary>
    public Guid InputPinId { get; }

    /// <summary>Destination (outflow) pin ID.</summary>
    public Guid OutputPinId { get; }

    /// <summary>Complex impulse-response samples h[n], one per time step.</summary>
    public Complex[] Samples { get; }

    /// <summary>Time step dt = 1 / bandwidth in seconds.</summary>
    public double TimeStepSeconds { get; }

    /// <summary>Initializes a new instance of <see cref="ImpulseResponse"/>.</summary>
    /// <param name="inputPinId">Source inflow pin.</param>
    /// <param name="outputPinId">Destination outflow pin.</param>
    /// <param name="samples">IFFT result: h[n] for n = 0 … N-1.</param>
    /// <param name="timeStepSeconds">dt = 1 / frequency-bandwidth.</param>
    public ImpulseResponse(Guid inputPinId, Guid outputPinId, Complex[] samples, double timeStepSeconds)
    {
        InputPinId = inputPinId;
        OutputPinId = outputPinId;
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        TimeStepSeconds = timeStepSeconds;
    }
}

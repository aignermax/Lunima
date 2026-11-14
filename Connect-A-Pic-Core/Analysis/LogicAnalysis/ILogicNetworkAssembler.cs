using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP_Core.Analysis.LogicAnalysis;

/// <summary>Builds an evaluable logic network from the selected design scope.</summary>
public interface ILogicNetworkAssembler
{
    /// <summary>
    /// Captures the gates and wiring of the design on the calling (owner) thread, cut into slices
    /// with <paramref name="captureSlices"/> handing the owner back between them, then solves the
    /// captured gates off the owner thread.
    /// </summary>
    /// <param name="components">The design's top-level components.</param>
    /// <param name="connections">The design's waveguide connections.</param>
    /// <param name="wavelengthNm">Laser wavelength in nm the gates are extracted at.</param>
    /// <param name="captureSlices">Yields the owner between capture slices; <see cref="NoCaptureYield"/> keeps one slice.</param>
    /// <param name="cancellationToken">Cancels the capture and the solve.</param>
    Task<LogicNetworkEvaluator> AssembleAsync(IReadOnlyList<Component> components,
        IReadOnlyList<WaveguideConnection> connections, int wavelengthNm, ICaptureYield captureSlices,
        CancellationToken cancellationToken = default);
}

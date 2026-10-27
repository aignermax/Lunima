using System.Collections.Immutable;
using CAP_Core.Grid;
using CAP_Core.LightCalculation.Snapshots;

namespace CAP_Core.LightCalculation.TimeDomainSimulation;

/// <summary>Detached circuit definitions and diagnostic ownership shared by transient and eye analyses.</summary>
public sealed class TransientCircuitSnapshot
{
    private readonly OpticalCircuitSnapshot _circuit;
    private readonly TransitiveClosureContext? _context;

    private TransientCircuitSnapshot(OpticalCircuitSnapshot circuit, TransitiveClosureContext? context)
    {
        _circuit = circuit;
        _context = context;
    }

    /// <summary>Captures the circuit used to construct a transient simulator.</summary>
    public static TransientCircuitSnapshot Capture(GridManager grid, TransitiveClosureContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var capturedContext = context is null ? null : new TransitiveClosureContext
        {
            PinOwnerNames = context.PinOwnerNames?.ToImmutableDictionary(),
            PinOwnerInstanceIds = context.PinOwnerInstanceIds?.ToImmutableDictionary(),
            ExternallyObservablePinIds = context.ExternallyObservablePinIds?.ToImmutableArray(),
            SourcePinIds = context.SourcePinIds?.ToImmutableArray(),
            WavelengthNm = context.WavelengthNm
        };
        return new(OpticalCircuitSnapshot.Capture(grid), capturedContext);
    }

    /// <summary>Creates a simulator with an explicitly supplied, run-owned warning receiver.</summary>
    public TimeDomainSimulator CreateSimulator(Action<PassivityWarning>? warningSink = null,
        Func<ISystemMatrixBuilder, ISystemMatrixBuilder>? decorateBuilder = null)
    {
        ISystemMatrixBuilder builder = new CapturedMatrixBuilder(_circuit);
        if (decorateBuilder != null) builder = decorateBuilder(builder);
        return new(builder, _context is null ? null : _context with { PassivityWarningSink = warningSink });
    }

    // Mutable interpolation/formula evaluation belongs to this simulator alone. The captured
    // circuit can therefore construct multiple simulators without sharing evaluation state.
    private sealed class CapturedMatrixBuilder : ISystemMatrixBuilder
    {
        private readonly OpticalCircuitSnapshot _circuit;
        private readonly List<Dictionary<int, SMatrix>> _components;

        internal CapturedMatrixBuilder(OpticalCircuitSnapshot circuit)
        {
            _circuit = circuit;
            _components = circuit.RebuildComponents(CancellationToken.None);
        }

        public SMatrix GetSystemSMatrix(int wavelengthNm) => GetSystemSMatrix((double)wavelengthNm);

        public SMatrix GetSystemSMatrix(double wavelengthNm) => _circuit.BuildMatrix(_components, wavelengthNm);
    }
}

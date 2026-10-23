using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;

namespace CAP_Core.Analysis.OnaAnalysis;

/// <summary>Executes a prepared wavelength sweep until its actual solver cleanup completes.</summary>
public interface IWavelengthSweepRunner
{
    Task<WavelengthSweepResult> RunAsync(WavelengthSweepConfiguration configuration,
        GridManager grid, IExternalPortManager ports, CancellationToken cancellationToken);
}

/// <summary>Runs the existing wavelength solver for a prepared circuit and its sources.</summary>
public sealed class WavelengthSweepRunner : IWavelengthSweepRunner
{
    public Task<WavelengthSweepResult> RunAsync(WavelengthSweepConfiguration configuration,
        GridManager grid, IExternalPortManager ports, CancellationToken cancellationToken) =>
        new WavelengthSweeper(new SystemMatrixBuilder(grid), ports).RunSweepAsync(configuration, grid, cancellationToken);
}

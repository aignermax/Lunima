using System.Numerics;
using CAP.Avalonia.Services.Lifetime;
using CAP.Avalonia.ViewModels.Analysis.OnaAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core;
using CAP_Core.Analysis.OnaAnalysis;
using CAP_Core.ExternalPorts;
using CAP_Core.Grid;
using UnitTests.Solvers.Lifetime;

namespace UnitTests.Analysis.Lifetime;

internal sealed class SweepLifetimeFixture : IAsyncDisposable
{
    public DesignCanvasViewModel Canvas { get; } = new();
    public ErrorConsoleService Errors { get; } = new();
    public ControlledSweep Runner { get; } = new();
    public WavelengthSpectrumViewModel? Spectrum { get; }
    public OnaSweepViewModel? Ona { get; }

    public SweepLifetimeFixture(bool spectrum, ApplicationWorkCoordinator? application = null)
    {
        var source = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        source.IsUserMarkedLightSource = true;
        source.LaserEnabled = true;
        Canvas.Components.Add(new ComponentViewModel(source));
        if (spectrum)
        {
            Spectrum = new WavelengthSpectrumViewModel(new CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrumFileSource(), Errors, Runner, application);
            Spectrum.Configure(Canvas);
        }
        else
        {
            Ona = new OnaSweepViewModel(Errors, Runner, application);
            Ona.Configure(Canvas);
        }
    }

    public Task RunAsync() => Spectrum?.RunSweepCommand.ExecuteAsync(null) ?? Ona!.RunSweepCommand.ExecuteAsync(null);
    public Task StopAsync() => Spectrum?.StopAsync() ?? Ona!.StopAsync();
    public bool HasResult => Spectrum?.HasResult ?? Ona!.HasResult;
    public bool IsRunning => Spectrum?.IsSweeping ?? Ona!.IsSweeping;
    public string Status => Spectrum?.StatusText ?? Ona!.StatusText;

    public void Reconfigure()
    {
        if (Spectrum != null) Spectrum.Configure(Canvas);
        else Ona!.Configure(Canvas);
    }

    public async ValueTask DisposeAsync()
    {
        Runner.Barrier.Release.TrySetResult();
        await StopAsync();
        await Canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Canvas.DisposeDocumentBindings();
    }

    internal sealed class ControlledSweep : IWavelengthSweepRunner
    {
        public RetirementBarrier Barrier { get; } = new();
        public int Calls { get; private set; }

        public async Task<WavelengthSweepResult> RunAsync(WavelengthSweepConfiguration configuration,
            GridManager grid, IExternalPortManager ports, CancellationToken cancellationToken)
        {
            Calls++;
            await Barrier.WaitAsync(cancellationToken);
            // Deliberately ignore cancellation after cleanup: publication permission
            // must be checked even when a worker finishes with a usable result.
            var pin = Guid.NewGuid();
            return new WavelengthSweepResult(configuration,
                [new WavelengthDataPoint(configuration.StartNm, new() { [pin] = Complex.One }, 1)],
                [pin], ["controlled sweep diagnostic"]);
        }
    }
}

using System.Numerics;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.ExternalPorts.LaserSpectrum;
using CAP_Core.LightCalculation;
using CAP_Core.LightCalculation.Snapshots;
using Shouldly;
using static UnitTests.Simulation.Snapshots.SnapshotPhysicsOracle;

namespace UnitTests.Simulation.Snapshots;

public sealed class SimulationServiceSnapshotTests
{
    [Fact]
    public async Task SpectralRun_DiagnosticUsesFirstConfiguredSampleAndPowerAddsIncoherently()
    {
        var (canvas, source, output, _) = CreateCanvas();
        var pins = source.GetAllPins();
        var config = canvas.Components[0].LaserConfig!;
        config.LineShape = LaserLineShape.Gaussian;
        config.LinewidthFwhmNm = 2;
        var low = new SMatrix(pins.SelectMany(pin => new[] { pin.IDInFlow, pin.IDOutFlow }).ToList(), new());
        low.SetValues(new() { [(pins[0].IDInFlow, pins[1].IDOutFlow)] = new Complex(0.25, 0) });
        var high = new SMatrix(pins.SelectMany(pin => new[] { pin.IDInFlow, pin.IDOutFlow }).ToList(), new());
        high.SetValues(new() { [(pins[0].IDInFlow, pins[1].IDOutFlow)] = new Complex(0.75, 0) });
        source.WaveLengthToSMatrixMap = new() { [1547] = low, [1553] = high };
        var service = new BlockingSolver();
        service.Release.TrySetResult();
        var result = await service.RunAsync(canvas).WaitAsync(TimeSpan.FromSeconds(5));
        result.Success.ShouldBeTrue();
        var matrix = result.SystemMatrix!;
        matrix.SMat[matrix.PinReference[pins[1].IDOutFlow], matrix.PinReference[pins[0].IDInFlow]]
            .Real.ShouldBe(0.25, 1e-9);
        var expectedPower = config.ToSpectrum().GetSamples().Sum(sample =>
            0.25 * sample.Weight * Math.Pow(0.25 + (sample.WavelengthNm - 1547) / 6.0 * 0.5, 2));
        result.FieldResults![output.PhysicalPins[1].LogicalPin.IDOutFlow].Magnitude
            .ShouldBe(Math.Sqrt(expectedPower), 1e-9);
        result.SourceConfigs.Single().SampleWavelengthsNm.First().ShouldBe(1547);
    }

    [Fact]
    public async Task RunAsync_UsesDetachedWorkerAndPreservesSourceMetadataAndFieldPower()
    {
        var (canvas, source, output, _) = CreateCanvas();
        var service = new BlockingSolver();
        var running = service.RunAsync(canvas);
        try
        {
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            service.Release.TrySetResult();
            var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
            result.Success.ShouldBeTrue();
            result.IsSuperseded.ShouldBeFalse();
            result.LightSourceCount.ShouldBe(1);
            result.ComponentCount.ShouldBe(2);
            result.ConnectionCount.ShouldBe(1);
            result.SourceConfigs.Single().InputPower.ShouldBe(0.25);
            result.WavelengthsUsed.ShouldBe([1550]);
            result.SystemMatrix.ShouldNotBeNull();
            result.FieldResults![output.PhysicalPins[1].LogicalPin.IDOutFlow].Magnitude.ShouldBe(0.5, 1e-9);
            canvas.PowerFlowVisualizer.CurrentResult.ShouldNotBeNull();
        }
        finally { await service.FinishAsync(running); }
    }

    [Fact]
    public async Task LiveMatrixSourceAndRouteChangesAfterWorkerAdmission_DoNotChangeCapturedPhysics()
    {
        var (canvas, source, output, wire) = CreateCanvas();
        var originalOutput = output.PhysicalPins[1].LogicalPin.IDOutFlow;
        var service = new BlockingSolver();
        var running = service.RunAsync(canvas);
        try
        {
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            canvas.Components[0].LaserConfig!.InputPower = 9;
            source.WaveLengthToSMatrixMap.Clear();
            output.WaveLengthToSMatrixMap.Clear();
            wire.StartPin = output.PhysicalPins[1];
            canvas.Document.Invalidate();
            service.Release.TrySetResult();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            // The controlled worker calls the actual snapshot solver after these live objects
            // are poisoned. A cancellation-only assertion would not prove isolation.
            service.Computed.ShouldNotBeNull();
            service.Computed.Fields[originalOutput].Magnitude.ShouldBe(0.5, 1e-9);
            source.WaveLengthToSMatrixMap.ShouldBeEmpty();
            output.WaveLengthToSMatrixMap.ShouldBeEmpty();
        }
        finally { await service.FinishAsync(running); }
    }

    [Fact]
    public async Task InvalidatedLease_RejectsLateCompletedPhysicsWithoutReplacingNewerOverlay()
    {
        var (canvas, source, output, wire) = CreateCanvas();
        var service = new BlockingSolver();
        var running = service.RunAsync(canvas);
        try
        {
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            canvas.Document.Invalidate();
            canvas.PowerFlowVisualizer.UpdateFromSimulation([wire], [source, output],
                new Dictionary<Guid, Complex> { [output.PhysicalPins[1].LogicalPin.IDOutFlow] = new(0.9, 0) });
            var newerOverlay = canvas.PowerFlowVisualizer.CurrentResult;
            service.Release.TrySetResult();
            var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
            service.Computed.ShouldNotBeNull("the real snapshot computation completed despite cancellation");
            result.IsSuperseded.ShouldBeTrue();
            result.Success.ShouldBeFalse();
            canvas.PowerFlowVisualizer.CurrentResult.ShouldBeSameAs(newerOverlay);
        }
        finally { await service.FinishAsync(running); }
    }

    [Fact]
    public async Task InvalidatedLease_DiscardsLateSolverErrorWithoutReplacingNewerOverlay()
    {
        var (canvas, source, output, wire) = CreateCanvas();
        var service = new BlockingSolver { Failure = new InvalidDataException("obsolete formula failure") };
        var running = service.RunAsync(canvas);
        try
        {
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            canvas.Document.Invalidate();
            canvas.PowerFlowVisualizer.UpdateFromSimulation([wire], [source, output],
                new Dictionary<Guid, Complex> { [output.PhysicalPins[1].LogicalPin.IDOutFlow] = new(0.9, 0) });
            var newerOverlay = canvas.PowerFlowVisualizer.CurrentResult;
            service.Release.TrySetResult();
            var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
            result.IsSuperseded.ShouldBeTrue();
            result.Success.ShouldBeFalse();
            canvas.PowerFlowVisualizer.CurrentResult.ShouldBeSameAs(newerOverlay);
        }
        finally { await service.FinishAsync(running); }
    }

    [Fact]
    public async Task CallerCancellation_ReachesWorkerAndStillThrowsInsteadOfPublishing()
    {
        var (canvas, _, _, _) = CreateCanvas();
        using var stop = new CancellationTokenSource();
        var service = new BlockingSolver();
        var running = service.RunAsync(canvas, stop.Token);
        try
        {
            await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            stop.Cancel();
            service.WorkerToken.IsCancellationRequested.ShouldBeTrue();
            service.Release.TrySetResult();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
            canvas.PowerFlowVisualizer.CurrentResult.ShouldBeNull();
        }
        finally { await service.FinishAsync(running); }
    }

    private static (DesignCanvasViewModel Canvas, Component Source, Component Output, WaveguideConnection Wire) CreateCanvas()
    {
        var canvas = new DesignCanvasViewModel();
        var source = CreateComponent(2, (0, 1, Complex.One));
        source.IsUserMarkedLightSource = true;
        var output = CreateComponent(2, (0, 1, Complex.One));
        var sourceVm = new ComponentViewModel(source);
        sourceVm.LaserConfig!.InputPower = 0.25;
        canvas.Components.Add(sourceVm);
        canvas.Components.Add(new ComponentViewModel(output));
        var wire = Wire(source, 1, output, 0);
        canvas.ConnectionManager.AddExistingConnection(wire);
        canvas.Connections.Add(new WaveguideConnectionViewModel(wire));
        return (canvas, source, output, wire);
    }

    private sealed class BlockingSolver : SimulationService
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SimulationSnapshotResult? Computed { get; private set; }
        internal CancellationToken WorkerToken { get; private set; }
        internal Exception? Failure { get; init; }

        protected override async Task<SimulationSnapshotResult> SolveSnapshotAsync(
            SimulationInputSnapshot snapshot, CancellationToken cancellationToken)
        {
            WorkerToken = cancellationToken;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(8));
            if (Failure != null) throw Failure;
            // Intentionally complete even after cancellation to exercise the publication lease.
            return Computed = await SimulationSnapshotSolver.RunAsync(snapshot);
        }

        internal async Task FinishAsync(Task running)
        {
            Release.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { /* Preserve the original assertion; every wait and worker is bounded. */ }
        }
    }
}

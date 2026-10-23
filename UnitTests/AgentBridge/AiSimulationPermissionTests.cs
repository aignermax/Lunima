using System.Numerics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.AiTools.GridTools;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using static UnitTests.Simulation.Snapshots.SnapshotPhysicsOracle;

namespace UnitTests.AgentBridge;

public class AiSimulationPermissionTests
{
    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("replace")]
    [InlineData("scope")]
    [InlineData("edit")]
    [InlineData("current")]
    public async Task ActualMcpSimulation_DrainsWorkerAndGuardsOverlayPublication(string change)
    {
        var canvas = new DesignCanvasViewModel();
        var source = CreateComponent(2, (0, 1, Complex.One));
        source.Name = "source";
        source.IsUserMarkedLightSource = true;
        var output = CreateComponent(2, (0, 1, Complex.One));
        output.Name = "output";
        var sourceView = new ComponentViewModel(source);
        sourceView.LaserConfig!.InputPower = 0.25;
        canvas.Components.Add(sourceView);
        canvas.Components.Add(new ComponentViewModel(output));
        var wire = Wire(source, 1, output, 0);
        canvas.ConnectionManager.AddExistingConnection(wire);
        canvas.Connections.Add(new WaveguideConnectionViewModel(wire));
        var solver = new HeldAiSimulation();
        var tool = new RunSimulationTool(AiAsyncToolFixture.Grid(canvas, solver));
        var catalog = AiAsyncToolFixture.Catalog(tool);
        using var caller = new CancellationTokenSource();
        var running = catalog.InvokeAsync(tool.Name, JsonSerializer.SerializeToElement(new { }), caller.Token);
        Task revoked = Task.CompletedTask;
        try
        {
            await solver.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            revoked = AiAsyncToolFixture.Revoke(change, canvas, catalog, caller);
            if (change == "cancel") await revoked;
            var newer = AiAsyncToolFixture.PublishNewerOverlay(canvas, source, output, wire);
            running.IsCompleted.ShouldBeFalse("admitted computation still owns its cleanup");
            if (change == "close") revoked.IsCompleted.ShouldBeFalse();
            solver.Release.TrySetResult();

            if (change is "close" or "cancel")
                await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
            else
            {
                var result = await running.WaitAsync(TimeSpan.FromSeconds(10));
                if (change == "current") result.Content.Single().Text.ShouldContain("Simulation completed");
                else result.Content.Single().Text.ShouldNotContain("Simulation completed");
            }
            await revoked.WaitAsync(TimeSpan.FromSeconds(5));
            solver.Computed.ShouldNotBeNull("the actual detached physics worker completed");
            if (change == "current")
            {
                canvas.PowerFlowVisualizer.CurrentResult.ShouldNotBeSameAs(newer);
                solver.Computed.Fields[output.PhysicalPins[1].LogicalPin.IDOutFlow].Magnitude.ShouldBe(0.5, 1e-9);
            }
            else
            {
                canvas.PowerFlowVisualizer.CurrentResult.ShouldBeSameAs(newer);
                solver.WorkerToken.IsCancellationRequested.ShouldBeTrue();
            }
        }
        finally
        {
            solver.Release.TrySetResult();
            await AiAsyncToolFixture.DrainAsync(running, catalog, canvas);
        }
    }
}

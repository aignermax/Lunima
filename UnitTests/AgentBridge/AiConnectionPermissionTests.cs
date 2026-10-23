using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AiTools.GridTools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.ViewModels.Canvas;

namespace UnitTests.AgentBridge;

public class AiConnectionPermissionTests
{
    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("replace")]
    [InlineData("scope")]
    [InlineData("edit")]
    [InlineData("current")]
    public async Task ActualMcpConnection_GuardsRoutesAndPostAwaitSimulationInvalidation(string change)
    {
        ControlledRoutingWorker worker = null!;
        var canvas = new DesignCanvasViewModel(new WaveguideRouter { Phase1MaxNodes = 2000, Phase2MaxNodes = 5000 },
            (router, manager, components, connections, document) =>
                worker = new ControlledRoutingWorker(router, manager, components, connections, document));
        var source = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        source.Name = "source";
        var output = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        output.Name = "output";
        output.PhysicalX = 200;
        canvas.AddComponent(source);
        canvas.AddComponent(output);
        canvas.InitializeAStarRouting(-40, -40, 300, 100);
        var tool = new CreateConnectionTool(AiAsyncToolFixture.Grid(canvas, new SimulationService()));
        var catalog = AiAsyncToolFixture.Catalog(tool);
        using var caller = new CancellationTokenSource();
        var running = catalog.InvokeAsync(tool.Name, JsonSerializer.SerializeToElement(new
        {
            from_component = source.Identifier, to_component = output.Identifier
        }), caller.Token);
        Task revoked = Task.CompletedTask;
        try
        {
            await worker.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var wire = canvas.Connections.Single().Connection;
            revoked = AiAsyncToolFixture.Revoke(change, canvas, catalog, caller);
            if (change == "cancel") await revoked;
            var newer = AiAsyncToolFixture.PublishNewerOverlay(canvas, source, output, wire);
            running.IsCompleted.ShouldBeFalse("the accepted routing worker must drain");
            if (change == "close") revoked.IsCompleted.ShouldBeFalse();
            worker.Begin.TrySetResult();
            await worker.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            running.IsCompleted.ShouldBeFalse("computed results still await worker cleanup");
            worker.Finish.TrySetResult();

            if (change == "current")
            {
                var result = await running.WaitAsync(TimeSpan.FromSeconds(10));
                result.Content.Single().Text.ShouldContain("Connected");
                wire.RoutedPath.ShouldNotBeNull();
                canvas.PowerFlowVisualizer.CurrentResult.ShouldBeNull("successful connection invalidates the old overlay");
            }
            else
            {
                await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
                wire.RoutedPath.ShouldBeNull();
                canvas.PowerFlowVisualizer.CurrentResult.ShouldBeSameAs(newer);
            }
            await revoked.WaitAsync(TimeSpan.FromSeconds(5));
            canvas.Connections.Count.ShouldBe(1, "revocation does not roll back the admitted connection creation");
        }
        finally
        {
            worker.Begin.TrySetResult();
            worker.Finish.TrySetResult();
            await AiAsyncToolFixture.DrainAsync(running, catalog, canvas);
            worker.FlushUpdates();
        }
    }
}

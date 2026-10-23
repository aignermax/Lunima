using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Edit;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.ViewModels.Canvas;

namespace UnitTests.AgentBridge;

public sealed class AgentEditRoutingOwnershipTests
{
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("scope")]
    [InlineData("edit")]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("current")]
    public async Task ActualMoveTool_SharesCommitPermissionWithItsRoutingWorker(string change)
    {
        ControlledRoutingWorker gate = null!;
        var canvas = new DesignCanvasViewModel(new WaveguideRouter { Phase1MaxNodes = 2000, Phase2MaxNodes = 5000 },
            (router, manager, components, connections, document) =>
                gate = new ControlledRoutingWorker(router, manager, components, connections, document));
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        end.PhysicalX = 200;
        canvas.AddComponent(start);
        canvas.AddComponent(end);
        var wire = new WaveguideConnection { StartPin = start.PhysicalPins[1], EndPin = end.PhysicalPins[0] };
        canvas.ConnectionManager.AddExistingConnection(wire);
        canvas.Connections.Add(new(wire));
        canvas.InitializeAStarRouting(-40, -40, 300, 100);
        var commands = new CommandManager();
        var session = new AgentDesignSession(canvas, commands, () => null, new ExampleDesignsService());
        var tool = new MoveComponentTool(session);
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        using var caller = new CancellationTokenSource();
        var running = catalog.InvokeAsync(tool.Name, JsonSerializer.SerializeToElement(new
        {
            id = start.Id.ToString(), dx = 20, dy = 0, force = true
        }), caller.Token);
        Task closing = Task.CompletedTask;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            start.PhysicalX.ShouldBe(20, "the original command was admitted before the worker started");
            switch (change)
            {
                case "replace": canvas.Document.ReplaceDocument(); break;
                case "scope": canvas.Document.ChangeScope(Guid.NewGuid()); break;
                case "edit": canvas.Document.Invalidate(); break;
                case "close": closing = catalog.CloseAdmissionAsync(); break;
                case "cancel": await caller.CancelAsync(); break;
            }
            running.IsCompleted.ShouldBeFalse("the accepted worker still owns its cleanup");
            gate.Begin.TrySetResult();
            await gate.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gate.Finish.TrySetResult();
            if (change == "current")
            {
                var result = await running.WaitAsync(TimeSpan.FromSeconds(10));
                result.IsError.ShouldBeFalse(result.Content[0].Text);
                wire.RoutedPath.ShouldNotBeNull();
            }
            else
            {
                await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
                wire.RoutedPath.ShouldBeNull("a revoked tool cannot publish its calculated frame");
            }
            commands.UndoCount.ShouldBe(1);
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            gate.Begin.TrySetResult();
            gate.Finish.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            gate.FlushUpdates();
            await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
            canvas.DisposeDocumentBindings();
        }
    }
}

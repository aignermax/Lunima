using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Tools;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP.Avalonia.Services.AiTools;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Routing;
using Shouldly;
using UnitTests.ViewModels.Canvas;

namespace UnitTests.AgentBridge;

public sealed class AgentRepairOwnershipTests
{
    [AvaloniaTheory]
    [InlineData("replace")]
    [InlineData("scope")]
    [InlineData("edit")]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("current")]
    public async Task ApplySuggestion_RechecksPermissionBeforeFollowUpCommand(string change)
    {
        ControlledRoutingWorker gate = null!;
        var canvas = new DesignCanvasViewModel(new WaveguideRouter { Phase1MaxNodes = 2000, Phase2MaxNodes = 5000 },
            (router, manager, components, connections, document) =>
                gate = new ControlledRoutingWorker(router, manager, components, connections, document));
        var start = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        var end = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        end.PhysicalX = 200;
        var startView = canvas.AddComponent(start);
        var endView = canvas.AddComponent(end);
        var wire = new WaveguideConnection { StartPin = start.PhysicalPins[1], EndPin = end.PhysicalPins[0] };
        canvas.ConnectionManager.AddExistingConnection(wire);
        canvas.Connections.Add(new(wire));
        canvas.InitializeAStarRouting(-40, -40, 300, 100);
        var commands = new CommandManager();
        var session = new AgentDesignSession(canvas, commands, () => null, new ExampleDesignsService());
        var candidate = new RepairCandidate("controlled_two_step", "two admitted moves", 30, new { }, () =>
            (new RepairEditPlan(new MoveComponentCommand(canvas, startView, 0, 0, 20, 0), () => start.PhysicalX == 20,
                [new MoveComponentCommand(canvas, endView, 200, 0, 220, 0)]), null));
        session.Suggestions = new RepairSuggestionSet(wire.Id.ToString(), RepairSuggestionSet.ComputeStamp(session),
            [new RepairSuggestion("s1", new TrialOutcome(candidate, true, null, 0, 0, true, 0))]);
        var tool = new ApplySuggestionTool(session);
        var catalog = new AgentToolCatalog([tool], new AiToolRegistry([]), new InlineUiDispatcher());
        using var caller = new CancellationTokenSource();
        var running = catalog.InvokeAsync(tool.Name, JsonSerializer.SerializeToElement(new { id = "s1" }), caller.Token);
        Task closing = Task.CompletedTask;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            start.PhysicalX.ShouldBe(20);
            switch (change)
            {
                case "replace": canvas.Document.ReplaceDocument(); break;
                case "scope": canvas.Document.ChangeScope(Guid.NewGuid()); break;
                case "edit": canvas.Document.Invalidate(); break;
                case "close": closing = catalog.CloseAdmissionAsync(); break;
                case "cancel": await caller.CancelAsync(); break;
            }
            gate.Begin.TrySetResult();
            await gate.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gate.Finish.TrySetResult();
            if (change == "current")
            {
                var result = await running.WaitAsync(TimeSpan.FromSeconds(10));
                result.IsError.ShouldBeFalse(result.Content[0].Text);
                start.PhysicalX.ShouldBe(20);
                end.PhysicalX.ShouldBe(220);
                commands.UndoCount.ShouldBe(2);
                session.Suggestions.ShouldBeNull();
            }
            else
            {
                try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (OperationCanceledException) { }
                start.PhysicalX.ShouldBe(20);
                end.PhysicalX.ShouldBe(200, "a revoked repair cannot execute its follow-up move");
                commands.UndoCount.ShouldBe(1, "a stale repair must neither add nor undo another history step");
                await Should.ThrowAsync<OperationCanceledException>(() => running);
            }
        }
        finally
        {
            gate.Begin.TrySetResult();
            gate.Finish.TrySetResult();
            try { await running.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            gate.FlushUpdates();
            await closing.WaitAsync(TimeSpan.FromSeconds(5));
            await catalog.CloseAdmissionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await canvas.Document.RetireAsync().WaitAsync(TimeSpan.FromSeconds(5));
            canvas.DisposeDocumentBindings();
        }
    }
}

using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP_Core.Routing.Snapshots;
using Shouldly;

namespace UnitTests.AgentBridge;

public class PrivateRepairTrialTests
{
    [AvaloniaFact]
    public async Task HeldTrial_LeavesLivePositionsRoutesAndUserUndoRedoUntouched()
    {
        var fixture = new RepairTrialFixture();
        using var originalRevision = fixture.Canvas.Document.CaptureOperation();
        var running = fixture.RunAsync();
        try
        {
            await fixture.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.AssertLiveUnchanged();
            originalRevision.IsCurrent.ShouldBeTrue();
            fixture.Gate.Begin.TrySetResult();
            await fixture.Gate.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            running.IsCompleted.ShouldBeFalse();
            fixture.AssertLiveUnchanged();
            fixture.Gate.Finish.TrySetResult();
            var response = await running.WaitAsync(TimeSpan.FromSeconds(15));
            response.IsError.ShouldBeFalse(response.Content[0].Text);
            fixture.AssertLiveUnchanged();
            originalRevision.IsCurrent.ShouldBeTrue("publishing suggestions is not a document edit");
            fixture.Session.Suggestions.ShouldNotBeNull();
            fixture.Session.Suggestions.Suggestions.Count.ShouldBeGreaterThan(0);
        }
        finally
        {
            originalRevision.Dispose();
            await fixture.FinishAsync(running);
        }
    }

    [AvaloniaTheory]
    [InlineData("close")]
    [InlineData("cancel")]
    [InlineData("replace")]
    [InlineData("scope")]
    [InlineData("edit")]
    public async Task RevokedTrial_DrainsPrivateWorkWithoutPublishingOrRollingBackTheLiveDesign(string change)
    {
        var fixture = new RepairTrialFixture();
        using var caller = new CancellationTokenSource();
        var running = fixture.RunAsync(caller.Token);
        Task revoked = Task.CompletedTask;
        try
        {
            await fixture.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            revoked = AiAsyncToolFixture.Revoke(change, fixture.Canvas, fixture.Catalog, caller);
            if (change == "cancel") await revoked;
            running.IsCompleted.ShouldBeFalse();
            if (change == "close") revoked.IsCompleted.ShouldBeFalse();
            fixture.Gate.Begin.TrySetResult();
            await fixture.Gate.Calculated.Task.WaitAsync(TimeSpan.FromSeconds(5));
            running.IsCompleted.ShouldBeFalse("accepted computation still owns its cleanup");
            fixture.Gate.Finish.TrySetResult();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(15)));
            await revoked.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.AssertLiveUnchanged();
            fixture.Session.Suggestions.ShouldBeNull();
        }
        finally { await fixture.FinishAsync(running); }
    }

    [AvaloniaFact]
    public void RoutingSnapshot_PreservesPlacementLocksRequiredByPrivateRepairCommands()
    {
        var fixture = new RepairTrialFixture();
        try
        {
            var original = fixture.Canvas.Components[0].Component;
            original.IsLocked = true;
            var snapshot = RoutingInputSnapshot.Capture(fixture.Canvas.Components.Select(view => view.Component).ToArray(),
                fixture.Canvas.Router, fixture.Canvas.ConnectionManager);
            var privateGraph = snapshot.CreatePrivateGraph();
            privateGraph.Components.Single(component => component.Id == original.Id).IsLocked.ShouldBeTrue();
        }
        finally { fixture.Canvas.DisposeDocumentBindings(); }
    }
}

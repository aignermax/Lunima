using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP_Core.Documents;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using System.Text.Json;
using Xunit;

namespace UnitTests.AgentBridge;

public sealed class PrivateRepairOwnershipTests
{
    [Theory]
    [InlineData("edit")]
    [InlineData("replace")]
    [InlineData("leave-return")]
    public async Task RevisionStamp_CannotReviveAfterMutationOrScopeReturn(string change)
    {
        var document = new DocumentSession();
        var stamp = document.CaptureRevision();
        document.IsCurrentRevision(stamp).ShouldBeTrue();
        switch (change)
        {
            case "edit": document.Invalidate(); break;
            case "replace": document.ReplaceDocument(); break;
            default: document.ChangeScope(Guid.NewGuid()); document.ChangeScope(null); break;
        }
        document.IsCurrentRevision(stamp).ShouldBeFalse();
        await document.RetireAsync(); // A stored suggestion stamp owns no work.
    }

    [AvaloniaFact]
    public async Task PrivateWorkbench_RetainsCapturedRouterManagerAndPlacementSettings()
    {
        var fixture = new RepairTrialFixture();
        try
        {
            var live = fixture.Canvas;
            live.Router.UseDiagonalRouting = true;
            live.Router.PreferDirectStyledRoutes = false;
            live.Router.MinBendRadiusMicrometers = 23;
            live.Router.ProcessMinBendRadiusMicrometers = 19;
            live.ConnectionManager.WaveguideWidthMicrometers = 1.75;
            live.ConnectionManager.UseSequentialRouting = false;
            live.ConnectionManager.MaxRoutingAttempts = 7;
            live.ConnectionManager.ContentionRepairTimeBudget = TimeSpan.FromMilliseconds(31);
            live.ConnectionManager.EnableCoherentPropagationPhase = false;
            var input = RoutingInputSnapshot.Capture(live.Components.Select(view => view.Component).ToArray(),
                live.Router, live.ConnectionManager);
            await using var permission = new RepairTrialPermission(fixture.Session, null);
            await using var workbench = await RepairWorkbench.CreateAsync(input, null, permission, null, (-15, -20, 345, 210));
            var trial = workbench.Session.Canvas;
            trial.Router.ShouldNotBeSameAs(live.Router);
            trial.ConnectionManager.ShouldNotBeSameAs(live.ConnectionManager);
            trial.Router.UseDiagonalRouting.ShouldBeTrue();
            trial.Router.PreferDirectStyledRoutes.ShouldBeFalse();
            trial.Router.MinBendRadiusMicrometers.ShouldBe(23);
            trial.Router.ProcessMinBendRadiusMicrometers.ShouldBe(19);
            trial.ConnectionManager.WaveguideWidthMicrometers.ShouldBe(1.75);
            trial.ConnectionManager.UseSequentialRouting.ShouldBeFalse();
            trial.ConnectionManager.MaxRoutingAttempts.ShouldBe(7);
            trial.ConnectionManager.ContentionRepairTimeBudget.ShouldBe(TimeSpan.FromMilliseconds(31));
            trial.ConnectionManager.EnableCoherentPropagationPhase.ShouldBeFalse();
            (trial.ChipMinX, trial.ChipMinY, trial.ChipMaxX, trial.ChipMaxY).ShouldBe((-15d, -20d, 345d, 210d));
            var first = live.Components[0];
            var instruction = new NudgeRepair(first.Component.Id, 0, 4);
            var (plan, error) = instruction.Build(workbench.Session);
            error.ShouldBeNull();
            plan.ShouldNotBeNull();
            using (trial.Routing.SuspendAutomaticRouting()) plan.Command.Execute();
            trial.Components[0].Y.ShouldBe(first.Y + 4);
            fixture.AssertLiveUnchanged();
        }
        finally { await fixture.FinishAsync(Task.CompletedTask); }
    }

    [AvaloniaFact]
    public async Task ApplySuggestion_RejectsAStampAfterLeavingAndReturningToTheSameScope()
    {
        var fixture = new RepairTrialFixture();
        try
        {
            var instruction = new NudgeRepair(fixture.Canvas.Components[0].Component.Id, 0, 4);
            var candidate = new RepairCandidate("nudge_component", "move first", 4, new { }, instruction);
            fixture.Session.Suggestions = new(fixture.Wire.Id.ToString(), RepairSuggestionSet.ComputeStamp(fixture.Session),
                [new("s1", new(candidate, true, null, 0, 0, true, 0))]);
            fixture.Canvas.Document.ChangeScope(Guid.NewGuid());
            fixture.Canvas.Document.ChangeScope(null);
            var result = await new ApplySuggestionTool(fixture.Session).ExecuteAsync(JsonSerializer.SerializeToElement(new { id = "s1" }));
            result.IsError.ShouldBeTrue();
            result.Content[0].Text.ShouldContain("design changed");
            fixture.AssertLiveUnchanged();
        }
        finally { await fixture.FinishAsync(Task.CompletedTask); }
    }
}

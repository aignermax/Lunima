using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services.AgentBridge.Tools.Repair.Suggest;
using CAP_Core.Routing.Snapshots;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge;

public sealed class PrivateRepairConstructionTests
{
    [AvaloniaTheory]
    [InlineData("current")]
    [InlineData("cancel")]
    [InlineData("close")]
    [InlineData("replace")]
    public async Task HeldGraphConstruction_KeepsDispatcherLiveAndDrainsBeforeBindingOrPublishing(string change)
    {
        var builder = new HeldGraphBuilder();
        var fixture = new RepairTrialFixture(builder);
        using var caller = new CancellationTokenSource();
        var running = fixture.RunAsync(caller.Token);
        Task revoked = Task.CompletedTask;
        try
        {
            await builder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            builder.EnteredOnUi.ShouldBeFalse("private rasterization and graph construction must run off the editor thread");
            fixture.RoutingOwnersCreated.ShouldBe(1, "private canvas binding waits for constructed graph values");
            for (var pulse = 0; pulse < 3; pulse++)
            {
                var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Dispatcher.UIThread.Post(() => tick.TrySetResult());
                await tick.Task.WaitAsync(TimeSpan.FromSeconds(1));
            }
            running.IsCompleted.ShouldBeFalse();
            if (change != "current")
            {
                revoked = AiAsyncToolFixture.Revoke(change, fixture.Canvas, fixture.Catalog, caller);
                if (change == "cancel") await revoked;
                if (change == "close") revoked.IsCompleted.ShouldBeFalse("the captured graph is still being reconstructed");
                running.IsCompleted.ShouldBeFalse();
            }
            builder.Release.TrySetResult();
            if (change == "current")
            {
                await fixture.Gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                fixture.RoutingOwnersCreated.ShouldBe(2);
                fixture.Gate.Begin.TrySetResult();
                fixture.Gate.Finish.TrySetResult();
                (await running.WaitAsync(TimeSpan.FromSeconds(15))).IsError.ShouldBeFalse();
                fixture.Session.Suggestions.ShouldNotBeNull();
            }
            else
            {
                await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(15)));
                await revoked.WaitAsync(TimeSpan.FromSeconds(5));
                fixture.RoutingOwnersCreated.ShouldBe(1, "revocation rejects binding after accepted construction drains");
                fixture.Session.Suggestions.ShouldBeNull();
            }
            builder.Built.ShouldBeTrue();
            fixture.AssertLiveUnchanged();
        }
        finally
        {
            builder.Release.TrySetResult();
            await fixture.FinishAsync(running);
        }
    }

    private sealed class HeldGraphBuilder : RepairGraphBuilder
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool EnteredOnUi { get; private set; }
        internal bool Built { get; private set; }
        internal override RoutingWorkGraph Build(RoutingInputSnapshot input)
        {
            EnteredOnUi = Dispatcher.UIThread.CheckAccess();
            Entered.TrySetResult();
            // The RED path records the wrong thread without deadlocking the test dispatcher.
            if (!EnteredOnUi) Release.Task.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            var graph = base.Build(input);
            Built = true;
            return graph;
        }
    }
}

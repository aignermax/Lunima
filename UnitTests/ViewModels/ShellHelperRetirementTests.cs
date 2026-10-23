using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Helpers;

namespace UnitTests.ViewModels;

public sealed class ShellHelperRetirementTests
{
    [Fact]
    public async Task GateViewRetirement_DetachesWorkspaceChanges()
    {
        await using var main = MainViewModelTestHelper.CreateMainViewModel();
        await main.GateView.StopAsync();
        var changes = 0;
        main.GateView.Changed += () => changes++;

        main.WorkspaceSwitch.Current = Workspace.Logic;

        changes.ShouldBe(0);
    }

    [Fact]
    public async Task HintRetirement_CancelsAndAwaitsPendingHide()
    {
        var cancelled = Signal();
        var release = Signal();
        var hint = new RunShortcutHintViewModel(async (_, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            await release.Task;
        });
        hint.TryShow("Workspace.Action.Checks").ShouldBeTrue();
        var stopping = hint.StopAsync();
        try
        {
            await Task.WhenAny(cancelled.Task, Task.Delay(250));
            cancelled.Task.IsCompleted.ShouldBeTrue();
            stopping.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); await Task.WhenAll(hint.PendingHide, stopping); }
    }

    [Fact]
    public async Task HintRetirement_RejectsLaterHints()
    {
        var hint = new RunShortcutHintViewModel();
        await hint.StopAsync();
        hint.TryShow("Workspace.Action.Checks").ShouldBeFalse();
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

using Shouldly;

namespace UnitTests.Solvers;

public sealed class ControlledSolverCleanupTests
{
    [SkippableFact]
    public async Task Disposal_WaitsForTransientWindowsSharingLockAfterChildrenHaveExited()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows directory deletion observes sharing locks.");
        var child = await ControlledSolverProcess.CreateAsync("os._exit(0)");
        var directory = Path.GetDirectoryName(child.ScriptPath)!;
        var held = new FileStream(Path.Combine(directory, "temporary-reader"),
            FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Task? disposal = null;
        try
        {
            disposal = child.DisposeAsync().AsTask();
            disposal.IsCompleted.ShouldBeFalse("a transient sharing violation must not fail cleanup immediately");
            held.Dispose();
            await disposal.WaitAsync(ControlledSolverProcess.GuardTimeout);
            Directory.Exists(directory).ShouldBeFalse();
        }
        finally
        {
            held.Dispose();
            if (disposal != null) await Record.ExceptionAsync(() => disposal);
            if (Directory.Exists(directory)) await child.DisposeAsync();
        }
    }
}

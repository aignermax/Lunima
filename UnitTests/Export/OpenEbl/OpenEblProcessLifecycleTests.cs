using CAP_Core.Export.Contracts;
using CAP_Core.Export.OpenEbl;
using System.Text.Json;
using CAP.Avalonia.Services.OpenEblCheck;
using CAP_Core.Export;
using Shouldly;
using UnitTests.Solvers;
using Xunit;

namespace UnitTests.Export.OpenEbl;

public sealed class OpenEblProcessLifecycleTests
{
    [SkippableFact]
    public async Task Probe_DrainsBothPipesBeforeWaitingForExit()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            sys.stdout.write('x' * 1048576)
            sys.stdout.flush()
            sys.stderr.write('e' * 1048576)
            sys.stderr.flush()
            os._exit(0)
            """);
        var locator = Locator(child, TimeSpan.FromSeconds(2));

        (await locator.ProbeAsync(child.PythonExecutable).WaitAsync(ControlledSolverProcess.GuardTimeout)).ShouldBeTrue();
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task CancelledProbe_ReapsItsChildBeforeReturningCancellation()
    {
        var child = await ControlledSolverProcess.CreateAsync("time.sleep(30)");
        using var cancellation = new CancellationTokenSource();
        var pending = Locator(child, TimeSpan.FromSeconds(15)).ProbeAsync(child.PythonExecutable, cancellation.Token);
        try
        {
            await child.WaitForStartedAsync();
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => pending.WaitAsync(ControlledSolverProcess.GuardTimeout));
            child.ShouldHaveExited();
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await child.TerminateChildrenAsync();
                try { await pending.WaitAsync(ControlledSolverProcess.GuardTimeout); } catch (OperationCanceledException) { }
            }
            finally { await child.DisposeAsync(); }
        }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootExit_DrainsAndRetiresDescendantEvenWhenRootIsAlreadyGone(bool inheritPipes)
    {
        var child = await ControlledSolverProcess.CreateAsync($$"""
            import subprocess
            stream = None if {{(inheritPipes ? "True" : "False")}} else subprocess.DEVNULL
            descendant = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(30)'], stdout=stream, stderr=stream)
            with open('descendant.pid', 'w') as handle:
                handle.write(str(descendant.pid))
            print('not a checker report', flush=True)
            os._exit(0)
            """);
        var directory = Path.GetDirectoryName(child.ScriptPath)!;
        await File.WriteAllTextAsync(Path.Combine(directory, "openebl_submission_check.py"), await File.ReadAllTextAsync(child.ScriptPath));
        await File.WriteAllTextAsync(Path.Combine(directory, "openebl_verification.py"), "print('not a checker report')");
        var gds = Path.Combine(directory, "test.gds");
        await File.WriteAllTextAsync(gds, "controlled fixture");
        var factory = ProcessLaunchFactory.CreateDefault();
        var locator = new OpenEblPythonLocator(factory, "pass", TimeSpan.FromSeconds(2));
        var checker = new OpenEblSubmissionChecker(factory, child.PythonExecutable, directory, locator, TimeSpan.FromMilliseconds(300));
        using var cancellation = new CancellationTokenSource();
        var pending = checker.CheckAsync(gds, cancellation.Token);
        try
        {
            var result = await pending.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Status.ShouldBe(OpenEblCheckStatus.CheckCrashed, "malformed fixture output must never count as passing");
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await child.TerminateChildrenAsync();
                // The checker may start its second script after the first pipe drains. Its cwd must still exist.
                try { await pending.WaitAsync(ControlledSolverProcess.GuardTimeout); } catch (OperationCanceledException) { }
            }
            finally { await child.DisposeAsync(); }
        }
    }

    private static OpenEblPythonLocator Locator(ControlledSolverProcess child, TimeSpan timeout) =>
        new(ProcessLaunchFactory.CreateDefault(), $"exec(open({JsonSerializer.Serialize(child.ScriptPath)}, encoding='utf-8').read())", timeout);
}

using CAP_Core.Export;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

public sealed class NazcaPreviewProcessLifetimeTests
{
    [SkippableFact]
    public async Task ClosedPipes_DoNotDisableRootDeadline()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("os.close(1)\nos.close(2)\ntime.sleep(20)");
        using var stop = new CancellationTokenSource();
        var running = Service(child).RenderAsync("demo", "test", null, stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            result.Error.ShouldBe("Preview script timed out after 2s.");
            child.ShouldHaveExited();
        }
        finally { await ScriptProcessLifetimeFixture.CleanupAsync(child, running, stop); }
    }

    [SkippableFact]
    public async Task CancellationAfterPipeEof_ReapsRootBeforeReturning()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            os.close(1)
            os.close(2)
            with open("closed.txt", "w") as marker:
                marker.write("closed")
            time.sleep(20)
            """);
        using var stop = new CancellationTokenSource();
        var running = Service(child, TimeSpan.FromSeconds(30)).RenderAsync("demo", "test", null, stop.Token);
        try
        {
            await ScriptProcessLifetimeFixture.ReadMarkerAsync(child, "closed.txt");
            stop.CancelAfter(TimeSpan.FromSeconds(1));
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            result.Error.ShouldBe("Operation was cancelled.");
            child.ShouldHaveExited();
        }
        finally { await ScriptProcessLifetimeFixture.CleanupAsync(child, running, stop); }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulRoot_RetiresDescendantBeforeReturningGeometry(bool inheritOutput)
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            ScriptProcessLifetimeFixture.DescendantBody(inheritOutput) + "\n" +
            $"print('{ScriptProcessLifetimeFixture.PreviewJson}', flush=True)\nos._exit(0)");
        using var stop = new CancellationTokenSource();
        var running = Service(child).RenderAsync("demo", "test", null, stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeTrue(result.Error);
            result.XMax.ShouldBe(2);
            await ScriptProcessLifetimeFixture.AssertDescendantRetiredAsync(child);
        }
        finally { await ScriptProcessLifetimeFixture.CleanupAsync(child, running, stop); }
    }

    [SkippableFact]
    public async Task PreCanceledRender_DoesNotLaunchScript()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("with open('launched.txt', 'w') as marker: marker.write('started')\ntime.sleep(20)");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var result = await Service(child).RenderAsync("demo", "test", null, stop.Token)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.Error.ShouldBe("Operation was cancelled.");
        File.Exists(Path.Combine(Path.GetDirectoryName(child.ScriptPath)!, "child.pid"))
            .ShouldBeFalse("cancelled admission must not create a process at all");
    }

    [SkippableFact]
    public async Task RawCodeInput_RemainsAvailableUntilProcessRetirementThenIsRemoved()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            code_path = sys.argv[sys.argv.index("--code-file") + 1]
            with open("code-path.txt", "w") as marker:
                marker.write(code_path)
            with open(code_path, encoding="utf-8", newline="") as source:
                with open("observed-code.txt", "w", encoding="utf-8", newline="") as observed:
                    observed.write(source.read())
            os.close(1)
            os.close(2)
            time.sleep(20)
            """);
        using var stop = new CancellationTokenSource();
        const string code = "# geometry λ\ndef component(): pass";
        var running = Service(child, TimeSpan.FromSeconds(30)).RenderRawCodeAsync(code, stop.Token);
        try
        {
            var codePath = await ScriptProcessLifetimeFixture.ReadMarkerAsync(child, "code-path.txt");
            (await ScriptProcessLifetimeFixture.ReadMarkerAsync(child, "observed-code.txt")).ShouldBe(code);
            File.Exists(codePath).ShouldBeTrue();
            stop.CancelAfter(TimeSpan.FromSeconds(1));
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            child.ShouldHaveExited();
            File.Exists(codePath).ShouldBeFalse();
        }
        finally { await ScriptProcessLifetimeFixture.CleanupAsync(child, running, stop); }
    }

    [SkippableFact]
    public async Task ChatterLargeStreamsAndNonzeroExit_KeepLegacyJsonPolicy()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            "sys.stdout.write('o' * 1048576 + '\\n')\nsys.stderr.write('e' * 1048576 + '\\n')\n" +
            $"print('{ScriptProcessLifetimeFixture.PreviewJson}', flush=True)\nos._exit(7)");
        var result = await Service(child, TimeSpan.FromSeconds(5)).RenderAsync("demo", "test", null)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.Success.ShouldBeTrue(result.Error);
        result.YMax.ShouldBe(3);
        child.ShouldHaveExited();
    }

    private static NazcaComponentPreviewService Service(ControlledSolverProcess child, TimeSpan? timeout = null) =>
        new(child.PythonExecutable, child.ScriptPath, timeout ?? ScriptProcessLifetimeFixture.Deadline);
}

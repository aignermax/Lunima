using CAP_Core.Export;
using CAP_Core.Export.Contracts;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

public sealed class GdsScriptProcessLifetimeTests
{
    private const string WriteArtifact = """
        import pathlib
        pathlib.Path(sys.argv[0]).with_suffix(".gds").write_bytes(b"controlled-gds")
        """;

    [SkippableFact]
    public async Task ClosedPipes_DoNotDisableExportDeadline()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("os.close(1)\nos.close(2)\ntime.sleep(20)");
        using var stop = new CancellationTokenSource();
        var running = Service(child).ExportToGdsAsync(child.ScriptPath, true, stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            result.ErrorMessage.ShouldContain("timed out");
            child.ShouldHaveExited();
            File.Exists(child.ScriptPath).ShouldBeTrue("the exported Python script belongs to the caller");
        }
        finally { await ScriptProcessLifetimeFixture.CleanupAsync(child, running, stop); }
    }

    [SkippableFact]
    public async Task CallerCancellation_ReapsActualRootBeforePropagating()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("time.sleep(20)");
        using var stop = new CancellationTokenSource();
        var running = Service(child, TimeSpan.FromSeconds(30)).ExportToGdsAsync(child.ScriptPath, true, stop.Token);
        try
        {
            await child.WaitForStartedAsync();
            stop.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
            child.ShouldHaveExited();
            File.Exists(child.ScriptPath).ShouldBeTrue();
        }
        finally { await ScriptProcessLifetimeFixture.CleanupAsync(child, running, stop); }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulExport_RetiresDescendantsBeforeReportingArtifact(bool inheritOutput)
    {
        await using var child = await ControlledSolverProcess.CreateAsync(
            ScriptProcessLifetimeFixture.DescendantBody(inheritOutput) + "\n" + WriteArtifact + "\nos._exit(0)");
        using var stop = new CancellationTokenSource();
        var running = Service(child).ExportToGdsAsync(child.ScriptPath, true, stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            result.ScriptPath.ShouldBe(child.ScriptPath);
            result.GdsPath.ShouldBe(Path.ChangeExtension(child.ScriptPath, ".gds"));
            (await File.ReadAllBytesAsync(result.GdsPath!)).ShouldBe("controlled-gds"u8.ToArray());
            await ScriptProcessLifetimeFixture.AssertDescendantRetiredAsync(child);
        }
        finally { await ScriptProcessLifetimeFixture.CleanupAsync(child, running, stop); }
    }

    [SkippableFact]
    public async Task PreCanceledExport_DoesNotProbeOrLaunch()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(WriteArtifact + "\nos._exit(0)");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var service = Service(child);
        await Should.ThrowAsync<OperationCanceledException>(() => service.ExportToGdsAsync(child.ScriptPath, true, stop.Token)
            .WaitAsync(ControlledSolverProcess.GuardTimeout));
        service.Probes.ShouldBe(0);
        File.Exists(Path.ChangeExtension(child.ScriptPath, ".gds")).ShouldBeFalse();
    }

    [SkippableFact]
    public async Task LargeStreamsAndNonzeroExit_PreserveFailureReportAndScript()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            sys.stdout.write("o" * 1048576 + "\n")
            sys.stdout.flush()
            sys.stderr.write("e" * 1048576 + "\n")
            sys.stderr.flush()
            os._exit(7)
            """);
        var result = await Service(child, TimeSpan.FromSeconds(5)).ExportToGdsAsync(child.ScriptPath, true)
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("exit code 7");
        result.ErrorMessage.ShouldContain(new string('e', 1048576));
        result.GdsPath.ShouldBeNull();
        File.Exists(child.ScriptPath).ShouldBeTrue();
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task ScriptOnlyExport_DoesNotProbeOrExecute()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(WriteArtifact + "\nos._exit(0)");
        var service = Service(child);
        var result = await service.ExportToGdsAsync(child.ScriptPath, false);
        result.Success.ShouldBeTrue();
        result.GdsPath.ShouldBeNull();
        service.Probes.ShouldBe(0);
        File.Exists(Path.ChangeExtension(child.ScriptPath, ".gds")).ShouldBeFalse();
    }

    private static ReadyExportService Service(ControlledSolverProcess child, TimeSpan? timeout = null)
    {
        var service = new ReadyExportService(timeout ?? ScriptProcessLifetimeFixture.Deadline);
        service.SetCustomPythonPath(child.PythonExecutable);
        return service;
    }

    private sealed class ReadyExportService(TimeSpan timeout) : GdsExportService(scriptTimeout: timeout)
    {
        internal int Probes { get; private set; }
        protected override Task<PythonEnvironmentInfo> ProbeEnvironmentAsync(CancellationToken cancellationToken = default)
        {
            Probes++;
            return Task.FromResult(new PythonEnvironmentInfo
                { PythonAvailable = true, PythonVersion = "3.12.0", NazcaAvailable = true, NazcaVersion = "0.6.1" });
        }
    }
}

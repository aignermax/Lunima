using CAP_Core.Export;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

public sealed class GdsCoordinateInterpreterTests
{
    private const string ComparisonOutput = """
        import json
        with open(sys.argv[3], "w") as report:
            json.dump({"passed":True,"max_deviation_um":0.125,"rms_deviation_um":0.0625,"tolerance_um":0.25}, report)
        print(sys.executable, flush=True)
        os._exit(0)
        """;

    [SkippableFact]
    public async Task Comparison_UsesTheExplicitInterpreterAndPreservesReportValues()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(ComparisonOutput);
        var service = new GdsCoordinateComparisonService(null,
            new CoordinateExecutionOptions(TimeSpan.FromSeconds(5)));
        service.SetCustomPythonPath(child.PythonExecutable);
        var running = service.CompareAsync(child.ScriptPath, child.ScriptPath, child.ScriptPath);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Passed.ShouldBeTrue(result.ErrorOutput);
            result.RawOutput.Trim().ShouldBe(child.PythonExecutable);
            result.MaxDeviationUm.ShouldBe(0.125);
            result.RmsDeviationUm.ShouldBe(0.0625);
            result.ToleranceUm.ShouldBe(0.25);
            child.ShouldHaveExited();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }

    [SkippableFact]
    public async Task Comparison_InvalidExplicitInterpreter_DoesNotSilentlyUseSystemPython()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(ComparisonOutput);
        var missingInterpreter = Path.Combine(Path.GetDirectoryName(child.ScriptPath)!, "missing-python-executable");
        var service = new GdsCoordinateComparisonService(null,
            new CoordinateExecutionOptions(TimeSpan.FromSeconds(5)));
        service.SetCustomPythonPath(missingInterpreter);
        var running = service.CompareAsync(child.ScriptPath, child.ScriptPath, child.ScriptPath);
        try
        {
            // Existing startup failures are exceptions; keep that contract, including
            // which explicitly selected executable caused the failure.
            var error = await Should.ThrowAsync<System.ComponentModel.Win32Exception>(
                () => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
            error.Message.ShouldContain("missing-python-executable");
            File.Exists(Path.Combine(Path.GetDirectoryName(child.ScriptPath)!, "child.pid")).ShouldBeFalse();
        }
        finally { await OwnedToolProcessScenarios.CleanupAsync(child, running); }
    }
}

using System.Text.Json;
using CAP.Avalonia.Services.Solvers;
using CAP_Core.Export.Processes;
using Shouldly;
using Xunit;

namespace UnitTests.Solvers;

public sealed class OwnedProcessLaunchContractTests
{
    [SkippableFact]
    public async Task Runner_PreservesArgumentsEnvironmentWorkingDirectoryAndInputEof()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            import json
            payload = sys.stdin.read()
            print(json.dumps({"args":sys.argv[1:], "env":os.environ["LUNIMA_PROCESS_TEST"],
                "cwd_matches":os.path.samefile(os.getcwd(), os.environ["LUNIMA_PROCESS_EXPECTED_DIRECTORY"]),
                "removed":"LUNIMA_PROCESS_REMOVED" in os.environ, "input":payload}), flush=True)
            os._exit(7)
            """);
        var directory = Path.Combine(Path.GetDirectoryName(child.ScriptPath)!, "working directory with spaces");
        Directory.CreateDirectory(directory);
        child.StartInfo.WorkingDirectory = directory;
        child.StartInfo.Environment["LUNIMA_PROCESS_EXPECTED_DIRECTORY"] = directory;
        child.StartInfo.Environment["LUNIMA_PROCESS_TEST"] = "value with spaces and 雪";
        child.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        child.StartInfo.Environment["LUNIMA_PROCESS_REMOVED"] = null;
        string[] arguments = ["", "space separated", "quote\"inside", "trailing\\", "雪", "$(literal); &"];
        foreach (var argument in arguments) child.StartInfo.ArgumentList.Add(argument);
        var result = await SubprocessJsonRunner.RunAsync(child.StartInfo, "stdin 雪", TimeSpan.FromSeconds(5),
            CancellationToken.None).WaitAsync(ControlledSolverProcess.GuardTimeout);
        result.Outcome.ShouldBe(SubprocessJsonRunner.Outcome.Completed);
        result.ExitCode.ShouldBe(7);
        using var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("args").EnumerateArray().Select(element => element.GetString()).ShouldBe(arguments);
        json.RootElement.GetProperty("env").GetString().ShouldBe("value with spaces and 雪");
        json.RootElement.GetProperty("cwd_matches").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("input").GetString().ShouldBe("stdin 雪");
        json.RootElement.GetProperty("removed").GetBoolean().ShouldBeFalse();
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task OwnedInputWriter_FlushesEachWriteLikeProcessStandardInput()
    {
        await using var child = await ControlledSolverProcess.CreateAsync("""
            line = sys.stdin.readline()
            print(line.strip(), flush=True)
            os._exit(0)
            """);
        await using var process = OwnedProcess.Start(child.StartInfo);
        var response = process.StandardOutput.ReadLineAsync();
        try
        {
            await process.StandardInput.WriteLineAsync("flushed without an explicit flush");
            (await response.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBe("flushed without an explicit flush");
            await process.WaitForExitAsync().WaitAsync(ControlledSolverProcess.GuardTimeout);
        }
        finally
        {
            await process.StopAsync();
            await response.WaitAsync(ControlledSolverProcess.GuardTimeout);
        }
        child.ShouldHaveExited();
    }

    [SkippableFact]
    public async Task Unix_PendingLaunchPipes_AreNotInheritedByAnotherOwnedTool()
    {
        Skip.If(OperatingSystem.IsWindows(), "Unix CLOEXEC contract; Windows uses an explicit handle list.");
        await using var child = await ControlledSolverProcess.CreateAsync("time.sleep(20)");
        using var pending = new OwnedProcessPipes(child.StartInfo);
        await using var unrelated = OwnedProcess.Start(child.StartInfo);
        try
        {
            pending.CloseChildCopies();
            // A pending launch's writer is now closed. An unrelated tool must not hold it alive.
            var eof = await pending.Output.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(2));
            eof.ShouldBeEmpty();
            unrelated.HasExited.ShouldBeFalse();
        }
        finally { await unrelated.StopAsync(); }
    }
}

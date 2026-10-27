using System.Text.Json;
using CAP_Core.Export.PdkResolution;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export.PdkResolution;

public sealed class PdkResolutionProcessLifetimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);
    private const string RecordInput = """
        input_path = sys.argv[sys.argv.index("--input") + 1]
        publish_fixture_signal("request-path.txt", input_path)
        """;

    private static readonly PdkResolutionEntry[] Entries =
    [
        new() { Name = "Coupler μ", Module = "demo", Function = "strt", Backend = "nazca" },
        new() { Name = "Second cell", Module = "cspdk.sin300", Function = "bend", Backend = "gdsfactory" }
    ];

    [SkippableFact]
    public async Task ClosedOutputPipes_DoNotDisableTheProcessDeadline()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(RecordInput + "\n" + """
            os.close(1)
            os.close(2)
            time.sleep(20)
            """);
        using var stop = new CancellationTokenSource();
        var running = Service(child, Deadline).ResolveAsync(Entries, stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            result.Error.ShouldBe("Resolution script timed out after 2s.");
            child.ShouldHaveExited();
            await AssertInputRemovedAsync(child);
        }
        finally { await CleanupAsync(child, running, stop); }
    }

    [SkippableFact]
    public async Task CancellationAfterPipeEof_ReapsRootBeforeReturningTheCancellationReport()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(RecordInput + "\n" + """
            os.close(1)
            os.close(2)
            publish_fixture_signal("pipes-closed.txt", "closed")
            time.sleep(20)
            """);
        using var stop = new CancellationTokenSource();
        var running = Service(child, TimeSpan.FromSeconds(30)).ResolveAsync(Entries, stop.Token);
        try
        {
            await ReadMarkerAsync(child, "pipes-closed.txt");
            // EOF is signalled before scheduling cancellation; the root remains alive throughout.
            stop.CancelAfter(TimeSpan.FromSeconds(1));
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            result.Error.ShouldBe("Operation was cancelled.");
            child.ShouldHaveExited();
            await AssertInputRemovedAsync(child);
        }
        finally { await CleanupAsync(child, running, stop); }
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulRootExit_RetiresDescendantBeforeReturningJson(bool inheritOutput)
    {
        await using var child = await ControlledSolverProcess.CreateAsync(RecordInput + "\n" + $$"""
            import subprocess
            descendant = subprocess.Popen(
                [sys.executable, "-c", "import time; time.sleep(20)"],
                stdin=subprocess.DEVNULL,
                stdout={{(inheritOutput ? "None" : "subprocess.DEVNULL")}},
                stderr={{(inheritOutput ? "None" : "subprocess.DEVNULL")}})
            publish_fixture_signal("descendant.pid", descendant.pid)
            print('{"success":true,"results":[{"name":"root done","status":"ok"}]}', flush=True)
            os._exit(0)
            """);
        using var stop = new CancellationTokenSource();
        var running = Service(child, Deadline).ResolveAsync(Entries, stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeTrue(result.Error);
            result.Results.ShouldHaveSingleItem().Name.ShouldBe("root done");
            (await ReadMarkerAsync(child, "descendant.pid")).ShouldNotBeNullOrWhiteSpace();
            child.ShouldHaveExited();
            await child.DescendantsShouldHaveExitedAsync();
            await AssertInputRemovedAsync(child);
        }
        finally { await CleanupAsync(child, running, stop); }
    }

    [SkippableFact]
    public async Task Timeout_RetainsStderrTail_AndDeletesInputAfterRetirement()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(RecordInput + "\n" + """
            for line in range(1, 8):
                print("diagnostic " + str(line), file=sys.stderr, flush=True)
            time.sleep(20)
            """);
        using var stop = new CancellationTokenSource();
        var running = Service(child, Deadline).ResolveAsync(Entries, stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeFalse();
            result.Error.ShouldBe("Resolution script timed out after 2s. Last output:\n"
                + "diagnostic 3\ndiagnostic 4\ndiagnostic 5\ndiagnostic 6\ndiagnostic 7");
            child.ShouldHaveExited();
            await AssertInputRemovedAsync(child);
        }
        finally { await CleanupAsync(child, running, stop); }
    }

    [SkippableFact]
    public async Task Success_PreservesOrderedUnicodeBackendRequests_AndTrailingJsonSemantics()
    {
        await using var child = await ControlledSolverProcess.CreateAsync(RecordInput + "\n" + """
            import json
            with open(input_path, encoding="utf-8") as source:
                entries = json.load(source)
            publish_fixture_signal("observed-input.json", json.dumps(entries, ensure_ascii=False))
            print("backend chatter", flush=True)
            print(json.dumps({"success": True, "results": [
                {"name": entry["name"], "status": "ok", "kind": entry["backend"],
                 "message": entry["module"] + "." + entry["function"]}
                for entry in entries]}), flush=True)
            os._exit(7)
            """);
        using var stop = new CancellationTokenSource();
        var running = Service(child, Deadline).ResolveAsync(Entries, stop.Token);
        try
        {
            var result = await running.WaitAsync(ControlledSolverProcess.GuardTimeout);
            result.Success.ShouldBeTrue(result.Error);
            result.Results.Select(value => (value.Name, value.Kind, value.Message, value.Status)).ShouldBe(new[]
            {
                ("Coupler μ", "nazca", "demo.strt", PdkResolutionStatus.Ok),
                ("Second cell", "gdsfactory", "cspdk.sin300.bend", PdkResolutionStatus.Ok)
            });
            using var input = JsonDocument.Parse(await ReadMarkerAsync(child, "observed-input.json"));
            input.RootElement.EnumerateArray().Select(value => (
                value.GetProperty("name").GetString(), value.GetProperty("module").GetString(),
                value.GetProperty("function").GetString(), value.GetProperty("backend").GetString()))
                .ShouldBe(Entries.Select(value => ((string?)value.Name, (string?)value.Module,
                    (string?)value.Function, (string?)value.Backend)));
            child.ShouldHaveExited();
            await AssertInputRemovedAsync(child);
        }
        finally { await CleanupAsync(child, running, stop); }
    }

    private static PdkFunctionResolutionService Service(ControlledSolverProcess child, TimeSpan timeout) =>
        new(child.PythonExecutable, child.ScriptPath, timeout);

    private static async Task AssertInputRemovedAsync(ControlledSolverProcess child)
    {
        var path = await ReadMarkerAsync(child, "request-path.txt");
        File.Exists(path).ShouldBeFalse("the request file is retired with its invocation");
    }

    private static async Task<string> ReadMarkerAsync(ControlledSolverProcess child, string name)
    {
        var path = Path.Combine(Path.GetDirectoryName(child.ScriptPath)!, name);
        using var deadline = new CancellationTokenSource(ControlledSolverProcess.GuardTimeout);
        while (true)
        {
            if (File.Exists(path))
            {
                var text = await File.ReadAllTextAsync(path, deadline.Token);
                if (text.Length > 0) return text;
            }
            await Task.Delay(10, deadline.Token);
        }
    }

    private static async Task CleanupAsync(ControlledSolverProcess child, Task running, CancellationTokenSource stop)
    {
        stop.Cancel();
        await child.TerminateChildrenAsync();
        await Record.ExceptionAsync(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
    }
}

using System.Diagnostics;
using System.Text.Json;
using ProcessLaunchFactory = CAP_Core.Export.ProcessLaunchFactory;
using CAP.Avalonia.Services.AgentBridge.Registration;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.AgentBridge;

internal sealed class ClaudeBatchCliFixture : IAsyncDisposable
{
    internal ControlledSolverProcess Child { get; }
    internal string DirectoryPath => Path.GetDirectoryName(Child.ScriptPath)!;
    internal string CapturePath => Path.Combine(DirectoryPath, "arguments.json");
    internal string SentinelPath => Path.Combine(DirectoryPath, "injected.txt");
    internal string ShimPath { get; }

    private ClaudeBatchCliFixture(ControlledSolverProcess child, string extension, bool specialPath)
    {
        Child = child;
        var shimDirectory = Path.Combine(DirectoryPath,
            specialPath ? "npm space & (group) ^ ! %LUNIMA_SHIM_PATH% μ" : "npm shim");
        Directory.CreateDirectory(shimDirectory);
        ShimPath = Path.Combine(shimDirectory, "claude" + extension);
        // Match the important npm-shim boundary: a batch file forwards its received
        // argument text through %* to a native interpreter, causing another cmd parse.
        File.WriteAllText(ShimPath,
            $"@echo off\r\nsetlocal\r\n\"{child.PythonExecutable}\" -u \"{child.ScriptPath}\" %*\r\nexit /b %errorlevel%\r\n");
    }

    internal static async Task<ClaudeBatchCliFixture> CreateAsync(
        string body = "os._exit(7)", string extension = ".cmd", bool specialPath = false)
    {
        Skip.If(!OperatingSystem.IsWindows(), "Windows batch execution requires cmd.exe.");
        var child = await ControlledSolverProcess.CreateAsync("""
            import json
            if os.environ.get("LUNIMA_BATCH_NATIVE") != "1":
                publish_fixture_signal("shell.pid", os.getppid())
            publish_fixture_signal("arguments.json", json.dumps({"arguments": sys.argv[1:], "cwd": os.getcwd(),
                           "environment": os.environ.get("LUNIMA_BATCH_VALUE")}))
            """ + "\n" + body);
        try { return new ClaudeBatchCliFixture(child, extension, specialPath); }
        catch { await child.DisposeAsync(); throw; }
    }

    internal ProcessStartInfo Build(IReadOnlyList<string> arguments, bool native = false)
    {
        IReadOnlyList<string> actual = native ? new[] { "-u", Child.ScriptPath }.Concat(arguments).ToArray() : arguments;
        ProcessLaunchFactory.CreateDefault().TryBuild(native ? Child.PythonExecutable : ShimPath,
            actual, DirectoryPath,
            new Dictionary<string, string>
            {
                ["LUNIMA_BATCH_VALUE"] = "configured owner μ",
                ["LUNIMA_BATCH_NATIVE"] = native ? "1" : "0",
                ["LUNIMA_EXPANSION"] = "MUST_NOT_EXPAND",
                ["LUNIMA_SHIM_PATH"] = "MUST_NOT_REPLACE_PATH"
            }, out var info, out var error).ShouldBeTrue(error);
        return info;
    }

    internal async Task AssertCaptureAsync(IReadOnlyList<string> expected)
    {
        using var capture = JsonDocument.Parse(await File.ReadAllTextAsync(CapturePath));
        capture.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()).ToArray()
            .ShouldBe(expected.ToArray());
        capture.RootElement.GetProperty("cwd").GetString().ShouldBe(DirectoryPath);
        capture.RootElement.GetProperty("environment").GetString().ShouldBe("configured owner μ");
    }

    internal async Task WaitForStartedAsync(Task<CliRunResult> running)
    {
        using var deadline = new CancellationTokenSource(ControlledSolverProcess.GuardTimeout);
        while (!File.Exists(CapturePath))
        {
            if (running.IsCompleted)
            {
                var result = await running;
                result.ExitCode.ShouldNotBe(CliProcessRunner.StartFailedExitCode, result.Output);
                throw new InvalidOperationException($"CLI returned before its controlled worker started: {result}");
            }
            await Task.Delay(10, deadline.Token);
        }
    }

    internal async Task CleanupAsync(CancellationTokenSource stop, Task<CliRunResult> running)
    {
        stop.Cancel();
        await Child.TerminateChildrenAsync();
        await Record.ExceptionAsync(() => running.WaitAsync(ControlledSolverProcess.GuardTimeout));
    }

    public ValueTask DisposeAsync() => Child.DisposeAsync();
}

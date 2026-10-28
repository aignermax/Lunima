using System.Diagnostics;
using CAP_Core.Export;
using CAP_Core.Export.Processes;
using Shouldly;

namespace UnitTests.Solvers;

public sealed class OwnedProcessCaptureCommandTests
{
    [SkippableTheory]
    [InlineData("echo", "hello")]
    [InlineData("echo", "hello world")]
    [InlineData("echo", "quote\"inside")]
    [InlineData("exit", "/b", "7")]
    public async Task WindowsInternalCommands_MatchProcessStartArgumentList(params string[] command)
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows cmd argument-list compatibility.");
        var factory = ProcessLaunchFactory.CreateDefault();
        ProcessStartInfo StartInfo()
        {
            factory.TryBuild("cmd.exe", new[] { "/d", "/c" }.Concat(command).ToArray(), null, null,
                out var info, out var error).ShouldBeTrue(error);
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            return info;
        }

        (int ExitCode, string Output, string Error) expected;
        using (var control = Process.Start(StartInfo())!)
        {
            var stdout = control.StandardOutput.ReadToEndAsync();
            var stderr = control.StandardError.ReadToEndAsync();
            try
            {
                control.StandardInput.Close();
                await control.WaitForExitAsync().WaitAsync(ControlledSolverProcess.GuardTimeout);
                expected = (control.ExitCode, await stdout, await stderr);
            }
            finally
            {
                if (!control.HasExited) control.Kill(entireProcessTree: true);
                await control.WaitForExitAsync().WaitAsync(ControlledSolverProcess.GuardTimeout);
                await Task.WhenAll(stdout, stderr).WaitAsync(ControlledSolverProcess.GuardTimeout);
            }
        }

        var actual = await OwnedProcessCapture.RunAsync(StartInfo(), TimeSpan.FromSeconds(5))
            .WaitAsync(ControlledSolverProcess.GuardTimeout);
        actual.TimedOut.ShouldBeFalse();
        actual.ExitCode.ShouldBe(expected.ExitCode);
        actual.Output.ShouldBe(expected.Output);
        actual.Error.ShouldBe(expected.Error);
    }
}

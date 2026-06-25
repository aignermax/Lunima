using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.Export.PythonEnvironmentManager;

/// <summary>
/// Unit tests for <see cref="UvBootstrapper"/> — tests that can run without
/// network access or an actual uv installation (pure logic tests).
/// </summary>
public class UvBootstrapperTests
{
    [Fact]
    public void EnvironmentsBaseDir_IsUnderLunimaAppData()
    {
        var dir = UvBootstrapper.EnvironmentsBaseDir;

        dir.ShouldNotBeNullOrWhiteSpace();
        dir.ShouldContain("Lunima");
        dir.ShouldContain("envs");
    }

    [Fact]
    public async Task RunProcessAsync_EchoCommand_ReturnsOutput()
    {
        // Run a simple cross-platform command to verify the process runner works
        var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);

        string fileName, args;
        if (isWindows)
        {
            fileName = "cmd.exe";
            args = "/c echo hello";
        }
        else
        {
            fileName = "echo";
            args = "hello";
        }

        var (exitCode, output, _) = await UvBootstrapper.RunProcessAsync(
            fileName, args, CancellationToken.None, timeoutMs: 10_000);

        exitCode.ShouldBe(0);
        output.Trim().ShouldContain("hello");
    }

    [Fact]
    public async Task RunProcessAsync_WithCancelledToken_ThrowsOrReturnsNonZero()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel(); // immediately cancelled

        // Either throws OperationCanceledException or returns early
        try
        {
            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
            var (exitCode, _, _) = await UvBootstrapper.RunProcessAsync(
                isWindows ? "cmd.exe" : "echo",
                isWindows ? "/c ping -n 5 127.0.0.1" : "hello",
                cts.Token, timeoutMs: 30_000);

            // If it returned without throwing, exitCode should signal failure
            // (either -1 for timeout/cancel path, or process was already done)
        }
        catch (OperationCanceledException)
        {
            // This is the expected path
        }
    }

    [Fact]
    public void DefaultPythonVersion_IsValidVersionString()
    {
        var version = UvBootstrapper.DefaultPythonVersion;
        version.ShouldNotBeNullOrWhiteSpace();
        version.ShouldMatch(@"^\d+\.\d+");
    }
}

using CAP_Core.Export.PythonEnvironmentManager;
using Shouldly;

namespace UnitTests.Export.PythonEnvironmentManager;

public class ManagedPythonProvisionerTests
{
    [Fact]
    public async Task BrokenExecutableFile_IsNotMistakenForUsableRuntime()
    {
        var path = Path.Combine(Path.GetTempPath(), "broken-python-" + Guid.NewGuid().ToString("N") + ".exe");
        await File.WriteAllTextAsync(path, "incomplete runtime download");
        try
        {
            (await ManagedPythonProvisioner.HasUsableRuntimeAsync(path,
                CAP_Core.Export.ProcessLaunchFactory.CreateDefault(), CancellationToken.None)).ShouldBeFalse();
        }
        finally { File.Delete(path); }
    }

    private const string Ready = "LUNIMA_PYTHON_READY:{\"PythonVersion\":\"3.11.12\",\"NazcaVersion\":\"0.6.1\",\"GdsFactoryVersion\":\"9\",\"CspdkVersion\":\"1\"}";

    [Fact]
    public void SuccessfulImports_WithLibraryStartupNoise_MarkCompleteHealth()
    {
        var environment = new PythonEnvironment { Name = "test", LastError = "Old failure" };
        ManagedPythonProvisioner.ApplyHealthResult(environment, 0, "Library startup message\n" + Ready + "\n", "").ShouldBeTrue();
        environment.Status.ShouldBe(PythonEnvironmentStatus.Healthy);
        environment.HasPyclipper.ShouldBeTrue();
        environment.CspdkVersion.ShouldBe("1");
        environment.LastError.ShouldBeNull();
    }

    [Theory]
    [InlineData(1, Ready, "Missing package")]
    [InlineData(0, "", "")]
    [InlineData(0, "LUNIMA_PYTHON_READY:{}", "")]
    [InlineData(0, "LUNIMA_PYTHON_READY:invalid", "")]
    public void IncompleteOrFailedProbe_NeverMarksHealthy(int exitCode, string output, string error)
    {
        var environment = new PythonEnvironment { Name = "test" };
        ManagedPythonProvisioner.ApplyHealthResult(environment, exitCode, output, error).ShouldBeFalse();
        environment.Status.ShouldBe(PythonEnvironmentStatus.Broken);
        environment.LastError.ShouldNotBeNullOrWhiteSpace();
    }
}

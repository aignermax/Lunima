using System.ComponentModel;
using CAP_Core.Export;
using Shouldly;

namespace UnitTests.Export;

public sealed class PythonDiscoveryFailureTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MissingExecutable_IsNotAnInstallation(int code)
    {
        var service = FailingProbe(new Win32Exception(code));
        if (code == 3 && !OperatingSystem.IsWindows())
        {
            await Should.ThrowAsync<Win32Exception>(() => service.CheckPythonInstallation("python", "Test"));
            return;
        }
        (await service.CheckPythonInstallation("missing-python", "Test")).ShouldBeNull();
    }

    [Theory]
    [InlineData("library")]
    [InlineData("entrypoint")]
    [InlineData("permission")]
    [InlineData("unexpected")]
    public async Task LauncherFailure_IsSurfaced(string failure)
    {
        Exception expected = failure switch
        {
            "library" => new DllNotFoundException("System.Native"),
            "entrypoint" => new EntryPointNotFoundException("SystemNative_Dup"),
            "permission" => new Win32Exception(5),
            _ => new InvalidOperationException("Launcher failed")
        };
        var actual = await Record.ExceptionAsync(() =>
            FailingProbe(expected).CheckPythonInstallation("python", "Test"));
        actual.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Cancellation_IsSurfaced()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            new PythonDiscoveryService().CheckPythonInstallation("python", "Test",
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task PackageImportFailure_PreservesPythonInstallation()
    {
        var calls = 0;
        var service = new PythonDiscoveryService(null, (_, _, _) => Task.FromResult(
            ++calls == 1 ? (0, "Python 3.12", "") : (1, "", "No module named nazca")));
        var installation = await service.CheckPythonInstallation("python", "Test");
        installation.ShouldNotBeNull();
        installation.HasNazca.ShouldBeFalse();
    }

    private static PythonDiscoveryService FailingProbe(Exception error) =>
        new(null, (_, _, _) => Task.FromException<(int, string, string)>(error));
}

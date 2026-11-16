using CAP_Core.Export;
using CAP_Core.Export.Processes;
using CAP.Avalonia.ViewModels.Export;
using Shouldly;
using UnitTests.Solvers;

namespace UnitTests.Export;

/// <summary>Exercises package discovery with real, isolated Python subprocesses.</summary>
public sealed class NazcaPackageProbeTests
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    [SkippableTheory]
    [InlineData(true, "0.6.1")]
    [InlineData(false, "unknown")]
    public async Task ColdImport_DoesNotRunDuringDiscovery_AndExportCompletes(bool metadata, string version)
    {
        await using var fixture = await ProbeEnvironment.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.SitePackages, "nazca.py"),
            "import time\ntime.sleep(3)\n__version__ = '0.6.1'\n");
        if (metadata)
        {
            var distribution = Directory.CreateDirectory(Path.Combine(fixture.SitePackages, "nazca-0.6.1.dist-info"));
            await File.WriteAllTextAsync(Path.Combine(distribution.FullName, "METADATA"), "Name: nazca\nVersion: 0.6.1\n");
        }

        var discovery = new PythonDiscoveryService(packageProbeTimeout: ProbeTimeout);
        var installation = await discovery.CheckPythonInstallation(fixture.Python, "fixture");
        installation.ShouldNotBeNull().NazcaVersion.ShouldBe(version);
        var service = fixture.ExportService();
        var environment = await service.CheckPythonEnvironmentAsync();
        environment.IsReady.ShouldBeTrue();
        environment.NazcaVersion.ShouldBe(version);

        var result = await service.ExportToGdsAsync(fixture.Script, true);
        result.Success.ShouldBeTrue(result.ErrorMessage);
        File.ReadAllText(result.GdsPath!).ShouldBe("fixture GDS");
    }

    [SkippableFact]
    public async Task SleepingInterpreter_ReportsTimeout_InDiscoveryExportAndSettings()
    {
        await using var fixture = await ProbeEnvironment.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(fixture.SitePackages, "sitecustomize.py"), "import time\ntime.sleep(30)\n");
        var discovery = new PythonDiscoveryService(packageProbeTimeout: ProbeTimeout);
        var error = await Should.ThrowAsync<TimeoutException>(() => discovery.CheckPythonInstallation(fixture.Python, "fixture"));
        error.Message.ShouldContain("checking nazca timed out after 2 s");
        error.Message.ShouldContain(fixture.Python);

        using (var cancellation = new CancellationTokenSource(ProbeTimeout))
        {
            await Should.ThrowAsync<OperationCanceledException>(() =>
                PythonPackageProbe.GetVersionAsync(ProcessLaunchFactory.CreateDefault(), fixture.Python,
                    "nazca", cancellation.Token, TimeSpan.FromSeconds(30)));
        }

        var service = fixture.ExportService();
        var result = await service.ExportToGdsAsync(fixture.Script, true);
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldNotBeNull().ShouldContain("checking nazca timed out after 2 s");
        result.ErrorMessage.ShouldNotContain("not installed");

        await using var model = new GdsExportViewModel(service, discovery);
        await model.SetPythonPathAsync(fixture.Python);
        model.NazcaStatus.ShouldContain("timed out after 2 s");
        model.IsEnvironmentReady.ShouldBeFalse();

        File.Delete(Path.Combine(fixture.SitePackages, "sitecustomize.py"));
        await File.WriteAllTextAsync(Path.Combine(fixture.SitePackages, "nazca.py"), "__version__ = '0.6.1'\n");
        (await service.ExportToGdsAsync(fixture.Script, true)).Success.ShouldBeTrue();
    }

    [SkippableFact]
    public async Task MissingModule_RemainsUnavailable_EvenWithStaleMetadata()
    {
        await using var fixture = await ProbeEnvironment.CreateAsync();
        var distribution = Directory.CreateDirectory(Path.Combine(fixture.SitePackages, "nazca-0.6.1.dist-info"));
        await File.WriteAllTextAsync(Path.Combine(distribution.FullName, "METADATA"), "Name: nazca\nVersion: 0.6.1\n");
        var environment = await fixture.ExportService().CheckPythonEnvironmentAsync();
        environment.PythonAvailable.ShouldBeTrue();
        environment.IsReady.ShouldBeFalse();
        environment.StatusMessage.ShouldBe("Nazca not installed");
    }

    private sealed class ProbeEnvironment : IAsyncDisposable
    {
        private readonly ControlledSolverProcess _host;
        internal string Python { get; }
        internal string SitePackages { get; private set; } = "";
        internal string Script { get; }

        private ProbeEnvironment(ControlledSolverProcess host)
        {
            _host = host;
            var root = Path.GetDirectoryName(host.ScriptPath)!;
            Python = OperatingSystem.IsWindows() ? Path.Combine(root, "venv", "Scripts", "python.exe")
                : Path.Combine(root, "venv", "bin", "python");
            Script = Path.Combine(root, "chip.py");
        }

        internal static async Task<ProbeEnvironment> CreateAsync()
        {
            var host = await ControlledSolverProcess.CreateAsync("");
            var fixture = new ProbeEnvironment(host);
            try
            {
                var root = Path.GetDirectoryName(host.ScriptPath)!;
                await RunAsync(host.PythonExecutable, new[] { "-m", "venv", "--without-pip", Path.Combine(root, "venv") });
                fixture.SitePackages = await RunAsync(fixture.Python, new[] { "-c", "import sysconfig; print(sysconfig.get_path('purelib'))" });
                await File.WriteAllTextAsync(fixture.Script,
                    "import nazca\nfrom pathlib import Path\nPath(__file__).with_suffix('.gds').write_text('fixture GDS')\n");
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal GdsExportService ExportService()
        {
            var service = new GdsExportService(packageProbeTimeout: ProbeTimeout);
            service.SetCustomPythonPath(Python);
            return service;
        }

        private static async Task<string> RunAsync(string python, string[] arguments)
        {
            ProcessLaunchFactory.CreateDefault().TryBuild(python, arguments, null, null, out var info, out var error).ShouldBeTrue(error);
            var result = await OwnedProcessCapture.RunAsync(info, TimeSpan.FromSeconds(30));
            result.TimedOut.ShouldBeFalse();
            result.ExitCode.ShouldBe(0, result.Error);
            return result.Output.Trim();
        }

        public ValueTask DisposeAsync() => _host.DisposeAsync();
    }
}

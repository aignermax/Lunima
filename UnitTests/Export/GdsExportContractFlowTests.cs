using CAP.Avalonia.ViewModels.Export;
using CAP_Core.Export.Contracts;
using Shouldly;
using Xunit;

namespace UnitTests.Export;

public sealed class GdsExportContractFlowTests
{
    [Fact]
    public async Task DiscoveredInterpreter_IsSelectedProbedAndUsedByTheSameExportSession()
    {
        var session = new Session();
        var discovery = new Discovery();
        await using var model = new GdsExportViewModel(session, discovery);
        string? savedPath = null;
        model.OnPythonPathChanged = path => savedPath = path;
        using var cancellation = new CancellationTokenSource();

        await model.SearchForPythonAsync(cancellation.Token);
        var result = await model.ExportScriptToGdsAsync("design.py");

        discovery.Token.CanBeCanceled.ShouldBeTrue();
        session.ProbeToken.ShouldBe(discovery.Token, "nested discovery and probing share one owned operation");
        model.AvailablePythons.ShouldHaveSingleItem().ShouldBeSameAs(discovery.Installation);
        savedPath.ShouldBe("fixture-python");
        model.IsEnvironmentReady.ShouldBeTrue();
        session.ExportedWith.ShouldBe("fixture-python");
        result.GdsPath.ShouldBe("design.gds");
        model.LastSuccessfulGdsExportRequestId.ShouldBe(model.LatestStartedGdsExportRequestId);
    }

    private sealed class Discovery : IPythonDiscovery
    {
        internal PythonInstallation Installation { get; } = new()
        {
            Path = "fixture-python", Source = "fixture", PythonVersion = "3.12", NazcaVersion = "0.6"
        };
        internal CancellationToken Token { get; private set; }
        public Task<List<PythonInstallation>> DiscoverPythonWithNazcaAsync(CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Task.FromResult(new List<PythonInstallation> { Installation });
        }
        public Task<string?> FindFirstNazcaPythonPathAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(Installation.Path);
        public Task<PythonInstallation?> CheckPythonInstallation(string pythonPath, string source,
            bool probeGdsFactory = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<PythonInstallation?>(Installation);
    }

    private sealed class Session : IGdsExportSession
    {
        private string? _path;
        internal string? ExportedWith { get; private set; }
        internal CancellationToken ProbeToken { get; private set; }
        public void SetCustomPythonPath(string? pythonPath) => _path = pythonPath;
        public string GetCurrentPythonPath() => _path ?? "python3";
        public Task<PythonEnvironmentInfo> CheckPythonEnvironmentAsync(CancellationToken cancellationToken = default)
        {
            ProbeToken = cancellationToken;
            return Task.FromResult(new PythonEnvironmentInfo { PythonAvailable = true, NazcaAvailable = true });
        }
        public Task<GdsExportResult> ExportToGdsAsync(string scriptPath, bool generateGds)
        {
            ExportedWith = _path;
            return Task.FromResult(new GdsExportResult { ScriptPath = scriptPath, GdsPath = "design.gds", Success = true });
        }
        public Task<GdsExportResult> ExportToGdsAsync(string scriptPath, bool generateGds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ExportToGdsAsync(scriptPath, generateGds);
        }
    }
}

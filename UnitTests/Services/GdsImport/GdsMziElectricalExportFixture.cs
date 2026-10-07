using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;
using UnitTests.Export;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// The MZI-with-electrical-connections design (<see cref="GdsMziElectricalFixture"/>),
/// exported ONCE through the app's own exporter and run through real Nazca twice —
/// normally (the klayout post-pass upgrades the SiEPIC stub when the PDK is present) and
/// forced-stub (klayout/siepic imports poisoned). Every round-trip test of the class
/// in <see cref="GdsMziElectricalExportCollection"/> re-imports these files: building,
/// routing and exporting per test repeated the same Python runs for the same bytes.
/// </summary>
public sealed class GdsMziElectricalExportFixture : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-gds-mzi-elec-" + Guid.NewGuid().ToString("N"));

    /// <summary>The Nazca-capable interpreter, or null — the tests skip then.</summary>
    public string? Python { get; private set; }

    /// <summary>The built (routed) design canvas — shared, read-only.</summary>
    public DesignCanvasViewModel Canvas { get; private set; } = null!;

    /// <summary>The exported Nazca script.</summary>
    public string Script { get; private set; } = "";

    /// <summary>Connections the exporter skipped.</summary>
    public List<string> SkippedConnections { get; } = new();

    /// <summary>Warnings the exporter raised.</summary>
    public List<string> ExportWarnings { get; } = new();

    /// <summary>The forced-stub GDS (deterministic on every machine).</summary>
    public string StubGds { get; private set; } = "";

    /// <summary>The upgraded GDS, or null when the environment has no klayout + SiEPIC PDK.</summary>
    public string? UpgradedGds { get; private set; }

    /// <summary>Builds the design and runs both exports, when Python with Nazca exists.</summary>
    public async Task InitializeAsync()
    {
        Python = await GdsUserDesignFixture.FindNazcaPythonAsync();
        if (Python is null) return;

        Canvas = GdsMziElectricalFixture.SharedMziCanvas;
        Script = new SimpleNazcaExporter().Export(
            Canvas, skippedConnections: SkippedConnections, exportWarnings: ExportWarnings);
        (StubGds, UpgradedGds) = await RunExportAsync(Python, Script);
    }

    /// <summary>Deletes the export directory.</summary>
    public Task DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        return Task.CompletedTask;
    }

    private async Task<(string StubGds, string? UpgradedGds)> RunExportAsync(string python, string script)
    {
        Directory.CreateDirectory(_root);
        var scriptPath = Path.Combine(_root, "mzi.py");
        await File.WriteAllTextAsync(scriptPath, script);

        var run = await SiepicRealGeometryExportTests.RunPythonAsync(python, _root, scriptPath);
        run.ExitCode.ShouldBe(0, $"nazca export script failed:\n{run.StdOut}\n{run.StdErr}");
        var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
        File.Exists(gdsPath).ShouldBeTrue($"script did not write {gdsPath}:\n{run.StdOut}");
        string? upgradedCopy = null;
        if (run.StdOut.Contains("SiEPIC cell(s) upgraded", StringComparison.Ordinal))
        {
            upgradedCopy = Path.Combine(_root, "mzi_upgraded.gds");
            File.Copy(gdsPath, upgradedCopy, overwrite: true);
        }

        var stubRunner = Path.Combine(_root, "mzi_stub.py");
        await File.WriteAllTextAsync(stubRunner,
            "import sys, runpy\n" +
            "sys.modules['klayout'] = None\n" +
            "sys.modules['klayout.db'] = None\n" +
            "sys.modules['siepic_ebeam_pdk'] = None\n" +
            $"sys.argv = [r'{scriptPath}']\n" +
            $"runpy.run_path(r'{scriptPath}', run_name='__main__')\n");
        var stubRun = await SiepicRealGeometryExportTests.RunPythonAsync(python, _root, stubRunner);
        stubRun.ExitCode.ShouldBe(0, $"forced-stub run failed:\n{stubRun.StdOut}\n{stubRun.StdErr}");
        var stubCopy = Path.Combine(_root, "mzi_stub.gds");
        File.Move(gdsPath, stubCopy, overwrite: true);
        return (stubCopy, upgradedCopy);
    }
}

/// <summary>Test classes that re-import the one shared MZI export.</summary>
[CollectionDefinition(Name)]
public sealed class GdsMziElectricalExportCollection : ICollectionFixture<GdsMziElectricalExportFixture>
{
    /// <summary>The collection name.</summary>
    public const string Name = "MZI electrical export";
}

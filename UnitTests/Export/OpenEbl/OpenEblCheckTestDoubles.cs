using CAP.Avalonia.Services;
using CAP.Avalonia.Services.OpenEblCheck;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export.OpenEbl;
using CAP_Core.Export;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Shared test doubles for the "Check for openEBL" ViewModel tests and screenshot tests:
/// a checker returning canned reports, a GDS export service writing a dummy file, and a
/// ViewModel subclass that skips the real Nazca script generation.
/// </summary>
internal static class OpenEblCheckTestDoubles
{
    /// <summary>Creates a canvas with a single component so the VM's nothing-to-export guard passes.</summary>
    public static DesignCanvasViewModel CanvasWithComponent()
    {
        var canvas = new DesignCanvasViewModel();
        var component = TestComponentFactory.CreateBasicComponent();
        component.Identifier = "C1";
        component.NazcaFunctionName = "ebeam_gc_te1550";
        canvas.AddComponent(component, "ebeam_gc_te1550");
        return canvas;
    }

    /// <summary>Creates a ViewModel wired to the given fakes; the Nazca script build is stubbed.</summary>
    public static TestableOpenEblCheckViewModel CreateViewModel(
        DesignCanvasViewModel canvas,
        FakeGdsExportService gdsExport,
        FakeOpenEblSubmissionChecker checker) =>
        new(canvas, gdsExport, checker);
}

/// <summary>VM subclass that stubs the Nazca script build (the exporter itself is tested elsewhere).</summary>
internal sealed class TestableOpenEblCheckViewModel : OpenEblCheckViewModel
{
    public TestableOpenEblCheckViewModel(
        DesignCanvasViewModel canvas,
        GdsExportService gdsExport,
        OpenEblSubmissionChecker checker)
        : base(canvas, new SimpleNazcaExporter(), gdsExport, checker)
    {
    }

    protected override string BuildNazcaScript() => "# stubbed nazca script for tests";
}

/// <summary>Checker fake returning scripted reports; records invocations.</summary>
internal sealed class FakeOpenEblSubmissionChecker : OpenEblSubmissionChecker
{
    private readonly Func<CancellationToken, Task<OpenEblCheckReport>> _run;

    public FakeOpenEblSubmissionChecker(Func<CancellationToken, Task<OpenEblCheckReport>> run)
    {
        _run = run;
    }

    public int CallCount { get; private set; }

    public string? LastGdsPath { get; private set; }

    public override Task<OpenEblCheckReport> CheckAsync(
        string gdsPath, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastGdsPath = gdsPath;
        return _run(cancellationToken);
    }

    /// <summary>A checker that immediately returns the given report.</summary>
    public static FakeOpenEblSubmissionChecker Returning(OpenEblCheckReport report) =>
        new(_ => Task.FromResult(report));
}

/// <summary>GDS export fake writing a dummy .gds next to the script (or failing on demand).</summary>
internal sealed class FakeGdsExportService : GdsExportService
{
    private readonly bool _success;

    public FakeGdsExportService(bool success = true)
    {
        _success = success;
    }

    public int CallCount { get; private set; }

    public override Task<ExportResult> ExportToGdsAsync(string scriptPath, bool generateGds)
    {
        CallCount++;
        if (!_success)
        {
            return Task.FromResult(new ExportResult
            {
                ScriptPath = scriptPath,
                Success = false,
                Status = "GDS generation failed",
                ErrorMessage = "simulated export failure",
            });
        }

        var gdsPath = Path.ChangeExtension(scriptPath, ".gds");
        File.WriteAllText(gdsPath, "fake gds content");
        return Task.FromResult(new ExportResult
        {
            ScriptPath = scriptPath,
            GdsPath = gdsPath,
            Success = true,
            Status = "ok",
        });
    }
}

using System.Text.Json;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.AgentBridge.Headless;
using CAP_Core.Export.Contracts;
using CAP_Core;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;

namespace UnitTests.AgentBridge.Headless;

/// <summary>Verifies script diagnostics survive the agent and desktop export workflows.</summary>
public sealed class GdsExportWarningJourneyTests
{
    private const string Warning = "[Lunima] WARN: SiEPIC real-geometry upgrade skipped; stub boxes kept.";

    [AvaloniaFact]
    public async Task ExportGds_PreservesRuntimeWarningsInJsonAndExistingDesktopStatus()
    {
        var runner = new Mock<IGdsExportSession>();
        runner.Setup(x => x.CheckPythonEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PythonEnvironmentInfo { PythonAvailable = true, NazcaAvailable = true });
        runner.Setup(x => x.ExportToGdsAsync(It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, bool generate, CancellationToken token) => new GdsExportResult
            {
                ScriptPath = path, GdsPath = Path.ChangeExtension(path, ".gds"), Success = true,
                Status = "Script and GDS exported with warnings: " + Warning, Warnings = new[] { Warning }
            });
        await using var session = HeadlessAgentSession.Create(TextWriter.Null,
            services => services.AddSingleton(runner.Object));
        var examples = new ExampleDesignsService(new FileExampleCatalog()).FindExamplesDirectory()!;
        await session.OpenDesignAsync(Path.Combine(examples, "EBeam Add-Drop Ring.lun"));
        var export = session.Main.FileOperations.GdsExport;
        export.GenerateGdsEnabled = true;
        await export.SetPythonPathAsync("fixture-python");
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "export-warning-" + Guid.NewGuid())).FullName;
        try
        {
            var script = Path.Combine(folder, "chip.py");
            var args = JsonSerializer.SerializeToElement(new { path = script });
            var result = await session.Catalog.InvokeAsync("export_gds", args);
            result.IsError.ShouldBeFalse();
            using var json = JsonDocument.Parse(result.Content[0].Text!);
            json.RootElement.GetProperty("warnings").EnumerateArray()
                .Select(item => item.GetString()).ShouldContain(Warning);
            var gdsStatus = json.RootElement.GetProperty("gds_status").GetString();
            gdsStatus.ShouldNotBeNull();
            gdsStatus!.ShouldContain(Warning);
            export.LastExportStatus.ShouldContain(Warning);

            string? status = null;
            var workflow = new CAP.Avalonia.ViewModels.Export.DesignFiles.NazcaExportWorkflow(
                new SimpleNazcaExporter(), session.Main.Canvas, session.Main.LeftPanel.AllTemplates,
                export, session.Services.GetRequiredService<ErrorConsoleService>(), Mock.Of<IUrlLauncher>());
            await workflow.ExportAsync(script, export.BeginExportRequest(), null, null, null, null, s => status = s);
            status.ShouldNotBeNull();
            status!.ShouldContain("GDS written with placeholder cells");
            status.ShouldContain("klayout siepic_ebeam_pdk SiEPIC");
            CaptureStatusIfRequested(session, status!);
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static void CaptureStatusIfRequested(HeadlessAgentSession session, string status)
    {
        var directory = Environment.GetEnvironmentVariable("UI_SHOT_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var window = new CAP.Avalonia.Views.MainWindow { DataContext = session.Main, Width = 1600, Height = 1000 };
        window.Show();
        try
        {
            session.Main.FileOperations.UpdateStatus?.Invoke(status);
            UnitTests.UI.Showcase.ShowcaseCapture.CaptureWindow(window, Path.Combine(directory, "placeholder-warning.png"));
        }
        finally { window.Close(); }
    }
}

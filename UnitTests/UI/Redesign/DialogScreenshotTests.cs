using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Analysis.OnaAnalysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.PdkResolution;
using CAP.Avalonia.ViewModels.Solvers;
using CAP.Avalonia.ViewModels.Solvers.DockerSetup;
using CAP.Avalonia.Services;
using CAP.Avalonia.Views.Dialogs;
using CAP_Core.Export;
using CAP_Core.Export.PdkResolution;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_Core.Solvers.Fdtd;
using CAP_Core.Solvers.ModeSolver;
using Moq;
using Xunit;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Visual documentation for the dialogs migrated onto the design system: writes one PNG per
/// dialog to <c>$UI_SHOT_DIR/ui-redesign/dialogs/</c>. Does nothing without <c>UI_SHOT_DIR</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class DialogScreenshotTests
{
    [AvaloniaFact]
    public void ExportDialogs()
    {
        if (!DialogShot.Enabled) return;
        var canvas = new DesignCanvasViewModel();

        var gdsFactory = new GdsFactoryExportViewModel(canvas, new GdsExportService());
        DialogShot.Capture(new GdsFactoryExportDialog { DataContext = gdsFactory }, "export-gdsfactory");

        var photonTorch = new PhotonTorchExportViewModel(new PhotonTorchExporter(), canvas)
        {
            UseTimeDomain = true,
        };
        DialogShot.Capture(new PhotonTorchExportDialog { DataContext = photonTorch }, "export-photontorch");

        var verilogA = new VerilogAExportViewModel(new VerilogAExporter(), new VerilogAFileWriter(), canvas);
        DialogShot.Capture(new VerilogAExportDialog { DataContext = verilogA }, "export-verilog-a");
    }

    [AvaloniaFact]
    public async Task DockerSetupDialog_LinuxNotInstalled()
    {
        if (!DialogShot.Enabled) return;
        var vm = new DockerSetupViewModel(
            _ => Task.FromResult(FdtdAvailability.Unavailable("Docker is not installed (or not on PATH).")),
            Mock.Of<IUrlLauncher>(), isLinux: true);
        await vm.CheckAgainCommand.ExecuteAsync(null);
        DialogShot.Capture(new DockerSetupDialog { DataContext = vm }, "docker-setup");
    }

    [AvaloniaFact]
    public async Task ModeSolverDialog_WithResults()
    {
        if (!DialogShot.Enabled) return;
        var service = new Mock<IModeSolverService>();
        service.Setup(s => s.SolveAsync(It.IsAny<ModeSolverRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModeSolverResult
            {
                Success = true,
                BackendUsed = "femwell",
                Modes = new[]
                {
                    new ModeSolverModeEntry { Wavelength = 1.55, ModeIndex = 0, NEff = 2.4412, NGroup = 4.2031, Polarisation = "TE" },
                    new ModeSolverModeEntry { Wavelength = 1.55, ModeIndex = 1, NEff = 1.7203, NGroup = 4.0112, Polarisation = "TM" },
                },
            });
        var vm = new ModeSolverViewModel(service.Object);
        await vm.SolveCommand.ExecuteAsync(null);
        DialogShot.Capture(new ModeSolverDialog { DataContext = vm }, "mode-solver");
    }

    [AvaloniaFact]
    public void OnaAnalyzerWindow_Idle()
    {
        if (!DialogShot.Enabled) return;
        DialogShot.Capture(new OnaAnalyzerWindow { DataContext = new OnaSweepViewModel() }, "ona-analyzer");
    }

    [AvaloniaFact]
    public void PdkResolutionCheckDialog_WithFailures()
    {
        if (!DialogShot.Enabled) return;
        var vm = new PdkResolutionCheckViewModel(
            new PdkLoader(), new Mock<PdkFunctionResolutionService>("python3", "script.py", null, null).Object,
            () => null)
        {
            StatusText = "1 error, 1 warning across 1 PDK.",
            HasFailures = true,
        };
        var group = new PdkResolutionGroupViewModel { PdkName = "Demo PDK", FileName = "demo-pdk.json" };
        group.Rows.Add(new PdkResolutionRowViewModel
        {
            ComponentName = "Straight", FunctionPath = "demo_pdk.straight",
            Status = PdkResolutionStatus.Ok, Message = "demo_pdk.straight",
        });
        group.Rows.Add(new PdkResolutionRowViewModel
        {
            ComponentName = "Ring Resonator", FunctionPath = "demo_pdk.ring_resonator",
            Status = PdkResolutionStatus.Error, Message = "module 'demo_pdk' has no attribute 'ring_resonator'",
        });
        group.Rows.Add(new PdkResolutionRowViewModel
        {
            ComponentName = "Coupler", FunctionPath = "demo_pdk.coupler",
            Status = PdkResolutionStatus.Warning, Message = "resolved via fallback",
        });
        vm.Pdks.Add(group);
        DialogShot.Capture(new PdkResolutionCheckDialog { DataContext = vm }, "pdk-resolution-check");
    }
}

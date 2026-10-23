using CAP_Core.Export.Contracts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.GdsImport;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP.Avalonia.ViewModels.GdsImport;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Views;
using CAP.Avalonia.Views.Dialogs;
using CAP_Core.Solvers.Fdtd;
using CAP_DataAccess.Persistence.PIR;
using Moq;
using UnitTests.Export.OpenEbl;
using UnitTests.Helpers;
using UnitTests.Import.Gds;
using UnitTests.Services.GdsImport;
using Xunit;

namespace UnitTests.UI.Redesign;

/// <summary>
/// Screenshots of the larger multi-state dialogs (GDS import, openEBL check, component settings)
/// for the design-system migration. Does nothing without <c>UI_SHOT_DIR</c>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class DialogWorkflowScreenshotTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "lunima-dlgshot-" + Guid.NewGuid().ToString("N"));
    private readonly GdsDesignScopeTestHost _host = new();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        _host.Dispose();
    }

    private static byte[] FoundryStyleLibrary() => GdsTestWriter.Create()
        .StandardPrologue()
        .BeginCell("TOP")
            .SRef("wg", 0, 0)
            .Path(37, 0, 500, 0, (0, 0), (50000, 0))
        .EndCell()
        .BeginCell("wg")
            .Boundary(1, 0, (0, 1750), (10000, 1750), (10000, 2250), (0, 2250), (0, 1750))
            .Text(56, 0, "opt_in", 0, 2000)
            .Text(56, 0, "opt_out", 10000, 2000)
        .EndCell()
        .EndLibrary()
        .ToArray();

    [AvaloniaFact]
    public async Task GdsImportDialog_CollapsedAndExpanded()
    {
        if (!DialogShot.Enabled) return;
        Directory.CreateDirectory(_root);
        var gdsPath = Path.Combine(_root, "foundry-library.gds");
        File.WriteAllBytes(gdsPath, FoundryStyleLibrary());
        var executor = new GdsPlacementExecutor(
            new DesignCanvasViewModel(), new CommandManager(), () => new List<ComponentTemplate>());
        var vm = new GdsImportDialogViewModel(gdsPath, _host.CreateService(), executor);
        await vm.StartAnalysisAsync();

        DialogShot.Capture(new GdsImportDialog { DataContext = vm }, "gds-import-collapsed");
        var expanded = new GdsImportDialog { DataContext = vm };
        DialogShot.Capture(expanded, "gds-import-expanded", () =>
            expanded.FindControl<Expander>("LayerAssignmentExpander")!.IsExpanded = true);
    }

    [AvaloniaFact]
    public async Task OpenEblCheckDialog_Failed()
    {
        if (!DialogShot.Enabled) return;
        var report = new CAP_Core.Export.Contracts.OpenEblCheckReport
        {
            Status = CAP_Core.Export.Contracts.OpenEblCheckStatus.Failed,
            SubmissionChecksPassed = false,
            VerificationPassed = true,
            SubmissionErrorCount = 2,
            DieBoundingBox = new CAP_Core.Export.Contracts.OpenEblDieBoundingBox(700.0, 450.0),
            Errors = new[]
            {
                new CAP_Core.Export.Contracts.OpenEblCheckError(
                    CAP_Core.Export.Contracts.OpenEblCheckCategories.TopCell, "Layout has 2 top cells."),
                new CAP_Core.Export.Contracts.OpenEblCheckError(
                    CAP_Core.Export.Contracts.OpenEblCheckCategories.DieSize,
                    "Die size 700 um x 450 um exceeds allowed size 605 um x 410 um",
                    XMicrometers: 350.0, YMicrometers: 225.0),
            },
        };
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(),
            new FakeGdsExportService(),
            FakeOpenEblSubmissionChecker.Returning(report));
        vm.Username = "jsmith";
        vm.DesignName = "MZI v2";
        await vm.ExportAndCheckCommand.ExecuteAsync(null);
        DialogShot.Capture(new OpenEblCheckDialog { DataContext = vm }, "openebl-check");
    }

    [AvaloniaFact]
    public async Task ComponentSettingsDialog_WithDockerHint()
    {
        if (!DialogShot.Enabled) return;
        const string hint = "Docker is not installed (or not on PATH). FDTD needs Docker Desktop.";
        var service = new Mock<IFdtdSMatrixService>();
        service.Setup(s => s.CheckAvailabilityAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync(FdtdAvailability.Unavailable(hint));
        var vm = new ComponentSettingsDialogViewModel(
            Mock.Of<IFileDialogService>(),
            fdtdService: service.Object,
            fdtdRequestFactory: (_, _) => Task.FromResult<FdtdSMatrixRequest?>(null));
        vm.Configure("comp", "comp", "Waveguide", new Dictionary<string, ComponentSMatrixData>(),
            liveComponent: TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        await vm.RecalculateSMatrixCommand.ExecuteAsync(null);
        DialogShot.Capture(new ComponentSettingsDialog { DataContext = vm }, "component-settings");
    }
}

using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

public class ZoomToFitDebugTests
{
    [Fact]
    public void ZoomToFit_AfterManualZoom_ShouldChangeZoomLevel()
    {
        var vm = new MainViewModel(
            new SimulationService(),
            new SimpleNazcaExporter(),
            new PdkLoader(),
            new CommandManager(),
            new UserPreferencesService(),
            new CAP_Core.Components.Creation.GroupLibraryManager(),
            new GroupPreviewGenerator(),
            new InputDialogService(),
            new GdsExportService());

        // Add component
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 0;
        comp.PhysicalY = 0;
        comp.WidthMicrometers = 1000;
        comp.HeightMicrometers = 1000;
        vm.Canvas.AddComponent(comp);

        // Simulate manual zoom (like mouse wheel)
        vm.ZoomLevel = 3.0;
        vm.ZoomLevel.ShouldBe(3.0, "Manual zoom should be applied");

        // Now call ZoomToFit
        vm.ZoomToFit(800, 600);

        // Zoom should have changed from 3.0 to something else
        vm.ZoomLevel.ShouldNotBe(3.0, "ZoomToFit should change the zoom level!");
        vm.ZoomLevel.ShouldBeInRange(0.3, 1.0, "Zoom should fit the 1000x1000 component in 800x600 viewport");
    }
}

using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Tests for <see cref="MainViewModel.ZoomToFit"/>.
/// </summary>
public class ZoomToFitTests
{
    private static MainViewModel CreateViewModel() =>
        new(new SimulationService(), new SimpleNazcaExporter(), new PdkLoader(), new CommandManager(), new UserPreferencesService(), new CAP_Core.Components.Creation.GroupLibraryManager(), new GroupPreviewGenerator(), new InputDialogService(), new GdsExportService());

    [Fact]
    public void ZoomToFit_EmptyCanvas_DoesNotChangeZoom()
    {
        var vm = CreateViewModel();
        double originalZoom = vm.ZoomLevel;

        vm.ZoomToFit(800, 600);

        vm.ZoomLevel.ShouldBe(originalZoom);
        vm.StatusText.ShouldContain("No components");
    }

    [Fact]
    public void ZoomToFit_WithComponents_SetsZoomAndPan()
    {
        var vm = CreateViewModel();
        var comp = TestComponentFactory.CreateStraightWaveGuide();
        comp.PhysicalX = 100;
        comp.PhysicalY = 100;
        comp.WidthMicrometers = 200;
        comp.HeightMicrometers = 200;
        vm.Canvas.AddComponent(comp);

        vm.ZoomToFit(800, 600);

        vm.ZoomLevel.ShouldBeGreaterThan(0);
        vm.StatusText.ShouldContain("Zoom to fit");
    }

    [Fact]
    public void ZoomToFit_ZeroViewport_DoesNothing()
    {
        var vm = CreateViewModel();
        double originalZoom = vm.ZoomLevel;

        vm.ZoomToFit(0, 0);

        vm.ZoomLevel.ShouldBe(originalZoom);
    }

    [Fact]
    public void ZoomToFit_NegativeViewport_DoesNothing()
    {
        var vm = CreateViewModel();
        double originalZoom = vm.ZoomLevel;

        vm.ZoomToFit(-100, -100);

        vm.ZoomLevel.ShouldBe(originalZoom);
    }

    [Fact]
    public void ZoomToFit_MultipleComponents_FitsAll()
    {
        var vm = CreateViewModel();

        var comp1 = TestComponentFactory.CreateStraightWaveGuide();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp1.WidthMicrometers = 100;
        comp1.HeightMicrometers = 100;
        vm.Canvas.AddComponent(comp1);

        var comp2 = TestComponentFactory.CreateStraightWaveGuide();
        comp2.PhysicalX = 900;
        comp2.PhysicalY = 900;
        comp2.WidthMicrometers = 100;
        comp2.HeightMicrometers = 100;
        vm.Canvas.AddComponent(comp2);

        vm.ZoomToFit(1000, 1000);

        // Bounding box: (0,0)-(1000,1000), with 10% padding: (-100,-100)-(1100,1100)
        // padded size: 1200x1200, viewport: 1000x1000
        // zoom = 1000/1200 ≈ 0.833
        vm.ZoomLevel.ShouldBeInRange(0.8, 0.9);
    }
}

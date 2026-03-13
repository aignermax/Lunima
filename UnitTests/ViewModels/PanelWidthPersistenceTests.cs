using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Services;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_Core.Components.Creation;
using Shouldly;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for panel width persistence across left and right panels.
/// Tests that panel widths are saved and restored correctly.
/// </summary>
public class PanelWidthPersistenceTests
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly GroupLibraryManager _libraryManager;
    private readonly PdkLoader _pdkLoader;
    private readonly UserPreferencesService _preferencesService;

    public PanelWidthPersistenceTests()
    {
        _canvas = new DesignCanvasViewModel();
        _libraryManager = new GroupLibraryManager();
        _pdkLoader = new PdkLoader();
        _preferencesService = new UserPreferencesService();
    }

    [Fact]
    public void LeftPanelWidth_DefaultsTo220()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LeftPanelWidth.ShouldBe(220);
    }

    [Fact]
    public void RightPanelWidth_DefaultsTo250()
    {
        var vm = new RightPanelViewModel(_canvas, _preferencesService);

        vm.RightPanelWidth.ShouldBe(250);
    }

    [Fact]
    public void LeftPanelWidth_ClampsToMinimum200()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LeftPanelWidth = 50; // Below minimum

        vm.LeftPanelWidth.ShouldBe(200);
    }

    [Fact]
    public void LeftPanelWidth_ClampsToMaximum800()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LeftPanelWidth = 1000; // Above maximum

        vm.LeftPanelWidth.ShouldBe(800);
    }

    [Fact]
    public void RightPanelWidth_ClampsToMinimum200()
    {
        var vm = new RightPanelViewModel(_canvas, _preferencesService);

        vm.RightPanelWidth = 100; // Below minimum

        vm.RightPanelWidth.ShouldBe(200);
    }

    [Fact]
    public void RightPanelWidth_ClampsToMaximum800()
    {
        var vm = new RightPanelViewModel(_canvas, _preferencesService);

        vm.RightPanelWidth = 1000; // Above maximum

        vm.RightPanelWidth.ShouldBe(800);
    }

    [Fact]
    public void LeftPanelWidth_PersistsToPreferences()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LeftPanelWidth = 350;

        _preferencesService.GetLeftPanelWidth().ShouldBe(350);
    }

    [Fact]
    public void RightPanelWidth_PersistsToPreferences()
    {
        var vm = new RightPanelViewModel(_canvas, _preferencesService);

        vm.RightPanelWidth = 400;

        _preferencesService.GetRightPanelWidth().ShouldBe(400);
    }

    [Fact]
    public void LeftPanelWidth_RestoresFromPreferences()
    {
        // Set a custom width
        _preferencesService.SetLeftPanelWidth(300);

        // Create new ViewModel and initialize
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);
        vm.Initialize();

        vm.LeftPanelWidth.ShouldBe(300);
    }

    [Fact]
    public void RightPanelWidth_RestoresFromPreferences()
    {
        // Set a custom width
        _preferencesService.SetRightPanelWidth(450);

        // Create new ViewModel and initialize
        var vm = new RightPanelViewModel(_canvas, _preferencesService);
        vm.Initialize();

        vm.RightPanelWidth.ShouldBe(450);
    }

    [Fact]
    public void LeftPanelWidth_RaisesPropertyChanged()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);
        bool propertyChanged = false;

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.LeftPanelWidth))
                propertyChanged = true;
        };

        vm.LeftPanelWidth = 400;

        propertyChanged.ShouldBeTrue();
    }

    [Fact]
    public void RightPanelWidth_RaisesPropertyChanged()
    {
        var vm = new RightPanelViewModel(_canvas, _preferencesService);
        bool propertyChanged = false;

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.RightPanelWidth))
                propertyChanged = true;
        };

        vm.RightPanelWidth = 350;

        propertyChanged.ShouldBeTrue();
    }

    [Fact]
    public void BothPanels_CanHaveIndependentWidths()
    {
        var leftVm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);
        var rightVm = new RightPanelViewModel(_canvas, _preferencesService);

        leftVm.LeftPanelWidth = 300;
        rightVm.RightPanelWidth = 500;

        leftVm.LeftPanelWidth.ShouldBe(300);
        rightVm.RightPanelWidth.ShouldBe(500);
        _preferencesService.GetLeftPanelWidth().ShouldBe(300);
        _preferencesService.GetRightPanelWidth().ShouldBe(500);
    }

    [Fact]
    public void LeftPanelWidth_AcceptsValidWidthInRange()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LeftPanelWidth = 350;

        vm.LeftPanelWidth.ShouldBe(350);
    }

    [Fact]
    public void RightPanelWidth_AcceptsValidWidthInRange()
    {
        var vm = new RightPanelViewModel(_canvas, _preferencesService);

        vm.RightPanelWidth = 400;

        vm.RightPanelWidth.ShouldBe(400);
    }
}

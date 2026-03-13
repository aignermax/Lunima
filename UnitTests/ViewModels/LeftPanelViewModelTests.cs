using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Services;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_Core.Components.Creation;
using Shouldly;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for LeftPanelViewModel.
/// Tests scroll position preservation and component library management.
/// </summary>
public class LeftPanelViewModelTests
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly GroupLibraryManager _libraryManager;
    private readonly PdkLoader _pdkLoader;
    private readonly UserPreferencesService _preferencesService;

    public LeftPanelViewModelTests()
    {
        _canvas = new DesignCanvasViewModel();
        _libraryManager = new GroupLibraryManager();
        _pdkLoader = new PdkLoader();
        _preferencesService = new UserPreferencesService();
    }

    [Fact]
    public void LibraryScrollOffset_DefaultsToZero()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LibraryScrollOffset.ShouldBe(0.0);
    }

    [Fact]
    public void LibraryScrollOffset_CanBeSet()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LibraryScrollOffset = 123.5;

        vm.LibraryScrollOffset.ShouldBe(123.5);
    }

    [Fact]
    public void LibraryScrollOffset_RaisesPropertyChanged()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);
        bool propertyChanged = false;

        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(vm.LibraryScrollOffset))
                propertyChanged = true;
        };

        vm.LibraryScrollOffset = 100.0;

        propertyChanged.ShouldBeTrue();
    }

    [Fact]
    public void LibraryScrollOffset_PreservesValue()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LibraryScrollOffset = 250.75;
        var savedValue = vm.LibraryScrollOffset;

        savedValue.ShouldBe(250.75);
    }

    [Fact]
    public void LeftPanelWidth_ClampsToMinimum()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LeftPanelWidth = 50; // Below minimum of 200

        vm.LeftPanelWidth.ShouldBe(200);
    }

    [Fact]
    public void LeftPanelWidth_ClampsToMaximum()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.LeftPanelWidth = 1000; // Above maximum of 800

        vm.LeftPanelWidth.ShouldBe(800);
    }

    [Fact]
    public void Initialize_LoadsComponentLibrary()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);

        vm.Initialize();

        vm.AllTemplates.Count.ShouldBeGreaterThan(0);
        vm.FilteredTemplates.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void SearchText_FiltersComponents()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);
        vm.Initialize();

        var initialCount = vm.FilteredTemplates.Count;
        vm.SearchText = "Coupler";

        vm.FilteredTemplates.Count.ShouldBeLessThanOrEqualTo(initialCount);
        vm.FilteredTemplates.All(t =>
            t.Name.Contains("Coupler", StringComparison.OrdinalIgnoreCase) ||
            t.Category.Contains("Coupler", StringComparison.OrdinalIgnoreCase))
            .ShouldBeTrue();
    }

    [Fact]
    public void SearchText_ClearedRestoresAllComponents()
    {
        var vm = new LeftPanelViewModel(_canvas, _libraryManager, _pdkLoader, _preferencesService);
        vm.Initialize();

        var initialCount = vm.FilteredTemplates.Count;
        vm.SearchText = "test";
        vm.SearchText = "";

        vm.FilteredTemplates.Count.ShouldBe(initialCount);
    }
}

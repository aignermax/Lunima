using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Services;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP_Core.Components.Creation;
using Shouldly;

namespace UnitTests.ViewModels;

/// <summary>
/// Unit tests for LeftPanelViewModel.
/// Tests scroll position preservation and component library management.
/// Uses isolated test preferences to avoid polluting user settings.
/// </summary>
public class LeftPanelViewModelTests : IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly GroupLibraryManager _libraryManager;
    private readonly PdkLoader _pdkLoader;
    private readonly UserPreferencesService _preferencesService;
    private readonly string _testPreferencesPath;

    public LeftPanelViewModelTests()
    {
        _canvas = new DesignCanvasViewModel();
        _libraryManager = new GroupLibraryManager();
        _pdkLoader = new PdkLoader();

        // Use temporary file for test isolation
        _testPreferencesPath = Path.Combine(Path.GetTempPath(), $"test-preferences-{Guid.NewGuid()}.json");
        _preferencesService = new UserPreferencesService(_testPreferencesPath);
    }

    public void Dispose()
    {
        // Clean up test preferences file
        if (File.Exists(_testPreferencesPath))
        {
            File.Delete(_testPreferencesPath);
        }
    }

    /// <summary>Creates a LeftPanelViewModel with all required sub-VM dependencies.</summary>
    private LeftPanelViewModel CreateLeftPanelViewModel() =>
        new(_canvas, _libraryManager, _pdkLoader, _preferencesService,
            new HierarchyPanelViewModel(_canvas),
            new PdkManagerViewModel(),
            new ComponentLibraryViewModel(_libraryManager));

    [Fact]
    public void LibraryScrollOffset_DefaultsToZero()
    {
        var vm = CreateLeftPanelViewModel();

        vm.LibraryScrollOffset.ShouldBe(0.0);
    }

    [Fact]
    public void LibraryScrollOffset_CanBeSet()
    {
        var vm = CreateLeftPanelViewModel();

        vm.LibraryScrollOffset = 123.5;

        vm.LibraryScrollOffset.ShouldBe(123.5);
    }

    [Fact]
    public void LibraryScrollOffset_RaisesPropertyChanged()
    {
        var vm = CreateLeftPanelViewModel();
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
        var vm = CreateLeftPanelViewModel();

        vm.LibraryScrollOffset = 250.75;
        var savedValue = vm.LibraryScrollOffset;

        savedValue.ShouldBe(250.75);
    }

    [Fact]
    public void LeftPanelWidth_ClampsToMinimum()
    {
        var vm = CreateLeftPanelViewModel();

        vm.LeftPanelWidth = new Avalonia.Controls.GridLength(50); // Below minimum of 200

        vm.LeftPanelWidth.Value.ShouldBe(200);
    }

    [Fact]
    public void LeftPanelWidth_ClampsToMaximum()
    {
        var vm = CreateLeftPanelViewModel();

        vm.LeftPanelWidth = new Avalonia.Controls.GridLength(1000); // Above maximum of 800

        vm.LeftPanelWidth.Value.ShouldBe(800);
    }

    [Fact]
    public void Initialize_LoadsComponentLibrary()
    {
        var vm = CreateLeftPanelViewModel();

        vm.Initialize();

        vm.AllTemplates.Count.ShouldBeGreaterThan(0);
        vm.FilteredTemplates.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void SearchText_FiltersComponents()
    {
        var vm = CreateLeftPanelViewModel();
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
        var vm = CreateLeftPanelViewModel();
        vm.Initialize();

        var initialCount = vm.FilteredTemplates.Count;
        vm.SearchText = "test";
        vm.SearchText = "";

        vm.FilteredTemplates.Count.ShouldBe(initialCount);
    }
}

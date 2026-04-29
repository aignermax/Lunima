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

    [Fact]
    public void ResolveBundledPdkDirectory_PrefersRepoSourceWhenAvailable()
    {
        // Lay out the dev-build shape we want to detect:
        //   <root>/CAP.Avalonia/bin/Debug/net8.0/        ← simulated baseDir
        //   <root>/CAP-DataAccess/PDKs/demo.json         ← repo source we want
        var root = Path.Combine(Path.GetTempPath(), $"cap_pdkdir_{Guid.NewGuid():N}");
        var baseDir = Path.Combine(root, "CAP.Avalonia", "bin", "Debug", "net8.0");
        var repoPdks = Path.Combine(root, "CAP-DataAccess", "PDKs");
        var bundledPdks = Path.Combine(baseDir, "PDKs");
        try
        {
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(repoPdks);
            Directory.CreateDirectory(bundledPdks);
            File.WriteAllText(Path.Combine(repoPdks, "demo.json"), "{}");
            File.WriteAllText(Path.Combine(bundledPdks, "demo.json"), "{}");

            var resolved = LeftPanelViewModel.ResolveBundledPdkDirectory(baseDir);

            // Edits saved through the editor must land in the repo copy so they
            // get committed — the bundled-next-to-exe copy is a build artefact.
            Path.GetFullPath(resolved!).ShouldBe(Path.GetFullPath(repoPdks));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolveBundledPdkDirectory_FallsBackToBundledWhenRepoMissing()
    {
        // Deployed-build shape: no CAP-DataAccess sibling exists, only the
        // bundled PDKs folder next to the executable.
        var root = Path.Combine(Path.GetTempPath(), $"cap_pdkdir_{Guid.NewGuid():N}");
        var baseDir = Path.Combine(root, "app");
        var bundledPdks = Path.Combine(baseDir, "PDKs");
        try
        {
            Directory.CreateDirectory(bundledPdks);
            File.WriteAllText(Path.Combine(bundledPdks, "demo.json"), "{}");

            var resolved = LeftPanelViewModel.ResolveBundledPdkDirectory(baseDir);

            Path.GetFullPath(resolved!).ShouldBe(Path.GetFullPath(bundledPdks));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolveBundledPdkDirectory_ReturnsNullWhenNothingExists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cap_pdkdir_{Guid.NewGuid():N}");
        var baseDir = Path.Combine(root, "app");
        try
        {
            Directory.CreateDirectory(baseDir);

            LeftPanelViewModel.ResolveBundledPdkDirectory(baseDir).ShouldBeNull();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ResolveBundledPdkDirectory_IgnoresEmptyRepoFolder()
    {
        // A bare CAP-DataAccess/PDKs without any *.json must not be treated as
        // the source of truth — it would mask the bundled copy and leave the
        // editor with nothing to load.
        var root = Path.Combine(Path.GetTempPath(), $"cap_pdkdir_{Guid.NewGuid():N}");
        var baseDir = Path.Combine(root, "CAP.Avalonia", "bin", "Debug", "net8.0");
        var emptyRepoPdks = Path.Combine(root, "CAP-DataAccess", "PDKs");
        var bundledPdks = Path.Combine(baseDir, "PDKs");
        try
        {
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(emptyRepoPdks);
            Directory.CreateDirectory(bundledPdks);
            File.WriteAllText(Path.Combine(bundledPdks, "demo.json"), "{}");

            var resolved = LeftPanelViewModel.ResolveBundledPdkDirectory(baseDir);

            Path.GetFullPath(resolved!).ShouldBe(Path.GetFullPath(bundledPdks));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

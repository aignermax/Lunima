using CAP.Avalonia;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.ViewModels.Properties;
using CAP_Core.Export;
using CAP_Core.Solvers.Fdtd;
using CAP_Core.Solvers.ModeSolver;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UnitTests.Helpers;
using Avalonia.Headless.XUnit;
using Xunit;

namespace UnitTests.Architecture;

/// <summary>
/// Builds the real production DI container (<see cref="App.ConfigureServices"/>) and
/// resolves the services that the vertical-slice refactor moved into per-feature
/// extension methods. A missing or misplaced registration would otherwise only surface
/// as a crash on app start. Other tests exercise the same container via
/// <see cref="ProductionContainerTestHelper"/> for narrower composition-root guarantees
/// (e.g. <c>RoutingSettingsPersistenceTests</c>); <c>UiScreenshotTests</c> deliberately use
/// a test-only app + VM helper instead.
///
/// Only POCO/solver/preview services are resolved here: they have no Avalonia-runtime
/// dependency, so they construct cleanly in a headless test.
/// </summary>
public class AppDiContainerTests
{
    [Fact]
    public async Task Container_PythonEnvironmentPortSharesTheSelectionOwner()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());
        sp.GetRequiredService<CAP_Core.Export.Contracts.IPythonEnvironmentRegistry>()
            .ShouldBeSameAs(sp.GetRequiredService<CAP_Core.Export.PythonEnvironmentManager.PythonEnvironmentRegistry>());
        sp.GetRequiredService<CAP_Core.Export.Contracts.IPythonEnvironmentHealth>()
            .ShouldBeSameAs(sp.GetRequiredService<CAP_Core.Export.PythonEnvironmentManager.EnvironmentHealthChecker>());
        sp.GetRequiredService<CAP_Core.Export.Contracts.IManagedEnvironmentPackages>()
            .ShouldBeSameAs(sp.GetRequiredService<CAP_Core.Export.PythonEnvironmentManager.ManagedEnvironmentPackageSetup>());
    }

    [Fact]
    public async Task Container_CoordinatePortsShareTheConfiguredServices()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());
        sp.GetRequiredService<CAP_Core.Export.Contracts.IGdsCoordinateExtractor>()
            .ShouldBeSameAs(sp.GetRequiredService<GdsCoordinateExtractor>());
        sp.GetRequiredService<CAP_Core.Export.Contracts.IGdsCoordinateComparer>()
            .ShouldBeSameAs(sp.GetRequiredService<GdsCoordinateComparisonService>());
        var extraction = sp.GetRequiredService<CAP.Avalonia.ViewModels.Export.GdsCoordExtractViewModel>();
        var comparison = sp.GetRequiredService<CAP.Avalonia.ViewModels.Diagnostics.GdsCoordinateComparisonViewModel>();
        var extractionStatus = extraction.StatusText;
        var comparisonStatus = comparison.ComparisonStatus;
        await sp.GetRequiredService<CAP.Avalonia.Services.Lifetime.ApplicationWorkCoordinator>().StopAsync();
        await extraction.ExtractCoordinatesAsync();
        await comparison.RunComparisonAsync();
        extraction.StatusText.ShouldBe(extractionStatus);
        comparison.ComparisonStatus.ShouldBe(comparisonStatus);
    }

    private static string NewTempPreferencesPath() =>
        Path.Combine(Path.GetTempPath(), $"lunima-di-test-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Container_FeaturePreferencesShareThePersistedUserStore()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());
        var preferences = sp.GetRequiredService<CAP.Avalonia.Services.UserPreferencesService>();

        sp.GetRequiredService<CAP_Core.Preferences.IPdkLibraryPreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IPanelLayoutPreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IPythonInterpreterPreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IFdtdPreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IAgentBridgePreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IAgentConnectionPromptPreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IAiPreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IChipSizePreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IInterconnectPreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.ILanguagePreferences>().ShouldBeSameAs(preferences);
    }

    [Fact]
    public async Task Container_GdsContractsShareInterpreterAndEnvironmentCacheOwner()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());
        var export = sp.GetRequiredService<GdsExportService>();

        sp.GetRequiredService<CAP_Core.Export.Contracts.IGdsScriptRunner>().ShouldBeSameAs(export);
        sp.GetRequiredService<CAP_Core.Export.Contracts.IGdsExportSession>().ShouldBeSameAs(export);
        sp.GetRequiredService<CAP_Core.Export.Contracts.IPythonDiscovery>()
            .ShouldBeSameAs(sp.GetRequiredService<PythonDiscoveryService>());
    }

    [Fact]
    public async Task Container_HomeAndLearningPreferencesShareThePersistedUserStore()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());
        var preferences = sp.GetRequiredService<CAP.Avalonia.Services.UserPreferencesService>();

        sp.GetRequiredService<CAP_Core.Preferences.IHomePreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.ILearningPreferences>().ShouldBeSameAs(preferences);
        sp.GetRequiredService<CAP_Core.Preferences.IRecentProjectPreferences>().ShouldBeSameAs(preferences);
    }

    [Fact]
    public async Task Container_LibraryAndTemplateModelsShareTheExistingStoreInstances()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());

        sp.GetRequiredService<CAP_Core.Components.Creation.IGroupLibrary>()
            .ShouldBeSameAs(sp.GetRequiredService<CAP_Core.Components.Creation.GroupLibraryManager>());
        var models = sp.GetRequiredService<CAP_DataAccess.Persistence.PIR.ITemplateSMatrixStore>();
        var store = sp.GetRequiredService<CAP.Avalonia.Services.UserSMatrixOverrideStore>();
        models.ShouldBeSameAs(store);
        models.Overrides.ShouldBeSameAs(store.Overrides);
    }

    [Fact]
    public async Task Container_ResolvesRedistributedSolverAndPreviewServices()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());

        sp.GetRequiredService<IFdtdSMatrixService>().ShouldNotBeNull();
        sp.GetRequiredService<IModeSolverService>().ShouldNotBeNull();
        sp.GetRequiredService<GdsPreviewRenderService>().ShouldNotBeNull();
        sp.GetRequiredService<NazcaComponentPreviewService>().ShouldNotBeNull();
        sp.GetRequiredService<ComponentEditorFactory>().ShouldNotBeNull();
    }

    [AvaloniaFact]
    public async Task Container_GdsImportWidthProvider_ResolvesThroughTheMainViewModel()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());
        var import = sp.GetRequiredService<CAP.Avalonia.Services.GdsImport.GdsImportService>();

        // Evaluated lazily at import time: a lookup of a type the container does not hold
        // (FileOperationsViewModel is owned by MainViewModel) surfaced as "GDS import failed".
        Should.NotThrow(() => import.ResolveProcessDefaultWidthUm());
    }

    [Fact]
    public async Task Container_ResolvesFdtdBackendsAndRegistry()
    {
        await using var sp = ProductionContainerTestHelper.BuildWithTempPreferences(NewTempPreferencesPath());

        // The NewComponent editor path keeps the local/free backend.
        sp.GetRequiredService<IFdtdSMatrixService>()
            .ShouldBeOfType<CAP.Avalonia.Services.Solvers.DockerFdtdSMatrixService>();
        sp.GetRequiredService<CAP.Avalonia.Services.Solvers.Tidy3dSMatrixService>().ShouldNotBeNull();

        var registry = sp.GetRequiredService<CAP.Avalonia.Services.Solvers.FdtdBackendRegistry>();
        registry.GetService(FdtdBackendType.MeepDocker)
            .ShouldBeOfType<CAP.Avalonia.Services.Solvers.DockerFdtdSMatrixService>();
        registry.GetService(FdtdBackendType.Tidy3D)
            .ShouldBeOfType<CAP.Avalonia.Services.Solvers.Tidy3dSMatrixService>();
    }
}

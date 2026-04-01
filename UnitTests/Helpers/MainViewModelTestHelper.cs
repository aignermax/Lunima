using System.Net.Http;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Update;
using CAP.Avalonia.ViewModels.AI;
using CAP_Core.Components.Creation;
using CAP_Core.Export;
using CAP_DataAccess.Components.ComponentDraftMapper;
using Moq;

namespace UnitTests.Helpers;

/// <summary>
/// Factory helpers for creating <see cref="MainViewModel"/> instances in tests.
/// Provides minimal but valid dependencies for all sub-ViewModels.
/// </summary>
public static class MainViewModelTestHelper
{
    /// <summary>
    /// Creates a fully wired <see cref="MainViewModel"/> with default test dependencies.
    /// </summary>
    public static MainViewModel CreateMainViewModel(
        SimulationService? simulationService = null,
        CommandManager? commandManager = null,
        UserPreferencesService? preferencesService = null,
        GroupLibraryManager? libraryManager = null,
        DesignCanvasViewModel? canvas = null)
    {
        canvas ??= new DesignCanvasViewModel();
        commandManager ??= new CommandManager();
        preferencesService ??= new UserPreferencesService();
        libraryManager ??= new GroupLibraryManager();
        simulationService ??= new SimulationService();

        var pdkLoader = new PdkLoader();
        var leftPanel = CreateLeftPanelViewModel(canvas, libraryManager, pdkLoader, preferencesService, commandManager);
        var rightPanel = CreateRightPanelViewModel(canvas, preferencesService);
        var bottomPanel = CreateBottomPanelViewModel(canvas, commandManager);

        return new MainViewModel(
            canvas,
            simulationService,
            new SimpleNazcaExporter(),
            commandManager,
            preferencesService,
            new GroupPreviewGenerator(),
            Mock.Of<IInputDialogService>(),
            new GdsExportService(),
            new CAP_Core.ErrorConsoleService(),
            leftPanel,
            rightPanel,
            bottomPanel);
    }

    /// <summary>
    /// Creates a <see cref="LeftPanelViewModel"/> with all required sub-VM dependencies.
    /// </summary>
    public static LeftPanelViewModel CreateLeftPanelViewModel(
        DesignCanvasViewModel? canvas = null,
        GroupLibraryManager? libraryManager = null,
        PdkLoader? pdkLoader = null,
        UserPreferencesService? preferencesService = null,
        CommandManager? commandManager = null)
    {
        canvas ??= new DesignCanvasViewModel();
        libraryManager ??= new GroupLibraryManager();
        pdkLoader ??= new PdkLoader();
        preferencesService ??= new UserPreferencesService();

        return new LeftPanelViewModel(
            canvas,
            libraryManager,
            pdkLoader,
            preferencesService,
            new HierarchyPanelViewModel(canvas),
            new PdkManagerViewModel(),
            new ComponentLibraryViewModel(libraryManager));
    }

    /// <summary>
    /// Creates a <see cref="RightPanelViewModel"/> with all required sub-VM dependencies.
    /// </summary>
    public static RightPanelViewModel CreateRightPanelViewModel(
        DesignCanvasViewModel? canvas = null,
        UserPreferencesService? preferencesService = null)
    {
        canvas ??= new DesignCanvasViewModel();
        preferencesService ??= new UserPreferencesService();

        var httpClient = new HttpClient();
        var updateChecker = new UpdateChecker(httpClient, "aignermax", "Connect-A-PIC-Pro");
        var updateDownloader = new UpdateDownloader(httpClient);
        var updateVm = new UpdateViewModel(
            updateChecker,
            updateDownloader,
            preferencesService,
            Mock.Of<IUrlLauncher>());

        return new RightPanelViewModel(
            canvas,
            preferencesService,
            new ParameterSweepViewModel(),
            new RoutingDiagnosticsViewModel(),
            new DesignValidationViewModel(),
            new ComponentDimensionDiagnosticsViewModel(canvas),
            new ComponentDimensionViewModel(),
            new ExportValidationViewModel(),
            new SMatrixPerformanceViewModel(),
            new CompressLayoutViewModel(),
            new GroupSMatrixViewModel(),
            new ArchitectureReportViewModel(),
            new PdkConsistencyViewModel(),
            updateVm,
            new AiAssistantViewModel(Mock.Of<IAiService>(), preferencesService));
    }

    /// <summary>
    /// Creates a <see cref="BottomPanelViewModel"/> with all required sub-VM dependencies.
    /// </summary>
    public static BottomPanelViewModel CreateBottomPanelViewModel(
        DesignCanvasViewModel? canvas = null,
        CommandManager? commandManager = null)
    {
        canvas ??= new DesignCanvasViewModel();
        commandManager ??= new CommandManager();
        var errorConsoleService = new CAP_Core.ErrorConsoleService();

        return new BottomPanelViewModel(
            canvas,
            commandManager,
            new WaveguideLengthViewModel(),
            new ElementLockViewModel(),
            new ErrorConsoleViewModel(errorConsoleService));
    }
}

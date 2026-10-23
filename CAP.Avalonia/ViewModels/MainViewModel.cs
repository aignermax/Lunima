using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components.Core;
using CAP_Core;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP.Avalonia.Services;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Simulation;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Export.Formats;
using CAP.Avalonia.ViewModels.Home;
using CAP.Avalonia.ViewModels.Update;
using CAP_Core.Export;
using CAP.Avalonia.ViewModels.PdkOffset;
using CAP_Core.Components.Process;
using CAP_DataAccess.Persistence.PIR;
using CAP_Core.Preferences;

namespace CAP.Avalonia.ViewModels;

/// <summary>
/// Shell bindings and commands; feature coordinators own process, selection, checks and learning workflows.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable, Services.Lifetime.IApplicationShellLifetime
{
    private readonly Shell.ProcessRoutingContext _processContext;
    private readonly Shell.SelectionPanelCoordinator _selectionPanels;
    private readonly Shell.LibraryInteractionCoordinator _libraryInteraction;
    private readonly Shell.DesignCheckRunCoordinator _designChecks;
    private readonly Shell.DesignCheckInteractionCoordinator _designCheckInteraction;
    private readonly Onboarding.LearningJourneys.LearningJourneysCoordinator _learning;
    private readonly Shell.ProjectProcessCoordinator _projectProcess;
    private readonly Shell.ShellActivationCoordinator _activation;
    private readonly Shell.ShellStateCoordinator _shellState;
    private IDisposable? _localizationSubscription;
    private Task? _retirement;
    private readonly Services.Lifetime.ShellWorkScope _work = new();
    private readonly Services.Lifetime.CallbackBindings _bindings = new();
    private readonly bool _ownsLayerVisibility;
    private bool _disposed;

    public Onboarding.LearningPath.LearningPathViewModel LearningPath => _learning.Path;
    public Onboarding.FirstStepsTutorial.ManufacturingTourViewModel ManufacturingTour => _learning.Manufacturing;
    public bool ShowLearningSuccess { get => _learning.ShowSuccess; set => _learning.Guidance.ShowSuccess = value; }

    private void LearningStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(Onboarding.LearningJourneys.LearningJourneysCoordinator.ShowSuccess))
            OnPropertyChanged(nameof(ShowLearningSuccess));
    }

    private void DesignCheckStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(Shell.DesignCheckRunCoordinator.IsRunning))
            OnPropertyChanged(nameof(IsRunningDesignChecks));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = StopAsync();
        _shellState.PropertyChanged -= ShellStateChanged;
        _shellState.Dispose();
        _localizationSubscription?.Dispose();
        UnwireDies();
        UnwireWorkspaces();
        UnwireWorkspaceContext();
        UnwireWorkspacePresentation();
        UnwireWorkspaceToolSets();
        UnwireMakeGate();
        CanvasInteraction.PropertyChanged -= OnInteractionModeChangedForGateView;
        RightPanel.Logic.PropertyChanged -= OnLogicPanelChangedForRun;
        StatusBar.Dispose();
        BlockedConnections.Dispose();
        GatesPanel.Dispose();
        _bindings.Dispose();
        if (_ownsLayerVisibility) LayerVisibility.Dispose();
        _selectionPanels.Dispose();
        _libraryInteraction.Dispose();
        _designCheckInteraction.Dispose();
        _designChecks.PropertyChanged -= DesignCheckStateChanged;
        _learning.PropertyChanged -= LearningStateChanged;
        _learning.Dispose();
        CanvasInteraction.Dispose();
        Canvas.DisposeDocumentBindings();
        FileOperations.Dispose();
    }

    public Task ActivateAsync(CancellationToken cancellationToken = default) => _activation.ActivateAsync(cancellationToken);
    public Task StopAsync() => _retirement ??= Task.WhenAll(
        _activation.StopAsync(), _liveSimulation.StopAsync(), _work.StopAsync(),
        Canvas.Document.RetireAsync(), ModeProbe?.StopAsync() ?? Task.CompletedTask,
        _designChecks.StopAsync(), GateView.StopAsync(), RunHint.StopAsync(), Update.StopAsync(),
        RightPanel.OnaAnalysis.StopAsync(), BottomPanel.Analysis.Spectrum.StopAsync(), RightPanel.Logic.StopAsync());
    public async ValueTask DisposeAsync() { try { await StopAsync(); } finally { Dispose(); } }

    [ObservableProperty]
    private DesignCanvasViewModel _canvas;

    [ObservableProperty]
    private string _statusText = LocalizationService.Instance.Translate("Status.Ready");

    /// <summary>Re-translate keyed status only while its original text remains visible, preserving transient messages.</summary>
    private (string Key, object[] Args, string Formatted)? _lastLocalizedStatus =
        ("Status.Ready", [], LocalizationService.Instance.Translate("Status.Ready"));

    /// <summary>Open-file name and dirty marker, or Untitled for an unsaved design.</summary>
    public string WindowTitle { get => _shellState.WindowTitle; set => _shellState.WindowTitle = value; }

    private void ShellStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs change)
    {
        if (change.PropertyName == nameof(Shell.ShellStateCoordinator.WindowTitle)) OnPropertyChanged(nameof(WindowTitle));
    }

    /// <summary>Toolbar label refreshed from the active design or die process.</summary>
    [ObservableProperty]
    private string _activeProcessLabel = LocalizationService.Instance.Translate("Process.NoneSelected");

    /// <summary>Tooltip of the process chip; in a die assembly it lists every die with its process.</summary>
    [ObservableProperty]
    private string _activeProcessTip = LocalizationService.Instance.Translate("StatusBar.ActiveProcessTip");

    /// <summary>Enables the toolbar warning when the process permits mixing PDKs.</summary>
    [ObservableProperty]
    private bool _isPlayground;

    /// <summary>Active simulation mode; the toolbar selector binds here and Run(L) dispatches on it.</summary>
    [ObservableProperty]
    private CAP.Avalonia.ViewModels.Analysis.SimulationMode _simulationMode = CAP.Avalonia.ViewModels.Analysis.SimulationMode.Cw;

    /// <summary>Toolbar ComboBox index: CW = 0, transient = 1.</summary>
    public int SimulationModeIndex
    {
        get => (int)SimulationMode;
        set => SimulationMode = (CAP.Avalonia.ViewModels.Analysis.SimulationMode)value;
    }

    /// <summary>Keeps <see cref="SimulationModeIndex"/> in sync when <see cref="SimulationMode"/> changes.</summary>
    partial void OnSimulationModeChanged(CAP.Avalonia.ViewModels.Analysis.SimulationMode value)
    {
        OnPropertyChanged(nameof(SimulationModeIndex));

        // Surface Transient mode to the canvas so laser on/off icons stay visible
        // and clickable during the transient/eye workflow (#690) — Transient mode
        // deliberately clears the CW ShowPowerFlow overlay.
        if (_canvas != null)
            _canvas.IsTransientModeActive = value == CAP.Avalonia.ViewModels.Analysis.SimulationMode.Transient;
    }

    public Commands.CommandManager CommandManager { get; }
    public SimulationService Simulation { get; }

    /// <summary>Owned canvas interaction facade for selection, placement and connections.</summary>
    public CanvasInteractionViewModel CanvasInteraction { get; }

    /// <summary>Selection actions that expose embedded imported GDS children.</summary>
    public GdsImport.GdsHierarchyActionsViewModel ImportedHierarchy { get; }

    /// <summary>Left icon rail: which section (library, hierarchy, PDKs) fills the left pane.</summary>
    public Shell.LeftRailViewModel LeftRail { get; } = new();

    /// <summary>Status-bar mode and selection fields.</summary>
    public Shell.StatusBarViewModel StatusBar { get; private set; } = null!;

    /// <summary>Status-bar warning field: blocked connections and the jump to the next one.</summary>
    public Shell.BlockedConnectionsViewModel BlockedConnections { get; private set; } = null!;

    /// <summary>Owned project persistence and export facade.</summary>
    public FileOperationsViewModel FileOperations { get; }

    /// <summary>Startup and recent-project navigation.</summary>
    public HomeViewModel Home { get; }

    public ViewModels.Onboarding.FirstStepsTutorial.TutorialViewModel Tutorial => _learning.Chapters.Tutorial;

    public ViewModels.Onboarding.FirstStepsTutorial.WatchComputeTourViewModel WatchTour => _learning.Chapters.Watch;

    public ViewModels.Onboarding.FirstStepsTutorial.FirstGateTourViewModel FirstGateTour => _learning.Chapters.FirstGate;

    /// <summary>Optional startup prompt for connecting the agent bridge.</summary>
    public ViewModels.Settings.AgentBridge.ClaudeCodeConnectPromptViewModel? ClaudeConnectPrompt { get; }

    public ViewModels.Onboarding.FirstStepsTutorial.RunProgramTourViewModel RunProgramTour => _learning.Chapters.Program;

    public ViewModels.Onboarding.FirstStepsTutorial.ConnectChipletsTourViewModel ConnectChipletsTour => _learning.Chapters.Chiplets;

    public ViewModels.Onboarding.FirstStepsTutorial.StoreNumberTourViewModel StoreNumberTour => _learning.Chapters.Memory;

    /// <summary>One-shot command-line design path; takes precedence over reopening the last project.</summary>
    public string? StartupDesignFile { get; set; }

    /// <summary>Canvas viewport navigation and zoom.</summary>
    public ViewportControlViewModel ViewportControl { get; }

    /// <summary>Optional mode-solver probe flyout.</summary>
    public ViewModels.Solvers.ModeProbe.ModeProbeViewModel? ModeProbe { get; }

    /// <summary>Component library and PDK management panel.</summary>
    public LeftPanelViewModel LeftPanel { get; }

    /// <summary>Self-contained GDS import flow exposed by the library button.</summary>
    public ViewModels.GdsImport.GdsImportButtonViewModel GdsImport { get; }

    /// <summary>Analysis, diagnostics and validation panels.</summary>
    public RightPanelViewModel RightPanel { get; }

    /// <summary>Analysis dock and connection property panels.</summary>
    public BottomPanelViewModel BottomPanel { get; }

    /// <summary>Photonic component registry browser shown in its own tool window.</summary>
    public ViewModels.ComponentRegistry.RegistryBrowser.RegistryBrowserViewModel Registry { get; }

    /// <summary>Update status shared by the main banner and Settings.</summary>
    public UpdateViewModel Update { get; }

    /// <summary>View callback opening Settings at the optional page type; null selects the default page.</summary>
    public Func<Type?, Task>? ShowSettingsWindowAsync { get; set; }

    /// <summary>Unified menu over the registered export formats.</summary>
    public ExportMenuViewModel ExportMenu { get; }

    /// <summary>PhotonTorch format — exposes <c>ShowOptionsDialogAsync</c> for code-behind wiring.</summary>
    public PhotonTorchExportFormat PhotonTorchExportFormat { get; private set; } = null!;

    /// <summary>gdsfactory format — exposes <c>ShowOptionsDialogAsync</c> for code-behind wiring.</summary>
    public GdsFactoryExportFormat GdsFactoryExportFormat { get; private set; } = null!;

    /// <summary>gdsfactory export options/executor ViewModel (dialog DataContext).</summary>
    public ViewModels.Export.GdsFactoryExportViewModel GdsFactoryExport { get; private set; } = null!;

    /// <summary>Verilog-A format — exposes <c>ShowOptionsDialogAsync</c> for code-behind wiring.</summary>
    public VerilogAExportFormat VerilogAExportFormat { get; private set; } = null!;

    public IFileDialogService? FileDialogService
    {
        get => FileOperations.FileDialogService;
        set
        {
            FileOperations.FileDialogService = value;
            FileOperations.PhotonTorchExport.FileDialogService = value;
            FileOperations.VerilogAExport.FileDialogService = value;
            GdsFactoryExport.FileDialogService = value;
            LeftPanel.FileDialogService = value;
            GdsImport.FileDialogService = value;
        }
    }

    private readonly Services.IUrlLauncher _urlLauncher;

    /// <summary>Toolbar busy indicator while asynchronous simulation is running.</summary>
    [ObservableProperty]
    private bool _isSimulating;

    /// <summary>PDK offset editor shared with the view-layer dialog wiring.</summary>
    public PdkOffsetEditorViewModel PdkOffsetEditor { get; }

    /// <summary>Canvas geometry preview service and repaint notifications.</summary>
    public GdsPreviewRenderService GdsPreviewRenderService { get; }

    /// <summary>Optional application-lifetime crossing-insertion binder; DI owns its lifetime.</summary>
    public ViewModels.Canvas.CrossingInsertion.CrossingInsertionCanvasBinder? CrossingInsertionBinder { get; }

    /// <summary>Persistent error reporting shared by panels and view-layer dialog wiring.</summary>
    public ErrorConsoleService ErrorConsole { get; }

    /// <summary>Chip bounds shared by Settings, persistence and design checks.</summary>
    public ViewModels.Canvas.ChipSizeViewModel ChipSize { get; }

    /// <summary>Per-design imported-layer visibility shared by canvas rendering and the Layers panel.</summary>
    public ViewModels.GdsImport.LayerVisibility.GdsLayerVisibilityViewModel LayerVisibility { get; }

    public MainViewModel(
        CAP_DataAccess.Persistence.NativeDesign.INativeDesignFileStore designFiles,
        DesignCanvasViewModel canvas,
        SimulationService simulationService,
        SimpleNazcaExporter nazcaExporter,
        SaxExporter saxExporter,
        Commands.CommandManager commandManager,
        IRecentProjectPreferences recentProjectPreferences,
        IHomePreferences homePreferences,
        ILearningPreferences learningPreferences,
        Services.GroupPreviewGenerator previewGenerator,
        Services.IInputDialogService inputDialogService,
        ErrorConsoleService errorConsoleService,
        GdsExportViewModel gdsExportViewModel,
        UpdateViewModel updateViewModel,
        LeftPanelViewModel leftPanel,
        RightPanelViewModel rightPanel,
        BottomPanelViewModel bottomPanel,
        ViewportControlViewModel viewportControl,
        PdkOffsetEditorViewModel pdkOffsetEditor,
        ViewModels.Export.PhotonTorchExportViewModel photonTorchExport,
        ViewModels.Export.VerilogAExportViewModel verilogAExport,
        ViewModels.Export.GdsFactoryExportViewModel gdsFactoryExport,
        ViewModels.Canvas.ChipSizeViewModel chipSize,
        ITemplateSMatrixStore userSMatrixOverrideStore,
        GdsPreviewRenderService gdsPreviewRenderService,
        ViewModels.ComponentRegistry.RegistryBrowser.RegistryBrowserViewModel registryBrowser,
        ViewModels.GdsImport.GdsImportButtonViewModel gdsImportButton,
        Services.IUrlLauncher? urlLauncher = null,
        Services.IAiGridService? aiGridService = null,
        Services.RecentProjectsService? recentProjectsService = null,
        HomeViewModel? homeViewModel = null,
        ViewModels.Canvas.CrossingInsertion.CrossingInsertionCanvasBinder? crossingInsertionBinder = null,
        ViewModels.Solvers.ModeProbe.ModeProbeViewModel? modeProbe = null,
        Services.GdsImport.DesignScope.DesignScopedGdsComponentService? designScopedGdsComponents = null,
        ViewModels.GdsImport.LayerVisibility.GdsLayerVisibilityViewModel? layerVisibility = null,
        ViewModels.Onboarding.LearningJourneys.LearningJourneyChapters? learningChapters = null,
        ViewModels.Settings.AgentBridge.ClaudeCodeConnectPromptViewModel? claudeConnectPrompt = null,
        ViewModels.Export.PythonEnvironmentManager.PythonStartupViewModel? pythonStartup = null,
        Services.GdsImport.GdsHierarchyExpansionService? hierarchyExpansion = null)
    {
        _urlLauncher = urlLauncher ?? Services.PlatformShellLauncher.CreateDefault();
        // Injected for activation: constructing the binder wires the adaptive
        // crossing-insertion service (Issue #553) into the canvas' connection manager.
        CrossingInsertionBinder = crossingInsertionBinder;
        Simulation = simulationService;
        CommandManager = commandManager;
        _canvas = canvas;
        PdkOffsetEditor = pdkOffsetEditor;
        GdsPreviewRenderService = gdsPreviewRenderService;
        ErrorConsole = errorConsoleService;
        ChipSize = chipSize;
        InitializeLiveSimulation();
        Update = updateViewModel;
        ClaudeConnectPrompt = claudeConnectPrompt;
        PythonStartup = pythonStartup;

        // Wire panel ViewModels (injected via DI)
        LeftPanel = leftPanel;
        RightPanel = rightPanel;
        BottomPanel = bottomPanel;
        Registry = registryBrowser;

        GdsImport = gdsImportButton;

        CanvasInteraction = new CanvasInteractionViewModel(_canvas, commandManager, LeftPanel.ComponentLibrary, previewGenerator, inputDialogService, errorConsoleService);

        var recentProjects = recentProjectsService ?? new Services.RecentProjectsService(recentProjectPreferences);
        FileOperations = new FileOperationsViewModel(designFiles, _canvas, commandManager, nazcaExporter, saxExporter, LeftPanel.AllTemplates, gdsExportViewModel, photonTorchExport, verilogAExport, errorConsoleService, userSMatrixOverrideStore, recentProjects: recentProjects);
        var files = FileOperations;
        _processContext = new Shell.ProcessRoutingContext(Canvas, LeftPanel, () => files.ActiveProcess);
        // Design-scoped GDS imports (#830): save embeds them in the .lun, load
        // restores/migrates them, New Project clears them.
        FileOperations.DesignScopedGdsComponents = designScopedGdsComponents;
        ImportedHierarchy = new GdsImport.GdsHierarchyActionsViewModel(_canvas, commandManager,
            designScopedGdsComponents is null ? null : hierarchyExpansion ??
                throw new ArgumentNullException(nameof(hierarchyExpansion), "A design-scoped import requires hierarchy expansion."))
        { ReportStatus = text => StatusText = text };
        // Per-layer visibility of imported GDS geometry (#858): edits mark the
        // design dirty, and save/load/new-project round-trip through the .lun.
        LayerVisibility = layerVisibility ?? new ViewModels.GdsImport.LayerVisibility.GdsLayerVisibilityViewModel(_canvas);
        _ownsLayerVisibility = layerVisibility == null;
        _bindings.Bind(() => LayerVisibility.SettingsEdited, value => LayerVisibility.SettingsEdited = value,
            () => FileOperations.HasUnsavedChanges = true);
        FileOperations.LayerVisibility = LayerVisibility;
        ViewportControl = viewportControl;

        // Home screen: shown at startup; delegates project I/O to FileOperations
        // and dismisses itself once a project is opened or created.
        Home = homeViewModel ?? new HomeViewModel(recentProjects, homePreferences);
        _bindings.Bind(() => Home.NewProjectRequested, value => Home.NewProjectRequested = value,
            async () => await FileOperations.NewProjectCommand.ExecuteAsync(null));
        _bindings.Bind(() => Home.OpenProjectRequested, value => Home.OpenProjectRequested = value,
            async () => await FileOperations.LoadDesignCommand.ExecuteAsync(null));
        _bindings.Bind(() => Home.OpenProjectFromPathRequested, value => Home.OpenProjectFromPathRequested = value, FileOperations.LoadDesignFromPathAsync);
        _bindings.Bind(() => Home.OpenExampleRequested, value => Home.OpenExampleRequested = value, FileOperations.OpenDesignAsCopyAsync);
        WorkspaceSwitch = InitializeWorkspaces();
        InitializeWorkspaceContext();
        InitializeWorkspacePresentation();
        InitializeGateView();
        InitializeRunHint();
        InitializeWorkspaceToolSets();
        var chapters = learningChapters ?? Onboarding.LearningJourneys.LearningJourneyChapters.Create(
            Canvas, RightPanel, BottomPanel, CommandManager);
        chapters.Attach(CanvasInteraction, LeftPanel, WorkspaceSwitch, LeftRail, BottomPanel, SelectGroupAndOpenMakeGate);
        _learning = new Onboarding.LearningJourneys.LearningJourneysCoordinator(chapters, Canvas, FileOperations,
            Home, _processContext, WorkspaceSwitch, ViewportControl, LeftRail, learningPreferences);
        _learning.PropertyChanged += LearningStateChanged;
        _projectProcess = new Shell.ProjectProcessCoordinator(FileOperations, _processContext, _learning.Guidance);
        _bindings.Bind(() => FileOperations.ProjectOpened, value => FileOperations.ProjectOpened = value, OnProjectOpened);
        WireMakeGate();

        // Build the unified Export menu (add new IExportFormat here for new formats)
        PhotonTorchExportFormat = new PhotonTorchExportFormat();
        VerilogAExportFormat = new VerilogAExportFormat(verilogAExport);
        GdsFactoryExportFormat = new GdsFactoryExportFormat();
        GdsFactoryExport = gdsFactoryExport;

        FileOperations.MetalRoutingSpecProvider = _processContext.MetalSpec;
        GdsFactoryExport.MetalRoutingSpecProvider = _processContext.MetalSpec;
        Canvas.WaveguideCompanionSpecProvider = _processContext.CompanionLayers.Resolve;
        CanvasInteraction.GetMinBendRadiusMicrometers = _processContext.MinBendRadius;
        Canvas.Routing.GetProcessMinBendRadiusMicrometers = _processContext.MinBendRadius;
        Canvas.Routing.GetMetalRoutingSpec = _processContext.MetalSpec;
        CanvasInteraction.GetMetalMinBendRadiusMicrometers = () => _processContext.MetalSpec().MinBendRadiusMicrometers;
        Canvas.Routing.BuildConnectionProcessFloorProvider = _processContext.BuildConnectionFloorProvider;
        // Let a Nazca export that hits gdsfactory-native components hand off to the gdsfactory export.
        _bindings.Bind(() => FileOperations.RequestGdsFactoryExport, value => FileOperations.RequestGdsFactoryExport = value, () => GdsFactoryExport.Export());
        ExportMenu = new ExportMenuViewModel(new IExportFormat[]
        {
            new NazcaExportFormat(FileOperations.ExportNazcaCommand),
            new DieFilesExportFormat(FileOperations.ExportDieFilesCommand),
            GdsFactoryExportFormat,
            new SaxExportFormat(FileOperations.ExportSaxCommand),
            PhotonTorchExportFormat,
            VerilogAExportFormat,
            // Circuit-topology netlist (gdsfactory YAML): the only place it is exported from.
            new NetlistExportFormat(new AsyncRelayCommand(SaveNetlistAsync)),
            new NetlistClipboardExportFormat(new AsyncRelayCommand(CopyNetlistToClipboardAsync)),
        });

        // Wire up status callbacks
        _bindings.Bind(() => CanvasInteraction.UpdateStatus, value => CanvasInteraction.UpdateStatus = value, UpdateStatusText);
        _bindings.Bind(() => FileOperations.UpdateStatus, value => FileOperations.UpdateStatus = value, UpdateStatusText);
        _bindings.Bind(() => ViewportControl.UpdateStatus, value => ViewportControl.UpdateStatus = value, UpdateStatusText);
        _bindings.Bind(() => LeftPanel.UpdateStatus, value => LeftPanel.UpdateStatus = value, UpdateStatusText);
        _bindings.Bind(() => GdsImport.UpdateStatus, value => GdsImport.UpdateStatus = value, UpdateStatusText);
        // A .gds/.gdsii pick in the File→Open dialog routes into the GDS import
        // flow (FileOperations owns no import knowledge beyond the callback).
        FileOperations.OpenGdsImportRequested = GdsImport.OpenGdsImportDialogForFileAsync;
        // Key-preserving sink: lets a live UI language switch re-translate the startup
        // "Loaded N component types" status while it is still showing.
        _bindings.Bind(() => LeftPanel.UpdateLocalizedStatus, value => LeftPanel.UpdateLocalizedStatus = value, SetLocalizedStatus);

        // A saved component definition takes effect type-wide: push the new PDK S-matrices
        // into already-placed instances; explicit overrides keep winning.
        _bindings.Bind(() => LeftPanel.TemplateDefinitionSaved, value => LeftPanel.TemplateDefinitionSaved = value, FileOperations.RefreshInstancesFromTemplate);

        // Offset-editor fork-on-save: saving a bundled PDK writes the user's copy into
        // user-pdks; the library must swap to (shadow with) that fork, same as the
        // component editor's fork flow.
        PdkOffsetEditor.BundledPdkForkSaved = LeftPanel.RegisterSavedPdkFork;
        // Direct (non-fork) offset-editor saves target the retargeted fork file or a
        // registered custom PDK — refresh the library's in-memory templates so exports
        // and new placements pick up the saved values without a restart.
        PdkOffsetEditor.UserPdkSaved = LeftPanel.RefreshRegisteredPdkAfterExternalSave;

        var placementContext = _processContext.Placement;
        CanvasInteraction.PlacementContext = placementContext;
        InitializeDies();
        _canvas.Clipboard.PdkSourceResolver = placementContext.ResolveComponentPdkSource;

        if (aiGridService is Services.AiGridService aiGrid)
        {
            aiGrid.PlacementContext = placementContext;
        }

        // Let the export guard open the Settings window (e.g. on the Python-Environments
        // page when Nazca is missing); ShowSettingsWindowAsync is wired later by MainWindow.
        _bindings.Bind(() => FileOperations.ShowSettingsWindow, value => FileOperations.ShowSettingsWindow = value, async pageType =>
        {
            if (ShowSettingsWindowAsync != null)
                await ShowSettingsWindowAsync(pageType);
        });

        // Analysis-output picker (#754): the dock header button and both analysis tabs
        // can switch the canvas into the eyedropper picker mode.
        Action activateOutputPicker = () => CanvasInteraction.SetPickAnalysisOutputModeCommand.Execute(null);
        _bindings.Bind(() => BottomPanel.Analysis.Output.PickRequested, value => BottomPanel.Analysis.Output.PickRequested = value, activateOutputPicker);
        _bindings.Bind(() => BottomPanel.Analysis.Eye.RequestOutputPicker, value => BottomPanel.Analysis.Eye.RequestOutputPicker = value, activateOutputPicker);
        _bindings.Bind(() => BottomPanel.Analysis.Transient.RequestOutputPicker, value => BottomPanel.Analysis.Transient.RequestOutputPicker = value, activateOutputPicker);

        // Monte-Carlo fabrication variance: the sigma inputs pre-fill from the
        // tolerances declared by the active PDK process.
        _bindings.Bind(() => BottomPanel.Analysis.MonteCarlo.ActiveProcessProvider, value => BottomPanel.Analysis.MonteCarlo.ActiveProcessProvider = value,
            () => ScopeDie?.ProcessBinding ?? FileOperations.ActiveProcess);

        _selectionPanels = new Shell.SelectionPanelCoordinator(Canvas, CanvasInteraction,
            LeftPanel, RightPanel, BottomPanel, ImportedHierarchy);
        _libraryInteraction = new Shell.LibraryInteractionCoordinator(LeftPanel, CanvasInteraction,
            text => StatusText = text,
            (text, error) => BottomPanel.ErrorConsole.Log(text, CAP_Contracts.Logger.LogLevel.Error, error));
        // Mode-slice probe (issue #691): clicking an element in Probe mode opens the
        // non-modal flyout at the click point, auto-filled from PDK/connection data.
        ModeProbe = modeProbe;
        if (ModeProbe != null)
        {
            _bindings.Bind(() => ModeProbe.GetActiveProcessFingerprint, value => ModeProbe.GetActiveProcessFingerprint = value,
                () => (ScopeDie?.ProcessBinding ?? FileOperations.ActiveProcess)?.Fingerprint);
            _bindings.Bind(() => ModeProbe.GetSimulationWavelengthNm, value => ModeProbe.GetSimulationWavelengthNm = value,
                () => Canvas.Components.FirstOrDefault(c => c.IsLightSource)?.LaserConfig?.WavelengthNm);
            CanvasInteraction.ProbeRequested = (target, canvasX, canvasY) =>
            {
                // Canvas → control pixels, so the flyout opens where the user clicked.
                var zoom = ViewportControl.ZoomLevel;
                ModeProbe.Open(target, canvasX * zoom + Canvas.PanX, canvasY * zoom + Canvas.PanY);
            };
        }

        // Wire rename from hierarchy panel through undo-aware command manager
        _bindings.Bind(() => LeftPanel.HierarchyPanel.RenameComponent, value => LeftPanel.HierarchyPanel.RenameComponent = value, (component, newName) =>
        {
            var cmd = new Commands.RenameComponentCommand(component, newName);
            CommandManager.ExecuteCommand(cmd);
            LeftPanel.HierarchyPanel.RefreshNode(component);
        });

        _designChecks = new Shell.DesignCheckRunCoordinator(Canvas, LeftPanel, _processContext,
            RightPanel.DesignValidation, ChipSize, text => StatusText = text);
        _designChecks.PropertyChanged += DesignCheckStateChanged;
        _designCheckInteraction = new Shell.DesignCheckInteractionCoordinator(Canvas, CommandManager,
            RightPanel.DesignValidation, ViewportControl.NavigateCanvasTo, _designChecks.RunAsync);
        WireHierarchyPanel();
        WireShell();
        WireFileOperations();
        _shellState = new Shell.ShellStateCoordinator(FileOperations, Canvas, CommandManager,
            Registry, BottomPanel.Analysis.MonteCarlo, RefreshProcessIndicator, UpdateStatusText,
            () => { UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged(); });
        _shellState.PropertyChanged += ShellStateChanged;


        // Initialize panels
        LeftPanel.Initialize();
        RightPanel.Initialize();

        _activation = new Shell.ShellActivationCoordinator(Update, FileOperations.GdsExport,
            ClaudeConnectPrompt, PythonStartup, ErrorConsole);
    }

    private void UpdateStatusText(string text)
    {
        StatusText = text;
        BottomPanel.SetStatus(text);
    }

    /// <summary>Saves the netlist from the Export menu and reports the outcome in the status bar.</summary>
    private async Task SaveNetlistAsync()
    {
        await RightPanel.Netlist.SaveYamlCommand.ExecuteAsync(null);
        UpdateStatusText(RightPanel.Netlist.StatusText);
    }

    /// <summary>Copies the netlist from the Export menu and reports the outcome in the status bar.</summary>
    private async Task CopyNetlistToClipboardAsync()
    {
        await RightPanel.Netlist.CopyYamlCommand.ExecuteAsync(null);
        UpdateStatusText(RightPanel.Netlist.StatusText);
    }

    private void WireShell()
    {
        StatusBar = new Shell.StatusBarViewModel(Canvas, CanvasInteraction, LocalizationService.Instance);
        BlockedConnections = new Shell.BlockedConnectionsViewModel(Canvas, CanvasInteraction, ViewportControl.NavigateCanvasTo);
    }

    private void WireHierarchyPanel()
    {
        LeftPanel.HierarchyPanel.NavigateToPosition = ViewportControl.NavigateCanvasTo;
        LeftPanel.HierarchyPanel.GetViewportSize = ViewportControl.GetViewportSize;
        // OpenComponentSettings is wired by MainWindow.axaml.cs (view layer) so it can open the dialog window.
    }

    private void WireFileOperations()
    {
        _bindings.Bind(() => FileOperations.PhotonTorchExport.UpdateStatus, value => FileOperations.PhotonTorchExport.UpdateStatus = value, UpdateStatusText);
        FileOperations.RebuildHierarchy = LeftPanel.HierarchyPanel.RebuildTree;

        // Single-process wiring (issue #570): supply the live PDK catalog for the
        // New-Design picker and legacy-file migration, route migration warnings to
        // the status bar, and keep the toolbar indicator in sync with the active process.
        FileOperations.ProcessCatalogProvider = _processContext.BuildCatalog;
        _bindings.Bind(() => FileOperations.ProcessAgnosticPdkNamesProvider, value => FileOperations.ProcessAgnosticPdkNamesProvider = value,
            () => LeftPanel.GetProcessAgnosticPdkNames());
        // Migration/revalidation warnings must survive the next status update — the
        // status bar is overwritten by the load-complete message an instant later.
        _bindings.Bind(() => FileOperations.OnProcessMigrationWarning, value => FileOperations.OnProcessMigrationWarning = value, warning =>
        {
            UpdateStatusText(warning);
            ErrorConsole.LogWarning(warning);
        });
        // Re-read the VM-side one-time translations when the UI language switches (field bug
        // round 5). Filtered to ActiveLanguageCode because SetLanguage raises several
        // notifications ("Item"/"Item[]" for the AXAML indexer bindings) per switch.
        _localizationSubscription = LocalizationWeakSubscription.Subscribe(this, static (self, e) =>
        {
            if (e.PropertyName == nameof(LocalizationService.ActiveLanguageCode))
                self.OnUiLanguageChanged();
        });

        // One zoom-to-fit body for both post-content triggers — design load and
        // GDS import placement: prefer the live viewport size over the caller's
        // fallback, then fit the WHOLE canvas content (not just the new part).
        Action<double, double> zoomToFitToViewport = (w, h) =>
        {
            var (vpWidth, vpHeight) = ViewportControl.GetViewportSize?.Invoke() ?? (w, h);
            ViewportControl.ZoomToFit(vpWidth, vpHeight);
        };
        _bindings.Bind(() => FileOperations.ZoomToFitAfterLoad, value => FileOperations.ZoomToFitAfterLoad = value, zoomToFitToViewport);
        _bindings.Bind(() => GdsImport.ZoomToFitAfterImport, value => GdsImport.ZoomToFitAfterImport = value, zoomToFitToViewport);
        // An import that outgrew the chip already resized the canvas; this syncs
        // the chip-size settings panel (same pattern as ApplyChipSizeAfterLoad).
        GdsImport.ApplyChipSizeAfterImport = ChipSize.ApplyFromMicrometers;

        // Restore chip size from saved file without overwriting the user preference default
        FileOperations.ApplyChipSizeAfterLoad = ChipSize.ApplyFromMicrometers;

        // Re-sync the Spectrum tab's coherent-interference toggle with the flag
        // just restored from the loaded file (the canvas survives loads, so the
        // panel's one-time Configure never sees the new value).
        FileOperations.CoherentModeRestoredAfterLoad = BottomPanel.Analysis.Spectrum.SyncCoherentToggleFromCanvas;

    }

    /// <summary>
    /// Updates <see cref="ActiveProcessLabel"/> and <see cref="IsPlayground"/> from
    /// <see cref="FileOperationsViewModel.ActiveProcess"/> (issue #570). Invoked whenever
    /// the active process changes (New-Design selection, load, or migration).
    /// </summary>
    private void RefreshProcessIndicator()
    {
        var p = FileOperations.ActiveProcess;
        UpdateActiveProcessLabel(p);
        LeftPanel.ApplyActiveProcess(p);
        if (Tutorial?.IsActive == true && !_learning.Guidance.HasLearningProcess)
            Tutorial.Skip();
    }

    /// <summary>
    /// Re-reads the VM-side one-time translations after a live language switch: the
    /// active-process badge (recomputed, PDK process lock untouched), the status bar's
    /// mode-relevant shortcut hints (#1162) and — only while the status bar shows the
    /// idle "Ready" text — the status bar itself.
    /// </summary>
    private void OnUiLanguageChanged()
    {
        UpdateActiveProcessLabel(FileOperations.ActiveProcess);
        CanvasInteraction.RefreshModeShortcutHints();
        if (_lastLocalizedStatus is { } status && StatusText == status.Formatted)
            SetLocalizedStatus(status.Key, status.Args);
    }

    /// <summary>
    /// Sets the status bar from a string-table key (formatted invariantly with
    /// <paramref name="args"/>) and remembers the key, so a live UI language switch can
    /// re-translate the message while it is still showing.
    /// </summary>
    internal void SetLocalizedStatus(string key, params object[] args)
    {
        var formatted = string.Format(
            CultureInfo.InvariantCulture, LocalizationService.Instance.Translate(key), args);
        _lastLocalizedStatus = (key, args, formatted);
        StatusText = formatted;
    }

    /// <summary>
    /// Recomputes the localized <see cref="ActiveProcessLabel"/> and <see cref="IsPlayground"/>
    /// flag (shown once, in the status bar) — without re-applying the PDK process
    /// lock. Called on process change and on UI language switch so the badge re-reads live.
    /// </summary>
    private void UpdateActiveProcessLabel(CAP_Core.Components.Process.ActiveProcessSelection? p)
    {
        var loc = LocalizationService.Instance;
        IsPlayground = p?.IsPlayground == true;
        ActiveProcessTip = loc.Translate("StatusBar.ActiveProcessTip");
        if (TryDescribeDieProcesses())
            return;
        ActiveProcessLabel = p == null ? loc.Translate("Process.NoneSelected")
            : p.IsPlayground ? loc.Translate("Process.PlaygroundBadge")
            : string.Format(CultureInfo.InvariantCulture, loc.Translate("Process.Prefix"), p.DisplayName);
    }

    // Canvas interaction delegates
    public void CanvasClicked(double x, double y) => CanvasInteraction.CanvasClicked(x, y);
    public void PinClicked(PhysicalPin pin) => CanvasInteraction.PinClicked(pin);
    public void CanvasMouseMove(double x, double y) => CanvasInteraction.CanvasMouseMove(x, y);
    public void StartMoveComponent(ComponentViewModel component) => CanvasInteraction.StartMoveComponent(component);
    public void EndMoveComponent() => CanvasInteraction.EndMoveComponent();
    public void StartGroupMove(IEnumerable<ComponentViewModel> components) => CanvasInteraction.StartGroupMove(components);
    public void EndGroupMove(IEnumerable<ComponentViewModel> components) => CanvasInteraction.EndGroupMove(components);
    public void PasteSelected(double? targetX = null, double? targetY = null) => CanvasInteraction.PasteSelected(targetX, targetY);

    // Viewport control delegates
    public void ZoomToFit(double viewportWidth, double viewportHeight) => ViewportControl.ZoomToFit(viewportWidth, viewportHeight);

    // Backward-compatible command delegates
    [RelayCommand]
    private void SetSelectMode() => CanvasInteraction.SetSelectModeCommand.Execute(null);

    [RelayCommand]
    private void SetConnectMode() => CanvasInteraction.SetConnectModeCommand.Execute(null);

    [RelayCommand]
    private void SetDeleteMode() => CanvasInteraction.SetDeleteModeCommand.Execute(null);

    [RelayCommand]
    private void SetProbeMode() => CanvasInteraction.SetProbeModeCommand.Execute(null);

    [RelayCommand]
    private void SetCutMode() => CanvasInteraction.SetCutModeCommand.Execute(null);

    [RelayCommand]
    private void DeleteSelected() => CanvasInteraction.DeleteSelectedCommand.Execute(null);

    [RelayCommand]
    private void CopySelected() => CanvasInteraction.CopySelectedCommand.Execute(null);

    [RelayCommand]
    private void PasteSelectedCommand() => CanvasInteraction.PasteSelectedCommandCommand.Execute(null);

    [RelayCommand]
    private void RotateSelected() => CanvasInteraction.RotateSelectedCommand.Execute(null);

    [RelayCommand]
    private void CreateGroup() => CanvasInteraction.CreateGroupCommand.Execute(null);

    [RelayCommand]
    private void Ungroup() => CanvasInteraction.UngroupCommand.Execute(null);

    [RelayCommand]
    private void ZoomIn() => ViewportControl.ZoomInCommand.Execute(null);

    [RelayCommand]
    private void ZoomOut() => ViewportControl.ZoomOutCommand.Execute(null);

    [RelayCommand]
    private void ResetZoom() => ViewportControl.ResetZoomCommand.Execute(null);

    [RelayCommand]
    private void ResetPan() => ViewportControl.ResetPanCommand.Execute(null);

    [RelayCommand]
    private async Task SaveDesign() => await FileOperations.SaveDesignCommand.ExecuteAsync(null);

    /// <summary>Shows the Home screen (recent projects, new/open) over the editor.</summary>
    [RelayCommand]
    private void ShowHome() => Home.Show();

    [RelayCommand]
    private async Task SaveDesignAs() => await FileOperations.SaveDesignAsCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task LoadDesign() => await FileOperations.LoadDesignCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task NewProject() => _projectProcess.NewProjectAsync();

    /// <summary>Prompts for a baseline process only if no design or learning flow has established one.</summary>
    public Task PromptForInitialProcessAsync() => _projectProcess.PromptInitiallyAsync();
    [RelayCommand]
    private async Task ExportNazca() => await FileOperations.ExportNazcaCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task ExportSax() => await FileOperations.ExportSaxCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task LoadPdk() => await LeftPanel.LoadPdkCommand.ExecuteAsync(null);

    /// <summary>
    /// Raised when the user requests to open the PDK Offset Editor window.
    /// The View layer subscribes and shows the window.
    /// </summary>
    public Action? ShowPdkOffsetEditorRequested { get; set; }

    [RelayCommand]
    private void OpenPdkOffsetEditor()
    {
        ShowPdkOffsetEditorRequested?.Invoke();
    }

    /// <summary>
    /// Shows the New-Design process-selection dialog (issue #570) and returns the
    /// user's choice, or null if the user cancelled. Wired by
    /// <see cref="CAP.Avalonia.Views.MainWindow"/>; left null in headless/test
    /// contexts, in which case <see cref="NewProject"/> skips the dialog and
    /// proceeds without locking a process.
    /// </summary>
    public Func<IReadOnlyList<ProcessGroup>, Task<ActiveProcessSelection?>>? ShowProcessSelectionAsync
    {
        get => _projectProcess.ShowPicker;
        set => _projectProcess.ShowPicker = value;
    }

    [RelayCommand]
    private void OpenPdkHelp()
    {
        var url = "https://github.com/aignermax/Lunima/blob/main/docs/PDK_JSON_FORMAT.md";

        try
        {
            _urlLauncher.Open(url);
            StatusText = LocalizationService.Instance.Translate("Status.OpeningPdkHelp");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LocalizationService.Instance.Translate("Status.CouldNotOpenBrowser"), ex.Message);
        }
    }

    /// <summary>
    /// Opens the application Settings window. The concrete window creation
    /// is wired by <see cref="CAP.Avalonia.Views.MainWindow"/>.
    /// </summary>
    [RelayCommand]
    private async Task OpenSettingsWindow()
    {
        if (ShowSettingsWindowAsync != null)
            await ShowSettingsWindowAsync(null);
    }

    /// <summary>
    /// Opens the Settings window focused on the AI Assistant page — used by
    /// the right-panel AI shortcut when no API key is configured yet.
    /// </summary>
    [RelayCommand]
    private async Task OpenAiSettings()
    {
        if (ShowSettingsWindowAsync != null)
            await ShowSettingsWindowAsync(typeof(ViewModels.Settings.AiAssistantSettingsPage));
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task Undo()
    {
        if (await Canvas.Routing.ExecuteHistoryChangeAsync(CommandManager.Undo, () => CommandManager.CanUndo))
        {
            StatusText = string.Format(LocalizationService.Instance.Translate("Status.Undone"), CommandManager.RedoDescription ?? "action");
        }
        else
        {
            StatusText = LocalizationService.Instance.Translate("Status.NothingToUndo");
        }
    }

    private bool CanUndo() => CommandManager.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task Redo()
    {
        if (await Canvas.Routing.ExecuteHistoryChangeAsync(CommandManager.Redo, () => CommandManager.CanRedo))
        {
            StatusText = string.Format(LocalizationService.Instance.Translate("Status.Redone"), CommandManager.UndoDescription ?? "action");
        }
        else
        {
            StatusText = LocalizationService.Instance.Translate("Status.NothingToRedo");
        }
    }

    private bool CanRedo() => CommandManager.CanRedo;

    [RelayCommand]
    private Task RunSimulation() => _work.RunAsync(RunSimulationCoreAsync);

    private async Task RunSimulationCoreAsync(CancellationToken cancellationToken)
    {
        if (IsSimulating) return;

        if (SimulationMode == CAP.Avalonia.ViewModels.Analysis.SimulationMode.Transient)
        {
            // Clear any stale CW power-flow overlay so it doesn't render on top of
            // the transient results (matches the CW toggle-off below).
            if (Canvas.ShowPowerFlow)
            {
                Canvas.ShowPowerFlow = false;
                Canvas.PowerFlowVisualizer.IsEnabled = false;
            }

            BottomPanel.Analysis.OpenTransient();
            await BottomPanel.Analysis.Transient.RunTransientCommand.ExecuteAsync(null);
            return;
        }

        // Always runs; the on/off toggle lives in RunWorkspace / TogglePowerOverlay (L, P).
        await ExecuteSimulation(cancellationToken);
    }

    /// <summary>
    /// Runs the CW simulation without toggle logic. <paramref name="isLiveRun"/> marks a
    /// re-run of the switched-on simulation after a design change: a cancelled live run
    /// applies nothing, a live run that cannot simulate (no light source, no connections,
    /// blocked circuit) switches the simulation off instead of leaving a stale overlay,
    /// and a live run that finds a simulation already running is re-queued.
    /// </summary>
    private Task ExecuteSimulation(CancellationToken cancellationToken = default, bool isLiveRun = false) =>
        _work.RunAsync(async stop =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, cancellationToken);
            await ExecuteSimulationCoreAsync(linked.Token, isLiveRun, cancellationToken);
        });

    private async Task ExecuteSimulationCoreAsync(CancellationToken cancellationToken, bool isLiveRun,
        CancellationToken caller)
    {
        if (IsSimulating)
        {
            _liveRunDeferred |= isLiveRun;
            return;
        }
        IsSimulating = true;

        try
        {
            StatusText = LocalizationService.Instance.Translate("Status.RunningSimulation");
            var result = await Simulation.RunAsync(Canvas, cancellationToken);
            _work.ThrowIfStopping();
            caller.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            if (result.IsSuperseded) return;

            if (result.Success)
            {
                StatusText = string.Format(LocalizationService.Instance.Translate("Status.SimulationComplete"),
                             result.LightSourceCount, result.ConnectionCount, result.WavelengthSummary);
            }
            else
            {
                ReportSimulationFailure(result.ErrorMessage ?? "Simulation failed", isLiveRun);
            }
        }
        catch (OperationCanceledException) when (_work.IsStopping || caller.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            // Superseded by a newer live run, which reports its own outcome.
        }
        catch (CAP_Core.LightCalculation.NonConvergentCircuitException ex)
        {
            // Physics guard (round-4): non-passive data / resonant loop — surface the
            // already-localized guard message instead of a raw exception string.
            var message = Analysis.NonConvergentCircuitMessageFormatter.Format(ex);
            ReportSimulationFailure(message, isLiveRun);
            BottomPanel.ErrorConsole.Log($"Simulation blocked: {message}", CAP_Contracts.Logger.LogLevel.Error, ex);
        }
        catch (Exception ex)
        {
            ReportSimulationFailure(
                string.Format(LocalizationService.Instance.Translate("Status.SimulationError"), ex.Message), isLiveRun);
            BottomPanel.ErrorConsole.Log($"Simulation failed: {ex.Message}", CAP_Contracts.Logger.LogLevel.Error, ex);

        }
        finally
        {
            IsSimulating = false;
            RequeueDeferredLiveRun();
        }
    }

    [RelayCommand]
    private Task RunDesignChecks() => _designChecks.RunAsync();

    public bool IsRunningDesignChecks { get => _designChecks.IsRunning; set => _designChecks.IsRunning = value; }
}

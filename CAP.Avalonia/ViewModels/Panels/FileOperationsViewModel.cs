using CAP_DataAccess.Persistence.NativeDesign;
using CAP.Avalonia.ViewModels.Panels.DesignModels;
using CAP.Avalonia.ViewModels.Panels.NativeDesign;
using System.Collections.ObjectModel;
using CAP_Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using CAP_Core.Routing;
using CAP_DataAccess.Persistence;
using CAP_DataAccess.Persistence.PIR;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.ComponentSettings.InstanceOverride;
using CAP.Avalonia.ViewModels.Converters;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Export;
using CAP.Avalonia.ViewModels.Export.DesignFiles;
using CAP_Core.Export;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for file operations (save, load, export).
/// Coordinates dialogs and presentation while native mappers and export workflows own persistence work.
/// </summary>
public partial class FileOperationsViewModel : ObservableObject, IDisposable
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private readonly ErrorConsoleService? _errorConsole;
    private readonly ComponentTemplateResolver _templates;
    private readonly NativeComponentMapper _componentMapper;
    private readonly NativeGroupLoader _groupLoader;
    private readonly NativeDesignCapture _designCapture;
    private readonly NativeLoadDiagnostics _loadDiagnostics;
    private readonly NativeConnectionLoader _connectionLoader;
    private readonly NativeDesignPostLoad _postLoad;
    private readonly NativeProcessBindings _processBindings;
    private readonly DesignModelOverrides _models;
    private readonly TemplateInstanceRefresher _templateRefresher;
    private readonly NazcaExportWorkflow _nazcaWorkflow;
    private readonly SaxExportWorkflow _saxWorkflow;
    private readonly DieExportWorkflow _dieWorkflow;
    private readonly RecentProjectsService? _recentProjects;
    private readonly NativeDesignIoCoordinator _nativeIo;

    /// <summary>
    /// Current .lun format version. Older files load with a warning and upgrade on save.
    /// </summary>
    private const string CurrentFormatVersion = "2.0";

    /// <summary>
    /// Absolute path of the currently open .lun file, or null for an unsaved
    /// new project. Observable so the Home screen and window title can react.
    /// </summary>
    [ObservableProperty]
    private string? _currentFilePath;

    /// <summary>Prompt shown before discarding unsaved changes to load another design.</summary>
    private const string LoadPromptMessage = "Do you want to save your changes before loading another design?";

    /// <summary>
    /// Persists metadata loaded from the last opened file so that Created date
    /// and other user-set fields survive a save-over-reload cycle.
    /// </summary>
    private DesignMetadata? _loadedMetadata;

    /// <summary>
    /// Background routing pass the last load started for connections that arrived without
    /// geometry; already completed when every connection came with a usable route.
    /// </summary>
    public Task PostLoadRouting { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Per-component S-matrix overrides loaded from the PIR section of the .lun file,
    /// or added via the S-parameter import feature. Survives save-over-reload cycles.
    /// Keyed by component identifier string; values are the stored S-matrices.
    /// </summary>
    public Dictionary<string, ComponentSMatrixData> StoredSMatrices => _models.Stored;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    /// <summary>
    /// The fabrication process this design is currently locked to (issue #570).
    /// Null means no components have been placed yet. Set via <see cref="SetActiveProcess"/>
    /// (from the New-Design dialog or the process picker), restored on load, or migrated
    /// from a legacy file's placed-component PDK sources.
    /// </summary>
    [ObservableProperty]
    private ActiveProcessSelection? _activeProcess;

    /// <summary>
    /// Supplies the current set of installable/loaded process groups. Wired by DI/MainViewModel
    /// to the live PDK catalog; used to migrate legacy files that predate single-process support.
    /// </summary>
    public Func<IReadOnlyList<ProcessGroup>>? ProcessCatalogProvider { get; set; }

    /// <summary>
    /// Supplies the process-derived metal routing parameters (trace width, GDS layer,
    /// crossing policy) for electrical connections at export time (issue #682). Wired by
    /// MainViewModel to the active process' PDK definitions; null falls back to
    /// <see cref="CAP_Core.Routing.MetalRouting.MetalRoutingSpec.Default"/>.
    /// </summary>
    public Func<CAP_Core.Routing.MetalRouting.MetalRoutingSpec>? MetalRoutingSpecProvider { get; set; }

    /// <summary>
    /// Supplies the names of loaded PDKs flagged process-agnostic (e.g. "Analysis Tools").
    /// Wired by DI/MainViewModel; passed to <see cref="ActiveProcessResolver.Migrate"/> so
    /// analyzer-only tool PDKs never count toward a legacy design's process attribution
    /// (issue #570 final review).
    /// </summary>
    public Func<IReadOnlyCollection<string>>? ProcessAgnosticPdkNamesProvider { get; set; }

    /// <summary>
    /// Callback invoked when loading a legacy file requires falling back to Playground
    /// because its components could not be attributed to a single installed process.
    /// </summary>
    public Action<string>? OnProcessMigrationWarning { get; set; }

    /// <summary>
    /// The open design's GDS-imported component sets (issue #830). Wired by
    /// MainViewModel; null in headless contexts. Captured into the .lun on
    /// save, restored (with legacy global import-PDK migration) on load, and
    /// cleared on New Project so imported components never leak across designs.
    /// </summary>
    public Services.GdsImport.DesignScope.DesignScopedGdsComponentService? DesignScopedGdsComponents { get; set; }

    /// <summary>
    /// Per-layer visibility of imported GDS geometry (issue #858). Wired by
    /// MainViewModel; null in headless contexts. Captured into the .lun on
    /// save, restored on load, and reset on New Project.
    /// </summary>
    public GdsImport.LayerVisibility.GdsLayerVisibilityViewModel? LayerVisibility { get; set; }

    /// <summary>
    /// ViewModel for GDS export functionality.
    /// </summary>
    public GdsExportViewModel GdsExport { get; }

    /// <summary>
    /// ViewModel for PhotonTorch export functionality.
    /// </summary>
    public PhotonTorchExportViewModel PhotonTorchExport { get; }

    /// <summary>
    /// ViewModel for Verilog-A / SPICE export functionality. Shared singleton
    /// so this property and the export-options dialog (VerilogAExportDialog,
    /// wired via <c>Views.Dialogs.ExportDialogWiring</c>) see the same state.
    /// </summary>
    public VerilogAExportViewModel VerilogAExport { get; }

    /// <summary>
    /// Callback to update status text in the UI.
    /// </summary>
    public Action<string>? UpdateStatus { get; set; }

    /// <summary>
    /// Callback invoked after a project is successfully opened or created
    /// (load from file or File → New). The Home screen uses this to dismiss itself.
    /// </summary>
    public Action? ProjectOpened { get; set; }

    /// <summary>
    /// Callback to rebuild hierarchy tree after loading.
    /// </summary>
    public Action? RebuildHierarchy { get; set; }

    /// <summary>
    /// Callback to trigger zoom-to-fit after loading.
    /// </summary>
    public Action<double, double>? ZoomToFitAfterLoad { get; set; }

    /// <summary>
    /// Callback to apply a chip size (in micrometers) after loading a project.
    /// Parameters: (widthMicrometers, heightMicrometers).
    /// </summary>
    public Action<double, double>? ApplyChipSizeAfterLoad { get; set; }

    /// <summary>
    /// Callback fired after a load restored the coherent interference mode, so
    /// views holding a toggle bound to that mode can re-sync (the canvas and
    /// its connection manager survive loads, so their Configure is not re-run).
    /// </summary>
    public Action? CoherentModeRestoredAfterLoad { get; set; }

    /// <summary>
    /// Supplies the active workspace for the .lun file on save (null = not stored).
    /// </summary>
    public Func<string?>? CaptureWorkspaceForSave { get; set; }

    /// <summary>
    /// Called after a load with the file's stored workspace (null when absent) and after
    /// File → New with null, once the canvas holds the new design, so the workspace switch
    /// can restore it or choose a content-based default.
    /// </summary>
    public Action<string?>? RestoreWorkspace { get; set; }

    /// <summary>
    /// File dialog service for showing open/save dialogs.
    /// </summary>
    public IFileDialogService? FileDialogService { get; set; }

    /// <summary>
    /// Message box service for showing confirmation dialogs.
    /// </summary>
    public IMessageBoxService? MessageBoxService { get; set; }

    /// <summary>
    /// Opens the Settings window, optionally pre-selecting a page by type.
    /// Wired by <see cref="MainViewModel"/>; null in headless contexts.
    /// </summary>
    public Func<Type?, Task>? ShowSettingsWindow { get; set; }

    /// <summary>
    /// Launches the gdsfactory export flow. Wired by <see cref="MainViewModel"/>; invoked when
    /// the user, prompted about gdsfactory-native components in a Nazca export, chooses to use
    /// the gdsfactory export instead. Null in headless contexts.
    /// </summary>
    public Func<Task>? RequestGdsFactoryExport { get; set; }

    /// <summary>
    /// Routes a .gds/.gdsii pick from the open-design dialog into the GDS import
    /// flow (the import dialog opens for that file, already analyzed on open).
    /// Wired by <see cref="MainViewModel"/> to
    /// <c>GdsImportButtonViewModel.OpenGdsImportDialogForFileAsync</c>; null in
    /// headless contexts, where the pick surfaces a status hint instead.
    /// </summary>
    public Func<string, Task>? OpenGdsImportRequested { get; set; }

    /// <summary>Initializes a new instance of <see cref="FileOperationsViewModel"/>.</summary>
    public FileOperationsViewModel(
        IUrlLauncher urlLauncher,
        INativeDesignFileStore designFiles,
        DesignCanvasViewModel canvas,
        CommandManager commandManager,
        SimpleNazcaExporter nazcaExporter,
        SaxExporter saxExporter,
        ObservableCollection<ComponentTemplate> componentLibrary,
        GdsExportViewModel gdsExport,
        PhotonTorchExportViewModel photonTorchExport,
        VerilogAExportViewModel verilogAExport,
        ErrorConsoleService? errorConsole = null,
        ITemplateSMatrixStore? userSMatrixOverrideStore = null,
        RecentProjectsService? recentProjects = null)
    {
        _nativeIo = new NativeDesignIoCoordinator(canvas.Document, designFiles);
        _canvas = canvas;
        _commandManager = commandManager;
        GdsExport = gdsExport;
        PhotonTorchExport = photonTorchExport;
        VerilogAExport = verilogAExport;
        _errorConsole = errorConsole;
        var launcher = urlLauncher;
        _nazcaWorkflow = new NazcaExportWorkflow(nazcaExporter, canvas, componentLibrary, gdsExport, errorConsole, launcher);
        _saxWorkflow = new SaxExportWorkflow(canvas, saxExporter, errorConsole);
        _dieWorkflow = new DieExportWorkflow(canvas, nazcaExporter, componentLibrary, gdsExport, errorConsole, _nazcaWorkflow);
        _recentProjects = recentProjects;

        // Track changes to mark project as unsaved
        _canvas.Components.CollectionChanged += OnDesignCollectionChanged;
        _canvas.Connections.CollectionChanged += OnDesignCollectionChanged;
        // The analysis-output designation (#754) is part of the design file too.
        _canvas.AnalysisOutput.PropertyChanged += OnAnalysisOutputChanged;

        _templates = new ComponentTemplateResolver(
            () => _canvas.Groups.RootComponents.Concat(_canvas.Components), componentLibrary);
        _componentMapper = new NativeComponentMapper(_canvas, _templates);
        _groupLoader = new NativeGroupLoader(_canvas, _templates);
        _loadDiagnostics = new NativeLoadDiagnostics(errorConsole);
        _connectionLoader = new NativeConnectionLoader(_canvas, _loadDiagnostics, errorConsole);
        _postLoad = new NativeDesignPostLoad(_canvas, errorConsole);
        _processBindings = new NativeProcessBindings(_templates,
            () => ProcessCatalogProvider?.Invoke() ?? Array.Empty<ProcessGroup>(),
            () => ProcessAgnosticPdkNamesProvider?.Invoke() ?? Array.Empty<string>(),
            warning => OnProcessMigrationWarning?.Invoke(warning));
        _designCapture = new NativeDesignCapture(_canvas, _componentMapper, new NativeGroupCapture(_canvas, _templates));
        _models = new DesignModelOverrides(_canvas.Components, _templates, userSMatrixOverrideStore, errorConsole,
            () => _canvas.Groups.RootComponents.Select(view => view.Component));
        _templateRefresher = new TemplateInstanceRefresher(_templates, _models, errorConsole);
    }

    /// <summary>
    /// Re-applies all user-global PDK template overrides to every live canvas
    /// component. Called by the Component Settings dialog after a successful
    /// import or delete in Per-Template mode so the change propagates to
    /// existing instances without requiring a project reload.
    /// </summary>
    public void ReapplyTemplateOverrides()
    {
        _models.ApplyGlobal(_canvas.Components.Select(vm => vm.Component));
    }

    /// <summary>
    /// Sets the active process this design is locked to (issue #570), e.g. from the
    /// New-Design dialog or a process-picker action.
    /// </summary>
    /// <param name="selection">The process to lock the design to (or Playground).</param>
    /// <param name="markDirty">
    /// False for the startup / New-Design picker: choosing the baseline process of a
    /// pristine empty design is not an unsaved change, and marking it dirty made every
    /// fresh launch answer a spurious "Save changes?" prompt.
    /// </param>
    public void SetActiveProcess(ActiveProcessSelection? selection, bool markDirty = true)
    {
        ActiveProcess = selection;
        if (markDirty)
            HasUnsavedChanges = true;
    }

    [RelayCommand]
    private Task SaveDesign() => SaveWithDialogAsync(forcePicker: false);

    [RelayCommand]
    private Task SaveDesignAs() => SaveWithDialogAsync(forcePicker: true);

    /// <returns>True when the design was written to disk.</returns>
    private async Task<bool> SaveWithDialogAsync(bool forcePicker, CAP_Contracts.ICommitPermission? permission = null)
    {
        await using var request = NativeDesignRequest.Begin(_canvas.Document, permission);
        if (request == null) return false;
        if (FileDialogService == null)
        {
            request.TryCommit(() => UpdateStatus?.Invoke("Save not available"));
            return false;
        }
        var filePath = forcePicker ? null : CurrentFilePath;
        if (filePath == null)
        {
            Task<string?>? picking = null;
            if (!request.TryCommit(() => picking = FileDialogService.ShowSaveFileDialogAsync(
                    forcePicker ? "Save Design As" : "Save Design", "lun", "Lunima Files|*.lun|All Files|*.*"))) return false;
            filePath = await picking!;
        }
        return filePath != null && request.IsCurrent && await SaveToFile(filePath, request);
    }

    /// <summary>
    /// Saves the open design to <paramref name="filePath"/> (no dialog) and makes it the current
    /// file — the scripted counterpart of Save As, used by the agent bridge.
    /// </summary>
    /// <returns>True when the file was written.</returns>
    public Task<bool> SaveDesignToPathAsync(string filePath, CAP_Contracts.ICommitPermission? permission = null) => SaveToFile(filePath, permission);

    private Task<bool> SaveToFile(string filePath, CAP_Contracts.ICommitPermission? permission = null) => _nativeIo.SaveAsync(filePath,
        CaptureDesignForSave, (saved, unchanged) => PublishSavedDesign(filePath, saved.UnresolvedPins, unchanged), error =>
        {
            _errorConsole?.LogError($"Failed to save design: {error.Message}", error);
            UpdateStatus?.Invoke($"Save failed: {error.Message}");
        }, permission);

    private NativeDesignSaveData CaptureDesignForSave()
    {
        var (designData, unresolvedConnectionPins) = _designCapture.Capture(_processBindings.CaptureGroup);
        var componentsList = _canvas.Groups.RootComponents.ToList();

        designData.FormatVersion = CurrentFormatVersion;
        // GDS-imported components travel inside the .lun (issue #830) so the
        // design stays self-contained; null when the design imported nothing.
        // Only sets still referenced by a placed component are embedded — an
        // import whose components were all deleted drops out of the file here.
        var referencedPdkSources = designData.Components.Select(c => c.PdkSource)
            .Concat(designData.Groups?.SelectMany(g => g.ChildComponents.Select(ch => ch.PdkSource))
                    ?? Enumerable.Empty<string?>());
        designData.ImportedGdsComponents = DesignScopedGdsComponents?.CaptureForSave(referencedPdkSources);
        designData.LayerVisibility = LayerVisibility?.CaptureForSave();
        designData.Metadata = NativeDesignCodec.BuildMetadataForSave(_loadedMetadata);
        if (StoredSMatrices.Count > 0)
        {
            var swept = _models.Capture();
            if (swept.Count > 0)
                designData.SMatrices = swept;
        }
        designData.ChipWidthMicrometers  = _canvas.Dies.RootChipWidthMicrometers;
        designData.ChipHeightMicrometers = _canvas.Dies.RootChipHeightMicrometers;
        designData.ActiveProcess = ActiveProcessResolver.ToData(ActiveProcess);
        // Persist the designated analysis-output coupler (#754) by its Identifier —
        // the runtime Component.Id is regenerated on every load.
        designData.AnalysisOutputCoupler = _canvas.AnalysisOutput.CouplerId is Guid outputId
            ? componentsList.FirstOrDefault(c => c.Component.Id == outputId)?.Component.Identifier
            : null;
        // Coherent interference mode (#1333): only stored when on — off is the
        // default, so old files without the field round-trip identically.
        designData.CoherentPropagationPhase = _canvas.ConnectionManager.EnableCoherentPropagationPhase
            ? true
            : null;
        designData.Workspace = CaptureWorkspaceForSave?.Invoke();
        return new NativeDesignSaveData(designData, unresolvedConnectionPins);
    }

    private void PublishSavedDesign(string filePath, IReadOnlyList<string> unresolvedConnectionPins,
        bool documentUnchangedSinceCapture)
    {
        CurrentFilePath = filePath;
        if (documentUnchangedSinceCapture) HasUnsavedChanges = false;
        _recentProjects?.RecordProject(filePath);
        if (unresolvedConnectionPins.Count > 0)
        {
            // Never drop a wire silently: an endpoint that resolved to neither a
            // canvas component nor any group member would vanish on reload.
            var warning = $"{unresolvedConnectionPins.Count} connection endpoint(s) could not be resolved " +
                          $"({string.Join(", ", unresolvedConnectionPins)}) and were not saved.";
            _errorConsole?.LogWarning(warning);
            UpdateStatus?.Invoke($"Saved to {Path.GetFileName(filePath)} — {warning}");
        }
        else
        {
            UpdateStatus?.Invoke($"Saved to {Path.GetFileName(filePath)}");
        }
    }

    /// <summary>Resolves the PDK-qualified template key used by model import consumers.</summary>
    public string? ResolveTemplateKey(Component component) => _templates.Key(component);

    /// <summary>Propagates a saved template definition while preserving instance model overrides.</summary>
    public void RefreshInstancesFromTemplate(ComponentTemplate template)
    {
        if (_templateRefresher.Refresh(_canvas.Components.Select(vm => vm.Component), template))
            _canvas.InvalidateSimulation();
    }

    /// <summary>
    /// Resolves which canvas component and pin name to use when serializing a connection endpoint.
    /// Handles both regular components (direct match) and group external pins (via InternalPin lookup).
    /// </summary>
    /// <param name="components">All top-level components on the canvas.</param>
    /// <param name="pin">The physical pin on the connection endpoint.</param>
    /// <returns>The component index and pin name to store in ConnectionData.</returns>
    internal static (int index, string pinName) ResolveConnectionEndpoint(
        List<ComponentViewModel> components, PhysicalPin pin)
    {
        var (index, pinName, _, _) = ResolveConnectionEndpointWithOwner(components, pin);
        return (index, pinName);
    }

    internal static (int index, string pinName, string? componentId, string? ancestorGroupId)
        ResolveConnectionEndpointWithOwner(List<ComponentViewModel> components, PhysicalPin pin) =>
        NativeEndpointMapper.ResolveConnectionEndpointWithOwner(components.Select(vm => vm.Component).ToList(), pin);

    internal static PhysicalPin? ResolvePin(Component component, string pinName) =>
        NativeEndpointMapper.ResolvePin(component, pinName);

    [RelayCommand]
    private async Task LoadDesign()
    {
        await using var request = NativeDesignRequest.Begin(_canvas.Document);
        if (request == null) return;
        if (FileDialogService == null)
        {
            UpdateStatus?.Invoke("Load not available");
            return;
        }

        if (!await ConfirmUnsavedChangesAsync(LoadPromptMessage, request)) return;
        Task<string?>? picking = null;
        if (!request.TryCommit(() => picking = FileDialogService.ShowOpenFileDialogAsync(
                "Load Design", "Lunima Files|*.lun|GDS files (*.gds;*.gdsii)|*.gds;*.gdsii|All Files|*.*"))) return;
        var filePath = await picking!;
        if (filePath == null || !request.IsCurrent)
            return;

        // A GDS pick routes into the GDS import flow instead of the .lun load
        // path: the import dialog opens for that file and analyzes it on open.
        // Like a .lun load, this REPLACES the current design — the clear
        // happens inside OpenGdsImportAsync, covered by the (already answered)
        // unsaved-changes prompt: prompt → pick → clear → import.
        if (IsGdsFile(filePath))
        {
            await OpenGdsImportAsync(filePath, request);
            return;
        }

        await LoadDesignFromFileAsync(filePath, permission: request);
    }

    /// <summary>True for GDS II layout files (.gds/.gdsii, case-insensitive).</summary>
    internal static bool IsGdsFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(".gds", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".gdsii", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Hands a GDS pick from the open-design dialog to the import flow and fires
    /// <see cref="ProjectOpened"/> so the Home screen lets go of the window — the
    /// import dialog and its canvas result must not stay hidden behind it.
    /// Loading a file starts fresh, so the current design is cleared FIRST — the
    /// same reset the .lun load performs (<see cref="ClearCanvas"/> plus the
    /// load-time migration state <see cref="LoadDesignFromFileAsync"/> resets);
    /// the import flow itself only ADDS to the canvas and would otherwise merge
    /// into the existing content. The discard was covered by the unsaved-changes
    /// prompt before the pick. The clear marks the project dirty (canvas change
    /// tracking) and detaches it from the previous project file — a later Save
    /// must not overwrite a .lun the imported design did not come from.
    /// </summary>
    private async Task OpenGdsImportAsync(string gdsPath, NativeDesignRequest request)
    {
        if (OpenGdsImportRequested is null)
        {
            UpdateStatus?.Invoke(Services.Localization.LocalizationService.Instance
                .Translate("GdsImport.StatusUnavailable"));
            return;
        }

        Task? importing = null;
        CAP_Core.Documents.DocumentIdentity importedDocument = default;
        if (!request.TryCommit(() =>
            {
                ClearCanvas();
                _loadDiagnostics.PinCalibrationMigrations.Clear();
                CurrentFilePath = null;
                _loadedMetadata = null;
                importedDocument = _canvas.Document.CaptureIdentity();
                importing = OpenGdsImportRequested(gdsPath);
            })) return;
        await importing!;
        try
        {
            using var completed = _canvas.Document.CaptureOperation(importedDocument, scopeBound: false);
            completed.TryPublish(() => ProjectOpened?.Invoke());
        }
        catch (OperationCanceledException) { /* The importing document was replaced or retired. */ }
    }

    /// <summary>
    /// Loads a design directly from a file path (no file picker), prompting to save
    /// unsaved changes first. Used by the Home screen's recent-projects list and
    /// command-line file arguments.
    /// </summary>
    /// <param name="filePath">Absolute path to the .lun file to open.</param>
    /// <returns>True when the design was loaded; false when cancelled, missing, or failed.</returns>
    public async Task<bool> LoadDesignFromPathAsync(string filePath)
    {
        await using var request = NativeDesignRequest.Begin(_canvas.Document);
        if (request == null || !await ConfirmUnsavedChangesAsync(LoadPromptMessage, request)) return false;
        return await LoadDesignFromFileAsync(filePath, permission: request);
    }

    /// <summary>
    /// Opens a design as an untitled copy, detached from its source file — used
    /// for the Home screen's shipped examples. The design loads, but the file
    /// path stays null (Save prompts for a new location, so the source can't be
    /// overwritten), the design is marked unsaved, and the source is not
    /// recorded in the recent-projects list.
    /// </summary>
    /// <param name="filePath">Absolute path to the template/example .lun file.</param>
    /// <returns>True when the design was opened; false when cancelled, missing, or failed.</returns>
    public Task<bool> OpenDesignAsCopyAsync(string filePath) => OpenDesignAsCopyAsync(filePath, null);

    /// <summary>Opens a copy only while its initiating navigation request is still current.</summary>
    public async Task<bool> OpenDesignAsCopyAsync(string filePath, Func<bool>? canProceed,
        CAP_Contracts.ICommitPermission? permission = null, Action? onOpened = null)
    {
        await using var request = NativeDesignRequest.Begin(_canvas.Document, permission, canProceed);
        if (request == null || !await ConfirmUnsavedChangesAsync(LoadPromptMessage, request)) return false;
        return await LoadDesignFromFileAsync(filePath, recordRecent: false,
            asCopy: true, permission: request, onOpened: onOpened);
    }

    /// <summary>
    /// Core load step shared by the file-picker command and path-based loading:
    /// reads a .lun file and replaces the current canvas contents with it.
    /// Does NOT prompt about unsaved changes — callers do that first.
    /// </summary>
    private Task<bool> LoadDesignFromFileAsync(string filePath, bool recordRecent = true, Func<bool>? canProceed = null,
        bool asCopy = false, CAP_Contracts.ICommitPermission? permission = null, Action? onOpened = null) =>
        _nativeIo.LoadAsync(filePath, canProceed, data =>
            {
                ApplyLoadedDesign(filePath, recordRecent, data, asCopy);
                onOpened?.Invoke();
            },
            message => UpdateStatus?.Invoke(message), error =>
            {
                _errorConsole?.LogError($"Failed to load design: {error.Message}", error);
                UpdateStatus?.Invoke($"Load failed: {error.Message}");
            }, permission);

    private void ApplyLoadedDesign(string filePath, bool recordRecent, DesignFileData designData, bool asCopy)
    {
        if (designData.FormatVersion != CurrentFormatVersion)
        {
            var actual = string.IsNullOrEmpty(designData.FormatVersion) ? "<missing>" : designData.FormatVersion;
            _errorConsole?.LogWarning(
                $"Legacy .lun file detected (FormatVersion: {actual}). Loading with missing PIR sections (S-matrices, metadata, simulation results) left empty. File will be upgraded to {CurrentFormatVersion} on next save.");
        }

        // Clear current design
        _canvas.Document.ReplaceDocument();
        _canvas.Groups.ResetDocument();
        _canvas.CanvasFrozenPaths.Clear();
        _commandManager.ClearHistory();
        _loadDiagnostics.PinCalibrationMigrations.Clear();
        _loadDiagnostics.DisplacedConnections.Clear();
        _loadDiagnostics.VerticalPortConnections.Clear();
        _loadDiagnostics.FailedGroups.Clear();

        // Design-scoped imported components (#830): restore the sets embedded
        // in this .lun (replacing the previous design's) and migrate any legacy
        // global "GDS Import - *" PDKs the file still references — BOTH must
        // happen before the placements below resolve their templates.
        var migratedGdsSets = 0;
        if (DesignScopedGdsComponents != null)
        {
            DesignScopedGdsComponents.RestoreDesignScope(
                designData.ImportedGdsComponents, w => _errorConsole?.LogWarning(w));
            var referencedPdkSources = designData.Components.Select(c => c.PdkSource)
                .Concat(designData.Groups?.SelectMany(g => g.ChildComponents.Select(ch => ch.PdkSource))
                        ?? Enumerable.Empty<string?>())
                .OfType<string>();
            migratedGdsSets = DesignScopedGdsComponents.MigrateLegacyImportPdks(
                referencedPdkSources, w => _errorConsole?.LogWarning(w));
        }

        // Per-layer visibility overrides for imported geometry (#858).
        LayerVisibility?.Restore(designData.LayerVisibility);

        // Load standalone components
        foreach (var compData in designData.Components)
        {
            _componentMapper.LoadComponentFromData(compData);
        }

        // Load ComponentGroups
        var groupCount = 0;
        if (designData.Groups != null)
        {
            groupCount = _groupLoader.LoadGroups(designData.Groups, _processBindings.RestoreGroup, _loadDiagnostics.FailedGroups);
        }

        // Restore chip size BEFORE the connections: each restored blocked wire is
        // classified against the pathfinding grid (the .lun format persists the
        // blocked flag but not the reason), and the small startup grid would clamp
        // out-of-range pins into its blocked border column and misreport ordinary
        // contention as a footprint-sealed endpoint.
        bool hasWidth = designData.ChipWidthMicrometers.HasValue;
        bool hasHeight = designData.ChipHeightMicrometers.HasValue;
        if (hasWidth && hasHeight)
        {
            if (ApplyChipSizeAfterLoad != null)
            {
                ApplyChipSizeAfterLoad.Invoke(
                    designData.ChipWidthMicrometers!.Value,
                    designData.ChipHeightMicrometers!.Value);
            }
            else
            {
                _canvas.SetRootChipSize(
                    designData.ChipWidthMicrometers!.Value,
                    designData.ChipHeightMicrometers!.Value);
            }
        }
        else if (hasWidth || hasHeight)
        {
            _errorConsole?.LogWarning(
                $"File '{Path.GetFileName(filePath)}' has only one chip-size field set " +
                $"(width: {hasWidth}, height: {hasHeight}). Falling back to current canvas size.");
        }

        // Load connections (index-based references to _canvas.Components)
        foreach (var connData in designData.Connections)
        {
            _connectionLoader.LoadConnectionFromData(connData);
        }

        // Restore canvas-level pin-less frozen paths (issue #856); files
        // saved before the field simply have none.
        if (designData.CanvasFrozenPaths != null)
        {
            foreach (var pathDto in designData.CanvasFrozenPaths)
            {
                var frozenPath = CAP_DataAccess.Persistence.ComponentGroupSerializer
                    .FromCanvasFrozenPathDto(pathDto);
                _canvas.CanvasFrozenPaths.Add(new CanvasFrozenPathViewModel(frozenPath));
            }
        }

        _postLoad.RestoreDieScopeAfterLoad();

        // Restore the coherent interference mode (#1333) before post-load
        // routing recalculates transmissions; missing field (old files) = off.
        _canvas.ConnectionManager.EnableCoherentPropagationPhase =
            designData.CoherentPropagationPhase == true;
        CoherentModeRestoredAfterLoad?.Invoke();

        _loadDiagnostics.ReportPinCalibrationMigrations();

        // Rebuild dissolution records for loaded auto-inserted crossings (#705)
        // so they dissolve/re-evaluate exactly like ones inserted this session.
        _postLoad.RebuildCrossingRecords();

        // Notify all connections about their paths for UI rendering
        foreach (var conn in _canvas.Connections)
        {
            conn.NotifyPathChanged();
        }

        // Restore the designated analysis-output coupler (#754); files without
        // the field (older versions) simply load with no designation.
        _postLoad.RestoreAnalysisOutput(designData);

        // Preserve PIR metadata so Created date survives subsequent saves
        _loadedMetadata = asCopy ? null : designData.Metadata;

        _processBindings.RestoreDesign(designData,
            _canvas.Components.Select(vm => vm.Component), selection => ActiveProcess = selection);

        if (_models.Restore(designData.SMatrices) > 0)
            HasUnsavedChanges = true;

        CurrentFilePath = asCopy ? null : filePath;
        HasUnsavedChanges = asCopy;
        if (migratedGdsSets > 0)
        {
            // The design must be re-saved to embed the migrated components —
            // until then it still depends on the legacy user-PDK files.
            _errorConsole?.LogWarning(
                $"Migrated {migratedGdsSets} legacy imported GDS component set(s) into this design. " +
                "Save the design to embed them in the .lun file.");
            HasUnsavedChanges = true;
        }
        if (recordRecent)
        {
            _recentProjects?.RecordProject(filePath);
        }
        UpdateStatus?.Invoke($"Loaded {Path.GetFileName(filePath)} ({_canvas.Components.Count} components, {_canvas.Connections.Count} connections, {groupCount} groups)");
        _loadDiagnostics.ReportDisplacedConnections(UpdateStatus);
        _loadDiagnostics.ReportFailedGroups(UpdateStatus);
        _loadDiagnostics.ReportVerticalPortConnections(UpdateStatus);
        _commandManager.NotifyStateChanged();

        // Rebuild hierarchy tree after loading
        RebuildHierarchy?.Invoke();

        // Auto zoom-to-fit after loading
        ZoomToFitAfterLoad?.Invoke(900, 800);

        RestoreWorkspace?.Invoke(designData.Workspace);
        // Capture routing input only after the complete restored document is visible.
        PostLoadRouting = _postLoad.StartPostLoadRouting(MaxConnectionsRoutedOnLoad);
        ProjectOpened?.Invoke();
    }

    /// <summary>
    /// Prompts to save unsaved changes (Save / Don't Save / Cancel) before a
    /// destructive action. Returns true when it is safe to proceed: nothing was
    /// unsaved, the user saved successfully, or chose Don't Save. Returns false
    /// on Cancel or when the user aborted the save dialog.
    /// </summary>
    private async Task<bool> ConfirmUnsavedChangesAsync(string message, NativeDesignRequest request)
    {
        if (!request.IsCurrent) return false;
        if (!HasUnsavedChanges || MessageBoxService == null) return true;
        Task<SavePromptResult>? prompting = null;
        if (!request.TryCommit(() => prompting = MessageBoxService.ShowSavePromptAsync(message, "Save Changes?"))) return false;
        var result = await prompting!;
        if (!request.IsCurrent) return false;
        if (result == SavePromptResult.Save)
        {
            // Changes that landed during the write (e.g. a finished routing pass) keep the dirty
            // flag, but the user's explicit "Save" was honoured and the action may proceed.
            return await SaveWithDialogAsync(forcePicker: false, permission: request) && request.IsCurrent;
        }
        return result == SavePromptResult.DontSave;
    }

    /// <summary>
    /// Asks whether the application may close, prompting to save unsaved changes
    /// first. Wired to the main window's Closing event.
    /// </summary>
    /// <returns>True when closing may proceed; false when the user cancelled.</returns>
    public async Task<bool> ConfirmCloseAsync()
    {
        await using var request = NativeDesignRequest.Begin(_canvas.Document);
        return request != null && await ConfirmUnsavedChangesAsync("Do you want to save your changes before closing?", request);
    }

    /// <summary>
    /// Creates a new empty project, prompting to save if there are unsaved changes.
    /// Exits group edit mode if active before clearing the canvas.
    /// </summary>
    [RelayCommand]
    private async Task NewProject() => await TryNewProjectAsync();

    /// <summary>
    /// Command body of File → New, returning whether the new project was actually
    /// created. False means the user cancelled (save prompt or save dialog) and the
    /// current design is untouched — callers such as the process picker must NOT
    /// proceed in that case. An empty canvas is no substitute for this signal: the
    /// canvas can be empty while the operation was still cancelled.
    /// </summary>
    public Task<bool> TryNewProjectAsync() => TryNewProjectAsync(null);

    /// <summary>Creates a project only while the initiating navigation request is still current.</summary>
    public async Task<bool> TryNewProjectAsync(Func<bool>? canProceed)
    {
        await using var request = NativeDesignRequest.Begin(_canvas.Document, canProceed: canProceed);
        if (request == null || !await ConfirmUnsavedChangesAsync(
                "Do you want to save your changes before creating a new project?", request)) return false;
        return request.TryCommit(CreateNewProject);
    }

    private void CreateNewProject()
    {
        // Clear the canvas
        ClearCanvas();

        // The coherent interference mode is per design: a fresh project must not
        // inherit it from the previously loaded one (and later save it as its own).
        _canvas.ConnectionManager.EnableCoherentPropagationPhase = false;
        CoherentModeRestoredAfterLoad?.Invoke();

        CurrentFilePath = null;
        _loadedMetadata = null;
        HasUnsavedChanges = false;
        UpdateStatus?.Invoke("New project created");

        // Rebuild hierarchy
        RebuildHierarchy?.Invoke();

        RestoreWorkspace?.Invoke(null);
        ProjectOpened?.Invoke();
    }

    /// <summary>
    /// Clears all components and connections from the canvas.
    /// Also clears the per-component S-matrix override store: without this,
    /// File → New (which calls ClearCanvas) leaves overrides from the
    /// previous design behind. A subsequent Save would write them as
    /// orphan entries (no matching component by Identifier or template
    /// key) into the new file — the user gets warnings on next Load and
    /// state from the prior design leaks into a "fresh" project.
    /// </summary>
    private void ClearCanvas()
    {
        _canvas.Document.ReplaceDocument();
        _canvas.Groups.ResetDocument();
        _canvas.CanvasFrozenPaths.Clear();
        _canvas.AnalysisOutput.Clear();
        _commandManager.ClearHistory();
        StoredSMatrices.Clear();
        // Imported GDS components are design-scoped (#830): a fresh project must
        // not inherit the previous design's imported library entries.
        DesignScopedGdsComponents?.ClearDesignScope();
        // Layer-visibility overrides are per design (#858): reset to all-visible.
        LayerVisibility?.ClearForNewDesign();
    }

    internal static Component? FindComponentByIdentifierRecursive(ComponentGroup group, string identifier) =>
        NativeEndpointMapper.FindComponentByIdentifierRecursive(group, identifier);

    /// <summary>
    /// Upper bound of path-less connections the loader routes on its own. Larger designs keep
    /// their fallback lines and get a console hint instead, like the GDS import's guard for
    /// very large re-routes: one unroutable wire makes a routing pass retry every ordering
    /// and run for minutes, which must not happen silently on open.
    /// </summary>
    internal const int MaxConnectionsRoutedOnLoad = 300;

    [RelayCommand]
    private async Task ExportNazca()
    {
        var exportRequestId = GdsExport.BeginExportRequest();
        if (FileDialogService == null)
        {
            UpdateStatus?.Invoke("Export not available");
            return;
        }

        if (_canvas.Components.Count == 0)
        {
            UpdateStatus?.Invoke("Nothing to export - add some components first");
            return;
        }

        // A gdsfactory-native design (e.g. CornerStone SiN) cannot be expressed in a Nazca
        // script — those components would be silently omitted. Make the user choose consciously:
        // continue anyway, or switch to the gdsfactory export that can include them (#570).
        if (!await _nazcaWorkflow.ConfirmBackendAsync(MessageBoxService, RequestGdsFactoryExport, UpdateStatus))
            return;

        var filePath = await FileDialogService.ShowSaveFileDialogAsync(
            "Export to Nazca Python",
            "py",
            "Python Files|*.py|All Files|*.*");

        if (filePath != null)
        {
            // A script named like a Python module it imports (e.g. re.py, numpy.py)
            // shadows that module and fails with a cryptic circular-import error —
            // refuse the name up front instead of letting the Nazca run explode.
            var stem = Path.GetFileNameWithoutExtension(filePath);
            if (PythonModuleShadowing.ShadowsPythonModule(stem))
            {
                var warning = $"'{Path.GetFileName(filePath)}' shadows the Python module '{stem.ToLowerInvariant()}' "
                    + "— the exported script could not import Nazca. Please choose a different file name (e.g. chip1.py).";
                if (MessageBoxService != null)
                    await MessageBoxService.ShowChoicePromptAsync(warning, "Invalid script name", new[] { "OK" });
                UpdateStatus?.Invoke(warning);
                return;
            }

            await _nazcaWorkflow.ExportAsync(filePath, exportRequestId,
                MetalRoutingSpecProvider, CurrentFilePath, MessageBoxService,
                ShowSettingsWindow, UpdateStatus);
        }
    }

    [RelayCommand]
    private Task ExportSax() => _saxWorkflow.ExecuteAsync(FileDialogService, UpdateStatus);

    /// <summary>Writes separate, uniquely named files in die-local coordinates.</summary>
    [RelayCommand]
    private async Task ExportDieFiles()
    {
        var localization = Services.Localization.LocalizationService.Instance;
        var exportRequestId = GdsExport.BeginExportRequest();
        var dies = _canvas.Dies.RootDies.ToArray();
        if (dies.Length == 0)
        {
            UpdateStatus?.Invoke(localization.Translate("Dies.Export.NoDies"));
            return;
        }
        if (FileDialogService == null || !await _nazcaWorkflow.ConfirmBackendAsync(MessageBoxService, RequestGdsFactoryExport, UpdateStatus))
            return;

        var basePath = await FileDialogService.ShowSaveFileDialogAsync(
            localization.Translate("Dies.Export.DialogTitle"), "py", "Python Files|*.py|All Files|*.*");
        if (basePath == null) return;

        await _dieWorkflow.ExecuteAsync(basePath, dies, exportRequestId,
            MetalRoutingSpecProvider, MessageBoxService, ShowSettingsWindow, UpdateStatus);
    }

    private void OnDesignCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (!_canvas.Groups.IsProjecting) HasUnsavedChanges = true;
    }

    private void OnAnalysisOutputChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs args) => HasUnsavedChanges = true;

    /// <summary>Detaches the design and model subscriptions owned by this file-operation session.</summary>
    public void Dispose()
    {
        _canvas.Components.CollectionChanged -= OnDesignCollectionChanged;
        _canvas.Connections.CollectionChanged -= OnDesignCollectionChanged;
        _canvas.AnalysisOutput.PropertyChanged -= OnAnalysisOutputChanged;
        _models.Dispose();
    }
}

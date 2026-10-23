using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Notifications;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Analysis.OnaAnalysis;
using CAP.Avalonia.ViewModels.Components.AddCustomComponent;
using CAP.Avalonia.ViewModels.ComponentSettings;
using CAP_Core.Components.Core;
using CAP_DataAccess.Components.AddCustomComponent;
using CAP_DataAccess.Components.ComponentDraftMapper;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.PdkImport;
using CAP.Avalonia.ViewModels.Process;
using CAP.Avalonia.Views.Dialogs;
using CAP.Avalonia.Views.PdkImport;
using CAP.Avalonia.ViewModels.Solvers;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace CAP.Avalonia.Views;

public partial class MainWindow : Window
{
    /// <summary>Set once the user confirmed closing so the guard lets the re-issued Close() through.</summary>
    private bool _forceClose;

    /// <summary>Suppresses a second save prompt while the first close confirmation is still open.</summary>
    private bool _confirmingClose;
    private bool _retiring;

    /// <summary>Application composition owns bridge, shell and container retirement.</summary>
    public Func<Task>? RetireApplicationAsync { get; init; }

    private readonly Shell.ComponentEditorHostFactory _editorHosts;
    private readonly Shell.ShellToolWindowFactory _toolWindows;
    private readonly Shell.ShellPanelDialogBindings _panelDialogs;
    private readonly Services.Lifetime.CallbackBindings _windowBindings = new();
    private readonly Services.Lifetime.CallbackBindings _shutdownBindings = new();
    private Shell.WindowFileDialogBindings? _fileDialogs;
    private IDisposable? _exportDialogs;

    private Shell.PdkWindowHost? _pdkWindows;
    private Shell.ComponentSettingsWindowHost? _componentSettingsWindows;

    /// <summary>
    /// The open Component Registry browser window (issue #656). Both entry points
    /// (Component Library header 🌐 and the Tools flyout) share it: a second open
    /// activates the existing window instead of spawning a duplicate — same
    /// pattern as the PDK editors. Cleared when it closes.
    /// </summary>
    private RegistryBrowserWindow? _registryBrowserWindow;

    /// <summary>
    /// The open AI Design Assistant tool window. Single instance: a second open
    /// activates the existing window instead of spawning a duplicate — same
    /// pattern as <see cref="_registryBrowserWindow"/>. Cleared when it closes.
    /// </summary>
    private AiAssistantWindow? _aiAssistantWindow;

    /// <summary>
    /// The open ISA playground tool window (issue #1194). Single instance: a second
    /// open activates the existing window instead of spawning a duplicate — same
    /// pattern as <see cref="_aiAssistantWindow"/>. Cleared when it closes.
    /// </summary>
    private IsaPlaygroundWindow? _isaPlaygroundWindow;

    public void ClearAllGroupSelections() => LibraryPane.ClearAllGroupSelections();

    private void PdkEditProcess_Click(object? sender, RoutedEventArgs e) => _pdkWindows?.EditProcess(sender, e);
    private void PdkDelete_Click(object? sender, RoutedEventArgs e) => _pdkWindows?.DeletePdk(sender, e);
    private async void PdkCreate_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdkWindows != null) await _pdkWindows.CreateAsync(this);
    }

    public MainWindow() : this(new(), new(), new()) { }

    internal MainWindow(Shell.ComponentEditorHostFactory editorHosts, Shell.ShellToolWindowFactory toolWindows,
        Shell.ShellPanelDialogBindings panelDialogs)
    {
        (_editorHosts, _toolWindows, _panelDialogs) = (editorHosts, toolWindows, panelDialogs);
        InitializeComponent();

        // Guard against losing unsaved changes when the window is closed
        Closing += OnWindowClosing;
        Closed += (_, _) => { ReleaseWindowBindings(); _shutdownBindings.Dispose(); };

        // Set up the FileDialogService when the window is loaded
        Loaded += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                var settingsWindows = _toolWindows.CreateSettings(this, status => vm.StatusText = status);
                _windowBindings.Bind(() => vm.ShowSettingsWindowAsync, value => vm.ShowSettingsWindowAsync = value, settingsWindows.ShowAsync);
                Action openSolverSettings = () => LogSettingsNavigationFaults(
                    vm.ShowSettingsWindowAsync?.Invoke(typeof(ViewModels.Settings.Tidy3dSettingsPage)), vm);
                var editors = _editorHosts.Create(this, vm.LeftPanel, vm.Canvas, vm.FileOperations,
                    component => vm.CanvasInteraction.PlacementContext.ResolveComponentPdkSource(component),
                    vm.ErrorConsole, openSolverSettings);
                _pdkWindows = editors.Pdks;
                _componentSettingsWindows = editors.Settings;
                var componentEditors = editors.Editors;
                _shutdownBindings.Bind(() => vm.Update.ConfirmShutdownAsync, value => vm.Update.ConfirmShutdownAsync = value, vm.FileOperations.ConfirmCloseAsync);
                _shutdownBindings.Bind(() => vm.Update.RetireBeforeShutdownAsync, value => vm.Update.RetireBeforeShutdownAsync = value, () => RetireAsync(vm));
                _shutdownBindings.Bind(() => vm.Update.ShutdownAfterConfirmation, value => vm.Update.ShutdownAfterConfirmation = value, () =>
                {
                    _forceClose = true;
                    if (global::Avalonia.Application.Current?.ApplicationLifetime is
                        global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
                        lifetime.Shutdown();
                    else Close();
                });
                _panelDialogs.Attach(this, vm.LeftPanel, analyzer => OpenOnaAnalyzerWindow(analyzer, vm));

                _fileDialogs = new Shell.WindowFileDialogBindings(this, vm.FileOperations, vm.GdsFactoryExport,
                    vm.LeftPanel, vm.GdsImport, vm.RightPanel, vm.BottomPanel);
                vm.FileOperations.MessageBoxService = new MessageBoxService();
                _windowBindings.Bind(() => vm.RightPanel.OnaAnalysis.OpenWindowAsync, value => vm.RightPanel.OnaAnalysis.OpenWindowAsync = value,
                    analyzer => OpenOnaAnalyzerWindow(analyzer, vm));
                _exportDialogs = ExportDialogWiring.Wire(vm, this, vm.ErrorConsole);
                _windowBindings.Bind(() => vm.ViewportControl.GetViewportSize, value => vm.ViewportControl.GetViewportSize = value, GetActualViewportSize);
                _windowBindings.Bind(() => vm.ViewportControl.GetCursorCanvasPosition, value => vm.ViewportControl.GetCursorCanvasPosition = value,
                    () => DesignCanvasControl.IsPointerOver ? (DesignCanvasControl.LastCanvasPosition.X, DesignCanvasControl.LastCanvasPosition.Y) : null);

                // Wire up rename dialog for group templates
                _windowBindings.Bind(() => vm.LeftPanel.ComponentLibrary.ShowRenameDialogAsync, value => vm.LeftPanel.ComponentLibrary.ShowRenameDialogAsync = value, async currentName =>
                {
                    var dialog = new RenameDialog(currentName);
                    return await dialog.ShowDialog<string?>(this);
                });

                // Wire up the GDS import dialog (#808): the button ViewModel builds the
                // dialog ViewModel (service + placement executor included), the view layer
                // only wraps it in a window — same pattern as the PDK wizard above.
                _windowBindings.Bind(() => vm.GdsImport.ShowImportDialogAsync, value => vm.GdsImport.ShowImportDialogAsync = value, async dialogVm =>
                {
                    var dialog = new Views.Dialogs.GdsImportDialog { DataContext = dialogVm };
                    await dialog.ShowDialog(this);
                });

                // Wire up the "New Component" window (issue #656) — non-modal, like the
                // Fabrication Process and ONA Analyzer tool windows, so the user can keep
                // iterating on the design while it stays open.
                _windowBindings.Bind(() => vm.LeftPanel.ShowNewComponentWindowAsync, value => vm.LeftPanel.ShowNewComponentWindowAsync = value, componentEditors.ShowAsync);

                // Wire up PDK Offset Editor window
                _windowBindings.BindReference(() => vm.PdkOffsetEditor.FileDialogService,
                    value => vm.PdkOffsetEditor.FileDialogService = value, new FileDialogService(this));
                _windowBindings.Bind(() => vm.ShowPdkOffsetEditorRequested, value => vm.ShowPdkOffsetEditorRequested = value, () =>
                {
                    var editorVm = vm.PdkOffsetEditor;
                    var editorWindow = new PdkOffsetEditorWindow
                    {
                        DataContext = editorVm
                    };
                    editorWindow.Show(this);
                });

                // Wire up the New-Design process-selection dialog (issue #570)
                _windowBindings.Bind(() => vm.ShowProcessSelectionAsync, value => vm.ShowProcessSelectionAsync = value, async groups =>
                {
                    var pvm = new ProcessSelectionViewModel(groups);
                    var dlg = new ProcessSelectionDialog { DataContext = pvm };
                    await dlg.ShowDialog(this);
                    return pvm.Result;
                });

                // Prompt for the fabrication process (issue #570) once the Home screen
                // is dismissed. A design opened from Home/startup restores its own
                // active process, so only a truly fresh start still needs the picker;
                // prompting over the Home card would be premature and redundant.
                var processPromptShown = false;
                void MaybePromptForInitialProcess()
                {
                    if (_windowBindings.IsDisposed || processPromptShown || vm.Home.IsHomeVisible || vm.FileOperations.ActiveProcess != null)
                        return;
                    processPromptShown = true;
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(
                        async () => { if (!_windowBindings.IsDisposed) await vm.PromptForInitialProcessAsync(); },
                        global::Avalonia.Threading.DispatcherPriority.Background);
                }
                _windowBindings.Observe(vm.Home, (_, args) =>
                {
                    if (args.PropertyName == nameof(vm.Home.IsHomeVisible))
                        MaybePromptForInitialProcess();
                });

                // Wire up clipboard for RoutingDiagnostics
                _windowBindings.Bind(() => vm.RightPanel.RoutingDiagnostics.CopyToClipboard, value => vm.RightPanel.RoutingDiagnostics.CopyToClipboard = value, async text =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                });

                // Wire up clipboard for the Netlist panel (#687)
                _windowBindings.Bind(() => vm.RightPanel.Netlist.CopyToClipboard, value => vm.RightPanel.Netlist.CopyToClipboard = value, async text =>
                {
                    var clipboard = Clipboard ?? throw new InvalidOperationException(
                        Services.Localization.LocalizationService.Instance.Translate("Netlist.ClipboardNotAvailable"));
                    await clipboard.SetTextAsync(text);
                });

                // Wire up clipboard for DimensionValidator
                _windowBindings.Bind(() => vm.RightPanel.DimensionValidator.CopyToClipboard, value => vm.RightPanel.DimensionValidator.CopyToClipboard = value, async text =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                });

                // Wire up clipboard for ErrorConsole
                _windowBindings.Bind(() => vm.BottomPanel.ErrorConsole.CopyToClipboard, value => vm.BottomPanel.ErrorConsole.CopyToClipboard = value, async text =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                });

                // Wire up auto-scroll: scroll to the newest entry when entries are added
                _windowBindings.Observe(vm.BottomPanel.ErrorConsole, (s, e) =>
                {
                    if (e.PropertyName == nameof(vm.BottomPanel.ErrorConsole.EntryCount) && ErrorConsoleListBox != null)
                    {
                        var items = ErrorConsoleListBox.ItemsSource;
                        if (items is System.Collections.IList list && list.Count > 0)
                        {
                            ErrorConsoleListBox.ScrollIntoView(list[list.Count - 1]);
                        }
                    }
                });

                // Both component context-menu entry points share one routing — see
                // OpenComponentEditorOrSettings.
                _windowBindings.Bind(() => vm.LeftPanel.HierarchyPanel.OpenComponentSettings, value => vm.LeftPanel.HierarchyPanel.OpenComponentSettings = value,
                    node => OpenComponentEditorOrSettings(node.ComponentViewModel, node.Component, vm));
                _windowBindings.Bind(() => vm.CanvasInteraction.OpenComponentSettings, value => vm.CanvasInteraction.OpenComponentSettings = value,
                    compVm => OpenComponentEditorOrSettings(compVm, compVm.Component, vm));

                // Wire up per-instance S-matrix override marker in hierarchy
                _windowBindings.Bind(() => vm.LeftPanel.HierarchyPanel.CheckHasSMatrixOverride, value => vm.LeftPanel.HierarchyPanel.CheckHasSMatrixOverride = value,
                    id => vm.FileOperations.StoredSMatrices.ContainsKey(id));

                // Initial badge population for PDK templates (covers user-global
                // overrides loaded from disk on app start). Updated again every
                // time the component-settings host mutates the user store.
                _componentSettingsWindows.RefreshTemplateOverrideBadges();

                // Wire up GridSplitter resize events
                SetupPanelResizing(vm);

                // Startup project — LAST, after every service/callback above is
                // wired (dialogs, hierarchy override badges). A command-line
                // .lun file wins; otherwise honor the reopen-last preference.
                // The Home card is disabled while the load is in flight.
                vm.Home.IsStartupLoadInProgress = true;
                try
                {
                    if (vm.StartupDesignFile is { } startupFile)
                    {
                        await vm.FileOperations.LoadDesignFromPathAsync(startupFile);
                    }
                    else
                    {
                        await vm.Home.TryReopenLastProjectAsync();
                    }
                }
                finally
                {
                    vm.Home.IsStartupLoadInProgress = false;
                }

                // Covers the startup-load path: Home is already dismissed, but a
                // legacy design may not have restored an active process.
                MaybePromptForInitialProcess();
            }
        };
    }

    /// <summary>
    /// Prompts to save unsaved changes before the window closes. Cancels the
    /// close, asks via <see cref="ViewModels.Panels.FileOperationsViewModel.ConfirmCloseAsync"/>,
    /// and re-issues Close() when the user confirmed (saved or discarded).
    /// </summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || DataContext is not MainViewModel vm)
            return;

        e.Cancel = true;
        if (_confirmingClose || _retiring)
            return;

        _confirmingClose = true;
        try
        {
            if (!vm.FileOperations.HasUnsavedChanges || await vm.FileOperations.ConfirmCloseAsync())
            {
                await RetireAsync(vm);
                _forceClose = true;
                Close();
            }
        }
        finally
        {
            _confirmingClose = false;
        }
    }

    private Task RetireAsync(MainViewModel shell)
    {
        _retiring = true;
        IsEnabled = false;
        ReleaseWindowBindings();
        return RetireApplicationAsync?.Invoke() ?? shell.DisposeAsync().AsTask();
    }

    private void ReleaseWindowBindings()
    {
        _windowBindings.Dispose();
        _panelDialogs.Dispose();
        _fileDialogs?.Dispose();
        _exportDialogs?.Dispose();
    }

    /// <summary>
    /// Sets up panel resizing by setting initial widths and listening to GridSplitter DragCompleted events.
    /// </summary>
    private void SetupPanelResizing(MainViewModel vm)
    {
        // Set initial widths from saved preferences
        if (LeftPanelGrid != null && LeftPanelGrid.ColumnDefinitions.Count > 0)
        {
            LeftPanelGrid.ColumnDefinitions[0].Width = new GridLength(vm.LeftPanel.LeftPanelWidth.Value, GridUnitType.Pixel);
        }

        if (RightPanelGrid != null && RightPanelGrid.ColumnDefinitions.Count > 1)
        {
            RightPanelGrid.ColumnDefinitions[1].Width = new GridLength(vm.RightPanel.RightPanelWidth.Value, GridUnitType.Pixel);
        }

        // Listen to GridSplitter drag events to save new widths
        // Left panel resizing - we need to find the GridSplitter in LeftPanelGrid
        if (LeftPanelGrid != null)
        {
            var leftSplitter = LeftPanelGrid.Children.OfType<GridSplitter>().FirstOrDefault();
            if (leftSplitter != null)
            {
                leftSplitter.DragDelta += (s, e) =>
                    ResizePanelColumn(LeftPanelGrid.ColumnDefinitions[0], e.Vector.X, LeftPanelBorder);
                leftSplitter.DragCompleted += (s, e) =>
                {
                    if (LeftPanelGrid.ColumnDefinitions.Count > 0)
                    {
                        var newWidth = LeftPanelGrid.ColumnDefinitions[0].Width.Value;
                        if (newWidth > 0)
                        {
                            vm.LeftPanel.LeftPanelWidth = new GridLength(newWidth);
                        }
                    }
                };
            }
        }

        // Right panel resizing
        if (RightPanelGrid != null)
        {
            var rightSplitter = RightPanelGrid.Children.OfType<GridSplitter>().FirstOrDefault();
            if (rightSplitter != null)
            {
                rightSplitter.DragDelta += (s, e) =>
                    ResizePanelColumn(RightPanelGrid.ColumnDefinitions[1], -e.Vector.X, RightPanelBorder);
                rightSplitter.DragCompleted += (s, e) =>
                {
                    if (RightPanelGrid.ColumnDefinitions.Count > 1)
                    {
                        var newWidth = RightPanelGrid.ColumnDefinitions[1].Width.Value;
                        if (newWidth > 0)
                        {
                            vm.RightPanel.RightPanelWidth = new GridLength(newWidth);
                        }
                    }
                };
            }
        }
    }

    /// <summary>
    /// Applies a splitter drag to a side panel's pixel column. Each side panel is a grid docked
    /// into the window's DockPanel with the splitter in its outermost column, so the GridSplitter
    /// itself finds no neighbouring column to trade space with and leaves the width alone; the
    /// drag is applied here instead, clamped to the panel's own Min/MaxWidth.
    /// </summary>
    private static void ResizePanelColumn(ColumnDefinition column, double delta, Layoutable panel)
    {
        double width = Math.Clamp(column.Width.Value + delta, panel.MinWidth, panel.MaxWidth);
        column.Width = new GridLength(width, GridUnitType.Pixel);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (DataContext is not MainViewModel mainVm) return;

        // While the Home screen overlay is shown, canvas shortcuts don't apply.
        // Escape dismisses it (same as "Continue without a project") — checked
        // BEFORE the TextBox guard so it also works from the recents search box.
        if (mainVm.Home.IsHomeVisible)
        {
            if (e.Key == Key.Escape)
            {
                mainVm.Home.ContinueWithoutProjectCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }

        // Don't intercept keystrokes when a text input has focus (e.g., search box)
        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        var ctrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Global keyboard shortcuts that work regardless of focus
        switch (e.Key)
        {
            case Key.A when ctrlPressed || e.KeyModifiers.HasFlag(KeyModifiers.Meta):
                mainVm.SelectAllCommand.Execute(null);
                break;
            case Key.N:
                if (ctrlPressed)
                    mainVm.NewProjectCommand.Execute(null);
                break;
            case Key.S:
                if (ctrlPressed)
                    mainVm.SaveDesignCommand.Execute(null);
                else
                    mainVm.SetSelectModeCommand.Execute(null);
                break;
            case Key.C:
                if (ctrlPressed)
                {
                    Console.WriteLine("DEBUG: Ctrl+C detected");
                    mainVm.CopySelectedCommand.Execute(null);
                }
                else
                    mainVm.SetConnectModeCommand.Execute(null);
                break;
            case Key.V:
                if (ctrlPressed)
                {
                    Console.WriteLine("DEBUG: Ctrl+V detected");
                    // Get the last canvas position for paste-at-cursor
                    var canvasPos = DesignCanvasControl.LastCanvasPosition;
                    mainVm.PasteSelected(canvasPos.X, canvasPos.Y);
                }
                else
                {
                    mainVm.GateView.ToggleCommand.Execute(null);
                }
                break;
            case Key.D:
                if (!ctrlPressed)
                    mainVm.SetDeleteModeCommand.Execute(null);
                break;
            case Key.Delete:
            case Key.Back:
                mainVm.DeleteSelectedCommand.Execute(null);
                break;
            case Key.Escape:
                // First priority: Exit group edit mode if active (via command for undo/redo)
                if (mainVm.Canvas.IsInGroupEditMode)
                {
                    if (mainVm.Canvas.CurrentEditGroup != null)
                    {
                        var exitCmd = new Commands.ExitGroupEditModeCommand(
                            mainVm.Canvas, mainVm.Canvas.CurrentEditGroup);
                        mainVm.CommandManager.ExecuteCommand(exitCmd);
                    }
                    else
                    {
                        mainVm.Canvas.ExitGroupEditMode();
                    }
                    mainVm.StatusText = "Exited group edit mode";
                }
                else
                {
                    mainVm.SetSelectModeCommand.Execute(null);
                }
                break;
            case Key.Z:
                if (ctrlPressed)
                    mainVm.UndoCommand.Execute(null);
                break;
            case Key.Y:
                if (ctrlPressed)
                    mainVm.RedoCommand.Execute(null);
                break;
            case Key.R:
                if (!ctrlPressed)
                    mainVm.RotateSelectedCommand.Execute(null);
                break;
            case Key.G:
                if (!ctrlPressed)
                {
                    var canvas = mainVm.Canvas;
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        canvas.ShowGridOverlay = !canvas.ShowGridOverlay;
                    }
                    else
                    {
                        canvas.GridSnap.Toggle();
                        mainVm.StatusText = canvas.GridSnap.IsEnabled
                            ? $"Grid snap ON ({canvas.GridSnap.GridSizeMicrometers}\u00b5m)"
                            : "Grid snap OFF";
                    }
                }
                break;
            case Key.F:
                if (!ctrlPressed)
                {
                    var (width, height) = GetActualViewportSize();
                    mainVm.ZoomToFit(width, height);
                }
                break;
            case Key.P:
                if (!ctrlPressed)
                    mainVm.TogglePowerOverlayCommand.Execute(null);
                break;
            case Key.L:
                if (!ctrlPressed)
                    mainVm.RunWorkspaceFromShortcutCommand.Execute(null);
                break;
            default:
                return; // Don't mark as handled for unrecognized keys
        }

        e.Handled = true;
        DesignCanvasControl.InvalidateVisual();
    }

    /// <summary>
    /// Opens the "Compute Modes for Waveguide" dialog from the Tools menu.
    /// </summary>
    private async void OpenModeSolverDialog_Click(object? sender, RoutedEventArgs e)
    {
        if (_retiring) return;
        await _toolWindows.ShowModeSolverAsync(this);
    }

    /// <summary>
    /// Opens the non-modal "Component Registry" browser window (issue #656) from
    /// either entry point (Component Library header 🌐 / Tools flyout). A second
    /// click activates the already-open window; the lazy index load happens in
    /// the window's own Opened hook.
    /// </summary>
    private void OpenRegistryBrowser_Click(object? sender, RoutedEventArgs e) =>
        OpenRegistryBrowserWindow();

    /// <summary>
    /// Link row under the local library hits (issue #772): opens the registry window
    /// with the library search pre-filled — same window-dedup as the other entry
    /// points, so a second click only activates the window and refreshes its search.
    /// </summary>
    private void OpenRegistrySearchHint_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        vm.Registry.SearchText = vm.LeftPanel.SearchText;
        OpenRegistryBrowserWindow();
    }

    private void OpenRegistryBrowserWindow()
    {
        if (_registryBrowserWindow is { IsVisible: true } existing)
        {
            // Un-minimize first: Activate() alone leaves a minimized window
            // minimized, which looks like the button silently did nothing.
            existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        if (DataContext is not MainViewModel vm)
            return;

        var window = new RegistryBrowserWindow { DataContext = vm.Registry };
        _registryBrowserWindow = window;
        // Only clear the field if it still points at THIS window (defensive,
        // mirrors the keyed-dictionary windows above).
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_registryBrowserWindow, window))
                _registryBrowserWindow = null;
        };
        window.Show(this);
    }

    /// <summary>
    /// Opens the non-modal AI Design Assistant tool window from the toolbar.
    /// A second click activates the already-open window.
    /// </summary>
    private void OpenAiAssistant_Click(object? sender, RoutedEventArgs e)
    {
        if (_aiAssistantWindow is { IsVisible: true } existing)
        {
            existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        if (DataContext is not MainViewModel vm)
            return;

        var window = new AiAssistantWindow { DataContext = vm };
        _aiAssistantWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_aiAssistantWindow, window))
                _aiAssistantWindow = null;
        };
        window.Show(this);
    }

    /// <summary>
    /// Opens the non-modal ISA playground tool window from the Tools flyout
    /// (issue #1194). A second click activates the already-open window.
    /// </summary>
    private void OpenIsaPlayground_Click(object? sender, RoutedEventArgs e)
    {
        if (_isaPlaygroundWindow is { IsVisible: true } existing)
        {
            // Un-minimize first: Activate() alone leaves a minimized window minimized.
            existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        var window = _toolWindows.CreatePlayground();
        if (window?.DataContext is not ViewModels.Logic.IsaPlayground.IsaPlaygroundViewModel vm) return;
        if (DataContext is ViewModels.MainViewModel main)
            Services.IsaPlaygroundNavigation.Attach(vm, main);
        // The "Run a program on your chip" tour (#1267) hosts its card here for
        // the playground steps and tracks the window's open state.
        _isaPlaygroundWindow = window;
        // Only clear the field if it still points at THIS window.
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_isaPlaygroundWindow, window))
                _isaPlaygroundWindow = null;
        };
        window.Show(this);
    }

    /// <summary>
    /// Opens the "Check PDKs against Python" dialog from the Tools menu (issue #515).
    /// </summary>
    private void OpenPdkResolutionCheckDialog_Click(object? sender, RoutedEventArgs e)
    {
        _toolWindows.ShowPdkCheck(this);
    }

    private void ZoomToFitButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var (width, height) = GetActualViewportSize();
            vm.ZoomToFit(width, height);
        }
    }

    /// <summary>
    /// Repaints the canvas after the toolbar grid-snap toggle flipped
    /// <see cref="ViewModels.Canvas.GridSnapSettings.IsEnabled"/> (the grid is drawn only while snapping).
    /// </summary>
    private void GridSnapToggle_Click(object? sender, RoutedEventArgs e) =>
        DesignCanvasControl.InvalidateVisual();

    /// <summary>
    /// Opens the analysis dock on the Checks tab after the "Check design" menu entry
    /// ran the validation (the bound command does the checking itself).
    /// </summary>
    private void CheckDesignMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.BottomPanel.Analysis.OpenChecks();
    }

    private async void ExportMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ViewModels.Export.Formats.NetlistClipboardExportFormat format }
            || DataContext is not MainViewModel vm)
            return;

        // Handle activation ourselves so the command runs once and the flyout stays bound while copying.
        e.Handled = true;
        if (!format.ExportCommand.CanExecute(null)) return;
        await format.ExportCommand.ExecuteAsync(null);
        if (vm.RightPanel.Netlist.LastCopySucceeded)
            Toolbar.HideExportFlyout();
    }

    /// <summary>
    /// Opens the "Check for openEBL…" dialog (issue #1361): exports the current design to
    /// GDS and runs the openEBL submission + verification checks on it. The ViewModel is a
    /// DI singleton so a reopened dialog keeps the entered username/design name.
    /// </summary>
    private async void CheckOpenEblMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        var designName = DataContext is MainViewModel vm && vm.FileOperations.CurrentFilePath != null
            ? System.IO.Path.GetFileNameWithoutExtension(vm.FileOperations.CurrentFilePath)
            : null;
        await _toolWindows.ShowOpenEblAsync(this, designName);
    }

    /// <summary>
    /// Gets the actual viewport size (visible area) independent of zoom level.
    /// Uses the DesignCanvas control's own layout bounds, which correctly excludes
    /// the left panel, right panel, and toolbar from the viewport dimensions.
    /// The rendering coordinate space is the canvas local space, so ZoomToFit
    /// must use canvas dimensions — not window dimensions — for correct centering.
    /// </summary>
    private (double width, double height) GetActualViewportSize()
    {
        // Use the canvas control's actual layout bounds.
        // This is correct because PanX/PanY are in canvas-local coordinates,
        // and ZoomToFit computes pan as: vpWidth/2 - boxCenterX * zoom.
        // Using window ClientSize (which includes sidebars) would shift the
        // computed pan center by (windowWidth - canvasWidth) / 2, causing the
        // "wrong position on first F-press" bug.
        var canvasBounds = DesignCanvasControl.Bounds;
        if (canvasBounds.Width > 0 && canvasBounds.Height > 0)
            return (canvasBounds.Width, canvasBounds.Height);

        // Fallback: if the canvas has not been laid out yet, use window client size.
        var windowWidth = ClientSize.Width;
        var windowHeight = ClientSize.Height;
        if (windowWidth > 0 && windowHeight > 0)
            return (windowWidth, windowHeight);

        return (1400, 900); // Last-resort default matching the initial window size
    }

    private void TemplateEditComponent_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        if (sender is Control { DataContext: ComponentTemplate template })
        {
            vm.LeftPanel.EditCustomComponentCommand.Execute(template);
        }
    }

    /// <summary>
    /// Handles "Delete…" click in the PDK template list context menu (LC-T5): confirms, then
    /// moves the component out of its user PDK file into <c>.trash</c> (backing up the pre-edit
    /// file) and out of the library via <see cref="LeftPanelViewModel.RemoveCustomComponentCommand"/>.
    /// Only wired to a visible/enabled menu item for custom (non-Foundry) templates — same
    /// <c>IsCustom</c> binding as "Edit…" — but repeats the authoritative
    /// <see cref="LeftPanelViewModel.CanEditTemplate"/> guard here before even showing the
    /// confirm dialog. Placed components on the canvas are never touched (Design Checks flag any
    /// resulting conflict).
    /// </summary>
    private async void TemplateDeleteComponent_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        // Accept both the context-menu item and the inline hover ✕ button.
        if (sender is not Control { DataContext: ComponentTemplate template } || !vm.LeftPanel.CanDeleteTemplate(template))
            return;

        // On a fork of a bundled PDK, a component the built-in original also has is not
        // deleted but reverted to the foundry definition.
        var isRevertToBundled = vm.LeftPanel.IsComponentRevertToBundled(template);
        var prompt = isRevertToBundled
            ? $"Remove your customized copy of '{template.Name}' and restore the built-in original?\n\n"
              + "A full backup of your customized PDK file is saved to user-pdks/.trash first. "
              + "Placed instances on the canvas are kept."
            : $"Move component '{template.Name}' to trash?\n\n"
              + "The PDK file is rewritten without this component; a full pre-edit backup "
              + "(including any hand-added JSON comments, which the rewrite does not preserve) is "
              + "saved to user-pdks/.trash first. Placed instances on the canvas are kept.";
        var choice = await new MessageBoxService().ShowChoicePromptAsync(
            prompt, "Delete Component?", new[] { "Cancel", isRevertToBundled ? "Restore Original" : "Move to Trash" });
        if (choice != 1)
            return;

        vm.LeftPanel.RemoveCustomComponentCommand.Execute(template);
    }

    /// <summary>
    /// Fire-and-forget Settings navigation with fault logging: the Tidy3D deep-link
    /// must surface a navigation crash in the error console instead of dropping it
    /// into an unobserved task fault.
    /// </summary>
    private static void LogSettingsNavigationFaults(Task? navigation, MainViewModel vm)
    {
        if (navigation is null)
            return;
        navigation.ContinueWith(
            t => vm.ErrorConsole.LogError(
                $"Opening the Tidy3D settings page failed: {t.Exception?.GetBaseException().Message}",
                t.Exception?.GetBaseException()),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Shared routing for both component context-menu entry points (canvas and hierarchy):
    /// an editable PDK template opens the unified "Edit Component" editor via the same command
    /// as the library's ✏ button, so fork-on-edit and window dedup apply automatically;
    /// otherwise (ComponentGroups, template-less legacy instances) falls back to the
    /// per-instance Component Settings dialog instead of a misleading error.
    /// </summary>
    private void OpenComponentEditorOrSettings(
        CAP.Avalonia.ViewModels.Canvas.ComponentViewModel? compVm,
        CAP_Core.Components.Core.Component component,
        MainViewModel vm)
    {
        var template = compVm is null
            ? null
            : CanvasComponentTemplateResolver.ResolveEditable(
                compVm, vm.LeftPanel.AllTemplates,
                vm.CanvasInteraction.PlacementContext.ResolveComponentPdkSource,
                vm.LeftPanel.CanEditTemplate);
        if (template is not null)
        {
            vm.LeftPanel.EditCustomComponentCommand.Execute(template);
            return;
        }

        _componentSettingsWindows?.Show(
            component.Identifier,
            component.HumanReadableName ?? component.Identifier,
            component);
    }

    /// <summary>
    /// Returns the display names of canvas components the given instance would overlap
    /// if resized to <paramref name="width"/> × <paramref name="height"/> at its current
    /// position. Non-blocking advisory used by the Nazca code editor's overlap warning.
    /// </summary>
    private static IReadOnlyList<string> FindOverlappingComponentNames(
        MainViewModel vm, CAP_Core.Components.Core.Component liveComponent, double width, double height)
    {
        var compVm = vm.Canvas.Components.FirstOrDefault(c => c.Component == liveComponent);
        if (compVm == null)
            return System.Array.Empty<string>();

        // CanPlaceComponent returns false on ANY overlap or chip-boundary violation;
        // when it reports a clash, enumerate the specific neighbours for the message.
        if (vm.Canvas.CanPlaceComponent(compVm.X, compVm.Y, width, height, excludeComponent: compVm))
            return System.Array.Empty<string>();

        var names = new List<string>();
        foreach (var other in vm.Canvas.Components)
        {
            if (other == compVm) continue;
            bool overlaps = compVm.X < other.X + other.Width &&
                            compVm.X + width > other.X &&
                            compVm.Y < other.Y + other.Height &&
                            compVm.Y + height > other.Y;
            if (overlaps)
                names.Add(other.Component.HumanReadableName ?? other.Component.Identifier);
        }
        return names;
    }

    /// <summary>
    /// Opens a new ONA Analyzer tool window bound to the given analyzer component.
    /// Each call creates a fresh <see cref="OnaSweepViewModel"/> so several
    /// analyzers can be inspected side-by-side; the window is non-modal.
    /// </summary>
    private System.Threading.Tasks.Task OpenOnaAnalyzerWindow(CAP_Core.Components.Core.Component analyzer, MainViewModel vm)
    {
        return _toolWindows.ShowOnaAsync(this, analyzer, vm.Canvas, vm.FileDialogService);
    }
}

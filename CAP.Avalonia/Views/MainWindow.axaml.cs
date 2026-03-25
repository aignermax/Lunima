using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using System.ComponentModel;
using System.Linq;

namespace CAP.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set up the FileDialogService when the window is loaded
        Loaded += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.FileDialogService = new FileDialogService(this);
                vm.FileOperations.MessageBoxService = new MessageBoxService();
                vm.Sweep.FileDialogService = vm.FileDialogService;
                vm.RoutingDiagnostics.FileDialogService = vm.FileDialogService;
                vm.ViewportControl.GetViewportSize = GetActualViewportSize;

                // Wire up clipboard for RoutingDiagnostics
                vm.RoutingDiagnostics.CopyToClipboard = async (text) =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                };

                // Wire up clipboard for DimensionValidator
                vm.DimensionValidator.CopyToClipboard = async (text) =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                };

                // Wire up GridSplitter resize events
                SetupPanelResizing(vm);
            }
        };
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (DataContext is not MainViewModel mainVm) return;

        // Don't intercept keystrokes when a text input has focus (e.g., search box)
        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        var ctrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Global keyboard shortcuts that work regardless of focus
        switch (e.Key)
        {
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
                            ? $"Grid snap ON ({canvas.GridSnap.GridSizeMicrometers}µm)"
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
                {
                    var canvasVm = mainVm.Canvas;
                    if (!canvasVm.ShowPowerFlow)
                    {
                        if (canvasVm.PowerFlowVisualizer.CurrentResult == null)
                            mainVm.RunSimulationCommand.Execute(null);
                        else
                        {
                            canvasVm.ShowPowerFlow = true;
                            canvasVm.PowerFlowVisualizer.IsEnabled = true;
                        }
                        mainVm.StatusText = "Power flow overlay: ON (auto-updates on changes)";
                    }
                    else
                    {
                        canvasVm.ShowPowerFlow = false;
                        canvasVm.PowerFlowVisualizer.IsEnabled = false;
                        mainVm.StatusText = "Power flow overlay: OFF";
                    }
                }
                break;
            case Key.L:
                if (!ctrlPressed)
                    mainVm.RunSimulationCommand.Execute(null);
                break;
            default:
                return; // Don't mark as handled for unrecognized keys
        }

        e.Handled = true;
        DesignCanvasControl.InvalidateVisual();
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
    /// Gets the actual viewport size (visible area) independent of zoom level.
    /// Uses the Window's client area bounds as the viewport.
    /// </summary>
    private (double width, double height) GetActualViewportSize()
    {
        // The viewport is the visible area of the window where the canvas is displayed.
        // We use ClientSize (or Bounds) of the Window, which is independent of canvas zoom.
        // This ensures ZoomToFit calculates correctly regardless of current zoom level.

        // Use the window's client area size
        var windowWidth = ClientSize.Width;
        var windowHeight = ClientSize.Height;

        // Fallback to reasonable defaults if window size is not available
        if (windowWidth <= 0 || windowHeight <= 0)
        {
            return (1400, 900); // Default window size
        }

        return (windowWidth, windowHeight);
    }

    /// <summary>
    /// Handles pointer entering a group template item (shows delete button).
    /// </summary>
    private void OnGroupItemPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is GroupTemplateItemViewModel itemVm)
        {
            itemVm.IsHovered = true;
        }
    }

    /// <summary>
    /// Handles pointer leaving a group template item (hides delete button).
    /// </summary>
    private void OnGroupItemPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is GroupTemplateItemViewModel itemVm)
        {
            itemVm.IsHovered = false;
        }
    }

}

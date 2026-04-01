using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using System.Linq;

namespace CAP.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.FileDialogService = new FileDialogService(this);
                vm.FileOperations.MessageBoxService = new MessageBoxService();
                vm.RightPanel.Sweep.FileDialogService = vm.FileDialogService;
                vm.RightPanel.RoutingDiagnostics.FileDialogService = vm.FileDialogService;
                vm.ViewportControl.GetViewportSize = GetActualViewportSize;

                // Wire up clipboard for RoutingDiagnostics
                vm.RightPanel.RoutingDiagnostics.CopyToClipboard = async (text) =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                        await clipboard.SetTextAsync(text);
                };

                // Wire up clipboard for DimensionValidator
                vm.RightPanel.DimensionValidator.CopyToClipboard = async (text) =>
                {
                    var clipboard = Clipboard;
                    if (clipboard != null)
                        await clipboard.SetTextAsync(text);
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
        if (LeftPanelGrid != null && LeftPanelGrid.ColumnDefinitions.Count > 0)
            LeftPanelGrid.ColumnDefinitions[0].Width = new GridLength(vm.LeftPanel.LeftPanelWidth.Value, GridUnitType.Pixel);

        if (RightPanelGrid != null && RightPanelGrid.ColumnDefinitions.Count > 1)
            RightPanelGrid.ColumnDefinitions[1].Width = new GridLength(vm.RightPanel.RightPanelWidth.Value, GridUnitType.Pixel);

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
                            vm.LeftPanel.LeftPanelWidth = new GridLength(newWidth);
                    }
                };
            }
        }

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
                            vm.RightPanel.RightPanelWidth = new GridLength(newWidth);
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
                        canvas.ShowGridOverlay = !canvas.ShowGridOverlay;
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
                return;
        }

        e.Handled = true;
        DesignCanvasControl.InvalidateVisual();
    }

    /// <summary>
    /// Gets the actual viewport size (canvas bounds) independent of zoom level.
    /// </summary>
    private (double width, double height) GetActualViewportSize()
    {
        var canvasBounds = DesignCanvasControl.Bounds;
        if (canvasBounds.Width > 0 && canvasBounds.Height > 0)
            return (canvasBounds.Width, canvasBounds.Height);

        var windowWidth = ClientSize.Width;
        var windowHeight = ClientSize.Height;
        if (windowWidth > 0 && windowHeight > 0)
            return (windowWidth, windowHeight);

        return (1400, 900);
    }
}

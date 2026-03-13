using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;

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
                vm.Sweep.FileDialogService = vm.FileDialogService;
                vm.RoutingDiagnostics.FileDialogService = vm.FileDialogService;
                vm.ViewportControl.GetViewportSize = () => (
                    DesignCanvasControl.Bounds.Width,
                    DesignCanvasControl.Bounds.Height);

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
            }
        };
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
                mainVm.SetSelectModeCommand.Execute(null);
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
                    mainVm.ZoomToFit(DesignCanvasControl.Bounds.Width, DesignCanvasControl.Bounds.Height);
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
            vm.ZoomToFit(DesignCanvasControl.Bounds.Width, DesignCanvasControl.Bounds.Height);
        }
    }

}

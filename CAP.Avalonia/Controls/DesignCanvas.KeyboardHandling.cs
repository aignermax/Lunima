using Avalonia.Input;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Keyboard event handling methods for DesignCanvas.
/// </summary>
public partial class DesignCanvas
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var mainVm = MainViewModel;
        if (mainVm == null) return;

        bool ctrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.S:
                HandleSKey(ctrlPressed, mainVm);
                break;
            case Key.C:
                if (!ctrlPressed)
                    mainVm.SetConnectModeCommand.Execute(null);
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
                    HandleGKey(e);
                }
                break;
            case Key.F:
                if (!ctrlPressed)
                {
                    mainVm.ZoomToFit(Bounds.Width, Bounds.Height);
                }
                break;
            case Key.P:
                if (!ctrlPressed)
                {
                    HandlePKey(mainVm);
                }
                break;
            case Key.L:
                if (!ctrlPressed)
                {
                    mainVm?.RunSimulationCommand.Execute(null);
                }
                break;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private void HandleSKey(bool ctrlPressed, MainViewModel mainVm)
    {
        if (ctrlPressed)
            mainVm.SaveDesignCommand.Execute(null);
        else
            mainVm.SetSelectModeCommand.Execute(null);
    }

    private void HandleGKey(KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm != null)
        {
            bool shiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (shiftPressed)
            {
                vm.ShowGridOverlay = !vm.ShowGridOverlay;
            }
            else
            {
                vm.GridSnap.Toggle();
                if (MainViewModel != null)
                {
                    MainViewModel.StatusText = vm.GridSnap.IsEnabled
                        ? $"Grid snap ON ({vm.GridSnap.GridSizeMicrometers}µm)"
                        : "Grid snap OFF";
                }
            }
        }
    }

    private void HandlePKey(MainViewModel mainVm)
    {
        var canvasVm = ViewModel;
        if (canvasVm != null)
        {
            if (!canvasVm.ShowPowerFlow)
            {
                if (canvasVm.PowerFlowVisualizer.CurrentResult == null)
                {
                    mainVm?.RunSimulationCommand.Execute(null);
                }
                else
                {
                    canvasVm.ShowPowerFlow = true;
                    canvasVm.PowerFlowVisualizer.IsEnabled = true;
                }
                if (mainVm != null)
                    mainVm.StatusText = "Power flow overlay: ON (auto-updates on changes)";
            }
            else
            {
                canvasVm.ShowPowerFlow = false;
                canvasVm.PowerFlowVisualizer.IsEnabled = false;
                if (mainVm != null)
                    mainVm.StatusText = "Power flow overlay: OFF";
            }
        }
    }
}

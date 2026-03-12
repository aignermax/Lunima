using CommunityToolkit.Mvvm.ComponentModel;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Commands;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the bottom panel.
/// Contains waveguide length configuration, element locking, and status text.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class BottomPanelViewModel : ObservableObject
{
    /// <summary>
    /// ViewModel for parameterized waveguide length configuration (phase matching).
    /// </summary>
    public WaveguideLengthViewModel WaveguideLength { get; }

    /// <summary>
    /// ViewModel for locking/unlocking components and connections.
    /// </summary>
    public ElementLockViewModel ElementLock { get; }

    [ObservableProperty]
    private string _statusText = "Ready";

    public BottomPanelViewModel(DesignCanvasViewModel canvas, CommandManager commandManager)
    {
        WaveguideLength = new WaveguideLengthViewModel();
        ElementLock = new ElementLockViewModel();

        // Configure ViewModels that need dependencies
        ElementLock.Configure(canvas, commandManager);
    }

    /// <summary>
    /// Updates the status text display.
    /// </summary>
    public void SetStatus(string status)
    {
        StatusText = status;
    }
}

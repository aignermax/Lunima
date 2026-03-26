using CommunityToolkit.Mvvm.ComponentModel;
using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.Commands;
using CAP_Core;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the bottom panel.
/// Contains waveguide length configuration, element locking, status text, and error console.
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

    /// <summary>
    /// ViewModel for the collapsible error console panel.
    /// </summary>
    public ErrorConsoleViewModel ErrorConsole { get; }

    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>
    /// Initializes the bottom panel with canvas, command manager, and error console service.
    /// </summary>
    public BottomPanelViewModel(DesignCanvasViewModel canvas, CommandManager commandManager, ErrorConsoleService errorConsoleService)
    {
        WaveguideLength = new WaveguideLengthViewModel();
        ElementLock = new ElementLockViewModel();
        ErrorConsole = new ErrorConsoleViewModel(errorConsoleService);

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

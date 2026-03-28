using CAP_Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Diagnostics;
using CAP.Avalonia.Services;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the right sidebar panel.
/// Contains analysis, diagnostics, and validation features.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class RightPanelViewModel : ObservableObject
{
    private readonly UserPreferencesService _preferencesService;

    private GridLength _rightPanelWidth = new GridLength(250);
    /// <summary>
    /// Width of the right panel in pixels. Persisted in user preferences.
    /// Clamped to [200, 800] range.
    /// </summary>
    public GridLength RightPanelWidth
    {
        get => _rightPanelWidth;
        set
        {
            // Clamp to reasonable values (min 200, max 800)
            var clampedValue = Math.Max(200, Math.Min(800, value.Value));
            var newGridLength = new GridLength(clampedValue);
            if (SetProperty(ref _rightPanelWidth, newGridLength))
            {
                SaveRightPanelWidth();
            }
        }
    }

    /// <summary>
    /// ViewModel for the PDK Consistency panel (validates JSON PDK definitions vs Nazca).
    /// Issue #334: Investigate PDK JSON vs Nazca Python coordinate mismatch.
    /// </summary>
    public PdkConsistencyViewModel PdkConsistency { get; }

    /// <summary>Initializes a new instance of <see cref="RightPanelViewModel"/>.</summary>
    public RightPanelViewModel(DesignCanvasViewModel canvas, UserPreferencesService preferencesService, ErrorConsoleService? errorConsole = null)
    {
        _preferencesService = preferencesService;

        PdkConsistency = new PdkConsistencyViewModel();
    }

    /// <summary>
    /// Initializes the panel (loads saved width from preferences).
    /// </summary>
    public void Initialize()
    {
        RestoreRightPanelWidth();
    }

    private void RestoreRightPanelWidth()
    {
        var width = _preferencesService.GetRightPanelWidth();
        RightPanelWidth = new GridLength(width);
    }

    private void SaveRightPanelWidth()
    {
        _preferencesService.SetRightPanelWidth(RightPanelWidth.Value);
    }
}

using CAP_Core.Analysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CAP.Avalonia.ViewModels.Canvas.ViewSettings;

/// <summary>
/// View-only canvas settings the active workspace decides: the drawing emphasis and the
/// design-check marker overlay. Nothing here touches the design; changing it only repaints.
/// </summary>
public partial class CanvasViewSettings : ObservableObject
{
    private IReadOnlyList<DesignIssue> _designCheckFindings = Array.Empty<DesignIssue>();

    /// <summary>What the canvas draws in the foreground (geometry or light).</summary>
    [ObservableProperty]
    private CanvasEmphasis _emphasis = CanvasEmphasis.Geometry;

    /// <summary>
    /// Whether the findings of the last design-check run are marked on the canvas
    /// (problems as warning badges, hints as faint rings).
    /// </summary>
    [ObservableProperty]
    private bool _showDesignCheckMarkers = true;

    /// <summary>
    /// The findings of the last design-check run that still describe the design; empty
    /// after any edit, until the checks run again.
    /// </summary>
    public IReadOnlyList<DesignIssue> DesignCheckFindings
    {
        get => _designCheckFindings;
        private set => SetProperty(ref _designCheckFindings, value);
    }

    /// <summary>True while there are current findings to mark.</summary>
    public bool HasDesignCheckFindings => DesignCheckFindings.Count > 0;

    /// <summary>Shows the findings of a finished design-check run.</summary>
    public void SetDesignCheckFindings(IEnumerable<DesignIssue> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        DesignCheckFindings = findings.ToList();
        OnPropertyChanged(nameof(HasDesignCheckFindings));
    }

    /// <summary>
    /// Drops the findings: the design changed, so their positions may no longer be true.
    /// A stale marker would point at a problem that is gone.
    /// </summary>
    public void ClearDesignCheckFindings()
    {
        if (DesignCheckFindings.Count == 0)
            return;
        DesignCheckFindings = Array.Empty<DesignIssue>();
        OnPropertyChanged(nameof(HasDesignCheckFindings));
    }

    /// <summary>Overlays menu: shows or hides the design-check markers.</summary>
    [RelayCommand]
    private void ToggleDesignCheckMarkers() => ShowDesignCheckMarkers = !ShowDesignCheckMarkers;
}

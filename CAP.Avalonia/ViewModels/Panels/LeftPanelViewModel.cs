using CommunityToolkit.Mvvm.ComponentModel;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Hierarchy;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Creation;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the left sidebar panel.
/// Contains hierarchy panel, component library, and PDK management features.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class LeftPanelViewModel : ObservableObject
{
    /// <summary>
    /// ViewModel for the hierarchy panel showing component tree structure.
    /// </summary>
    public HierarchyPanelViewModel HierarchyPanel { get; }

    /// <summary>
    /// ViewModel for PDK management (loading, filtering, enabling/disabling PDKs).
    /// </summary>
    public PdkManagerViewModel PdkManager { get; }

    /// <summary>
    /// ViewModel for managing saved ComponentGroup templates.
    /// </summary>
    public ComponentLibraryViewModel ComponentLibrary { get; }

    public LeftPanelViewModel(DesignCanvasViewModel canvas, GroupLibraryManager libraryManager)
    {
        HierarchyPanel = new HierarchyPanelViewModel(canvas);
        PdkManager = new PdkManagerViewModel();
        ComponentLibrary = new ComponentLibraryViewModel(libraryManager);
    }

    /// <summary>
    /// Configures the PDK manager with a filter callback.
    /// Called during MainViewModel initialization.
    /// </summary>
    public void ConfigurePdkManager(Action filterCallback)
    {
        PdkManager.OnFilterChanged = filterCallback;
    }
}

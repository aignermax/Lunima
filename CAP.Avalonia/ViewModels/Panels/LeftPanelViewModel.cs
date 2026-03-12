using CommunityToolkit.Mvvm.ComponentModel;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.ViewModels.Panels;

/// <summary>
/// ViewModel for the left sidebar panel.
/// Contains component library and PDK management features.
/// Max 250 lines per CLAUDE.md guideline.
/// </summary>
public partial class LeftPanelViewModel : ObservableObject
{
    /// <summary>
    /// ViewModel for PDK management (loading, filtering, enabling/disabling PDKs).
    /// </summary>
    public PdkManagerViewModel PdkManager { get; }

    /// <summary>
    /// Component group manager for saving and loading user-defined component groups.
    /// </summary>
    public ComponentGroupViewModel ComponentGroups { get; }

    public LeftPanelViewModel()
    {
        PdkManager = new PdkManagerViewModel();
        ComponentGroups = new ComponentGroupViewModel();
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

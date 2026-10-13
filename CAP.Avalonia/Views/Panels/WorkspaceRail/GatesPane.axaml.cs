using Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Views.Panels.WorkspaceRail;

/// <summary>
/// The left rail's Gates pane (Logic workspace default): saved gates to drag in or
/// click-to-place, the starter gate examples to open, and "Make gate from selection".
/// </summary>
public partial class GatesPane : UserControl
{
    /// <summary>Initializes the pane.</summary>
    public GatesPane()
    {
        InitializeComponent();
    }

    /// <summary>Click-to-place: a picked saved gate arms placement like a Saved Groups row.</summary>
    private void OnSavedGatesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not ListBox listBox)
            return;
        if (listBox.SelectedItem is GroupTemplateItemViewModel item)
            vm.LeftPanel.SelectedGroupTemplate = item.Template;
    }
}

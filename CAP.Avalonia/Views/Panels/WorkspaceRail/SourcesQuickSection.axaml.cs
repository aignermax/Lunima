using Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Views.Panels.WorkspaceRail;

/// <summary>
/// The Optics workspace's "Sources &amp; detectors" quick section on top of the component
/// library: light sources, fiber couplers and detectors, drag-in or click-to-place.
/// </summary>
public partial class SourcesQuickSection : UserControl
{
    /// <summary>Initializes the section.</summary>
    public SourcesQuickSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Click-to-place: arms the picked template. One-way on purpose — a two-way binding would
    /// clear the main library's selection whenever it picks a template this list lacks.
    /// </summary>
    private void OnSourcesSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.AddedItems.Count > 0 && e.AddedItems[0] is ComponentTemplate template)
            vm.CanvasInteraction.SelectedTemplate = template;
    }
}

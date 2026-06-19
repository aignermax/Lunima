using Avalonia.Controls;

namespace CAP.Avalonia.Views.Panels;

/// <summary>
/// Right-panel section that hosts the per-component property editor for
/// whatever is currently selected on the canvas. The actual editor view
/// is picked from a set of DataTemplates by the editor ViewModel's runtime
/// type — see <c>ComponentEditorFactory</c>.
/// </summary>
public partial class SelectedComponentPropertiesPanel : UserControl
{
    /// <summary>Initialises the panel.</summary>
    public SelectedComponentPropertiesPanel()
    {
        InitializeComponent();
    }
}

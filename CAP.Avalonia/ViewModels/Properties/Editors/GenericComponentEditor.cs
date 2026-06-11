using System.Globalization;
using CAP.Avalonia.ViewModels.Canvas;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Properties.Editors;

/// <summary>
/// Fallback editor for components that do not have a specialised editor.
/// Shows read-only metadata (name, position, size, PDK function) so the user
/// at least confirms what they selected.
/// </summary>
public partial class GenericComponentEditorViewModel : ObservableObject
{
    private readonly ComponentViewModel _componentVm;
    private readonly CultureInfo _ci = CultureInfo.InvariantCulture;

    /// <summary>Display name of the underlying component.</summary>
    public string ComponentName => !string.IsNullOrEmpty(_componentVm.Component.HumanReadableName)
        ? _componentVm.Component.HumanReadableName!
        : _componentVm.Component.Name;

    /// <summary>PDK Nazca function this template was generated from.</summary>
    public string NazcaFunction => _componentVm.Component.NazcaFunctionName ?? "(none)";

    /// <summary>Component position on the canvas, in micrometres.</summary>
    public string Position => string.Format(_ci, "({0:0.##}, {1:0.##}) µm",
        _componentVm.Component.PhysicalX, _componentVm.Component.PhysicalY);

    /// <summary>Component bounding-box size in micrometres.</summary>
    public string Size => string.Format(_ci, "{0:0.##} × {1:0.##} µm",
        _componentVm.Component.WidthMicrometers, _componentVm.Component.HeightMicrometers);

    /// <summary>Initialises the generic editor.</summary>
    public GenericComponentEditorViewModel(ComponentViewModel componentVm)
    {
        _componentVm = componentVm;
    }
}

/// <summary>Provider that always returns a generic editor — must be registered last.</summary>
public class GenericComponentEditorProvider : IComponentEditorProvider
{
    /// <inheritdoc/>
    public object? TryCreateEditor(ComponentViewModel componentVm)
        => new GenericComponentEditorViewModel(componentVm);
}

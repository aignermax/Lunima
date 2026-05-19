using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Simulation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Properties.Editors;

/// <summary>
/// Editor ViewModel for a light-source component (grating coupler, edge
/// coupler). Exposes the per-instance <see cref="LaserConfig"/> so the user
/// can set this source's wavelength and input power without leaving the
/// canvas.
/// </summary>
public partial class LightSourceEditorViewModel : ObservableObject
{
    private readonly ComponentViewModel _componentVm;

    /// <summary>Display name of the underlying component.</summary>
    public string ComponentName => !string.IsNullOrEmpty(_componentVm.Component.HumanReadableName)
        ? _componentVm.Component.HumanReadableName!
        : _componentVm.Component.Name;

    /// <summary>The per-instance laser configuration bound by the panel.</summary>
    public LaserConfig? LaserConfig => _componentVm.LaserConfig;

    /// <summary>Creates the editor for the given light-source component.</summary>
    public LightSourceEditorViewModel(ComponentViewModel componentVm)
    {
        _componentVm = componentVm;
    }
}

/// <summary>Provider that surfaces the laser editor for components with a <see cref="LaserConfig"/>.</summary>
public class LightSourceEditorProvider : IComponentEditorProvider
{
    /// <inheritdoc/>
    public object? TryCreateEditor(ComponentViewModel componentVm)
    {
        if (componentVm.Component.IsAnalysisTool) return null;
        if (!componentVm.IsLightSource) return null;
        return new LightSourceEditorViewModel(componentVm);
    }
}

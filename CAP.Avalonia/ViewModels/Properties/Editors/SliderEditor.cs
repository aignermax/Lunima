using CAP.Avalonia.ViewModels.Canvas;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Properties.Editors;

/// <summary>
/// Editor ViewModel for any component that exposes a slider (phase shifter,
/// tunable coupler, etc.). Wraps <see cref="ComponentViewModel"/>'s existing
/// slider API and republishes value changes so the slider control in the
/// properties panel binds cleanly.
/// </summary>
public partial class SliderEditorViewModel : ObservableObject
{
    private readonly ComponentViewModel _componentVm;

    /// <summary>Display name of the underlying component.</summary>
    public string ComponentName => !string.IsNullOrEmpty(_componentVm.Component.HumanReadableName)
        ? _componentVm.Component.HumanReadableName!
        : _componentVm.Component.Name;

    /// <summary>Label for the slider (e.g. "Phase (°)" — derived from the component).</summary>
    public string SliderLabel => _componentVm.SliderLabel;

    /// <summary>Minimum slider value.</summary>
    public double Min => _componentVm.SliderMin;

    /// <summary>Maximum slider value.</summary>
    public double Max => _componentVm.SliderMax;

    /// <summary>Current slider value; writes propagate into the component's S-matrix.</summary>
    public double Value
    {
        get => _componentVm.SliderValue;
        set
        {
            if (Math.Abs(_componentVm.SliderValue - value) < double.Epsilon) return;
            _componentVm.SliderValue = value;
            OnPropertyChanged(nameof(Value));
        }
    }

    /// <summary>Creates the slider editor for the given component view-model.</summary>
    public SliderEditorViewModel(ComponentViewModel componentVm)
    {
        _componentVm = componentVm;
    }
}

/// <summary>Provider that surfaces a slider editor for any component with a slider.</summary>
public class SliderEditorProvider : IComponentEditorProvider
{
    /// <inheritdoc/>
    public object? TryCreateEditor(ComponentViewModel componentVm)
    {
        if (!componentVm.HasSliders) return null;
        // Skip if the component is also a light source (those get the laser
        // editor) or analysis tool — keeps editor priority sensible.
        if (componentVm.IsLightSource) return null;
        if (componentVm.Component.IsAnalysisTool) return null;
        return new SliderEditorViewModel(componentVm);
    }
}

using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Sets one slider (a tunable parameter such as a phase or coupling ratio) of a placed
/// component, clamped to its range, and asks for a re-simulation — the same effect as moving
/// the slider in the properties panel, but undoable.
/// </summary>
public class SetSliderValueCommand : IUndoableCommand
{
    private readonly ComponentViewModel _component;
    private readonly Slider _slider;
    private readonly double _oldValue;
    private readonly double _newValue;

    /// <summary>Creates the command; <paramref name="value"/> is clamped to the slider's range.</summary>
    public SetSliderValueCommand(ComponentViewModel component, Slider slider, double value)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        _slider = slider ?? throw new ArgumentNullException(nameof(slider));
        _oldValue = slider.Value;
        _newValue = Math.Clamp(value, slider.MinValue, slider.MaxValue);
    }

    /// <summary>The value the slider gets (after clamping).</summary>
    public double NewValue => _newValue;

    /// <inheritdoc/>
    public string Description => $"Set {_component.Name} parameter {_slider.Number} to {_newValue:G4}";

    /// <inheritdoc/>
    public void Execute() => Apply(_newValue);

    /// <inheritdoc/>
    public void Undo() => Apply(_oldValue);

    private void Apply(double value)
    {
        _slider.Value = value;
        _component.OnSliderChanged?.Invoke();
    }
}

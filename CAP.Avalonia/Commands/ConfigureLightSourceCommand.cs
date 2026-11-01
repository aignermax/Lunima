using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Simulation;

namespace CAP.Avalonia.Commands;

/// <summary>
/// Changes a light source's laser — on/off, wavelength and input power — in one undoable step.
/// Values left null keep their current setting.
/// </summary>
public class ConfigureLightSourceCommand : IUndoableCommand
{
    private readonly LaserConfig _laser;
    private readonly string _name;
    private readonly (bool Enabled, int WavelengthNm, double Power) _before;
    private readonly (bool Enabled, int WavelengthNm, double Power) _after;

    /// <summary>Creates the command for a light-source component.</summary>
    /// <exception cref="ArgumentException">The component is not a light source.</exception>
    public ConfigureLightSourceCommand(ComponentViewModel source, bool? enabled, int? wavelengthNm, double? power)
    {
        ArgumentNullException.ThrowIfNull(source);
        _laser = source.LaserConfig ?? throw new ArgumentException($"'{source.Name}' is not a light source.", nameof(source));
        _name = source.Name;
        _before = (_laser.IsEnabled, _laser.WavelengthNm, _laser.InputPower);
        _after = (enabled ?? _before.Enabled, wavelengthNm ?? _before.WavelengthNm, power ?? _before.Power);
    }

    /// <inheritdoc/>
    public string Description => $"Configure light source '{_name}'";

    /// <inheritdoc/>
    public void Execute() => Apply(_after);

    /// <inheritdoc/>
    public void Undo() => Apply(_before);

    private void Apply((bool Enabled, int WavelengthNm, double Power) state)
    {
        _laser.IsEnabled = state.Enabled;
        _laser.WavelengthNm = state.WavelengthNm;
        _laser.InputPower = state.Power;
    }
}

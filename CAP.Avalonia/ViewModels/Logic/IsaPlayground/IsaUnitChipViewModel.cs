using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// One chip in the playground's "what runs on light" row (issue #1456): the unit
/// name (ALU, Z, RAM, ACC, PC) plus whether that unit is currently computed by the
/// photonic network the user built (green "light") or simulated electronically
/// (grey). PC is always electronic — there is no photonic program counter yet;
/// the ACC chip lights up on the combined ALU + RAM + ACC chip, whose register
/// clocks the accumulator on light (issue #1479).
/// </summary>
public partial class IsaUnitChipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isOnLight;

    /// <summary>Creates a chip with the given unit name; starts electronic.</summary>
    public IsaUnitChipViewModel(string unitName)
    {
        UnitName = unitName;
    }

    /// <summary>The unit label shown on the chip (ALU, Z, RAM, ACC, PC).</summary>
    public string UnitName { get; }
}

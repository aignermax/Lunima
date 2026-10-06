using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// One chip in the playground's "what runs on light" row (issue #1456): the unit
/// name (ALU, Z, RAM, ACC, PC) plus whether that unit is currently computed by the
/// photonic network the user built (green "light") or simulated electronically
/// (grey). ACC and PC are always electronic — there is no photonic accumulator or
/// program counter yet.
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

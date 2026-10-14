using Avalonia.Media;
using CAP.Avalonia.Controls.Rendering;
using CAP_Core.Components.Dies;
using CAP_Core.Components.Process;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Dies;

/// <summary>A process a die can be fabricated in, as offered in a die's process selector.</summary>
/// <param name="Name">Display name of the process.</param>
/// <param name="Selection">The process selection a die bound to it carries.</param>
public sealed record DieProcessOption(string Name, ActiveProcessSelection Selection)
{
    /// <summary>The process's die-frame color.</summary>
    public IBrush Brush => DieProcessColors.BrushFor(Selection);

    /// <summary>The selector shows the name.</summary>
    public override string ToString() => Name;

    /// <summary>One option per catalog process, in catalog order.</summary>
    public static IReadOnlyList<DieProcessOption> FromCatalog(IEnumerable<ProcessGroup> catalog) =>
        catalog.Select(g => new DieProcessOption(g.DisplayName, ActiveProcessSelection.ForGroup(g))).ToList();
}

/// <summary>A die size choice: a process preset, or the "Custom" entry.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Preset">The preset, null for Custom.</param>
public sealed record DieSizeOption(string Name, DieSizePreset? Preset)
{
    /// <summary>True for the Custom entry (the user types width and height).</summary>
    public bool IsCustom => Preset == null;

    /// <summary>The selector shows the name.</summary>
    public override string ToString() => Name;

    /// <summary>The presets of <paramref name="process"/> followed by a Custom entry labelled <paramref name="customLabel"/>.</summary>
    public static IReadOnlyList<DieSizeOption> For(ActiveProcessSelection? process, string customLabel) =>
        DieSizePresets.For(process).Select(p => new DieSizeOption(p.Name, p))
            .Append(new DieSizeOption(customLabel, null)).ToList();
}

/// <summary>One PDK of a die's process with its enabled state on that die.</summary>
public partial class DiePdkOption : ObservableObject
{
    private readonly Action _changed;

    /// <summary>Creates the option; <paramref name="changed"/> runs when the user toggles it.</summary>
    public DiePdkOption(string name, bool isEnabled, Action changed)
    {
        Name = name;
        _isEnabled = isEnabled;
        _changed = changed;
    }

    /// <summary>PDK name.</summary>
    public string Name { get; }

    /// <summary>True when parts of this PDK may be placed on the die.</summary>
    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _changed();
}

namespace CAP.Avalonia.ViewModels.Workspaces.ToolSets;

/// <summary>What a workspace lets the user place from its left panel.</summary>
public enum LibraryScope
{
    /// <summary>Every component, saved group and PDK macro (Layout).</summary>
    AllComponents,

    /// <summary>Only instruments: where light enters, leaves or is measured (Optics).</summary>
    Instruments,

    /// <summary>Only saved groups that are logic gates; placing one places its physical group (Logic).</summary>
    Gates,

    /// <summary>Nothing — the workspace works on what exists (System: dies and their links).</summary>
    None,
}

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>The sections the main window's left icon rail can show in its single pane.</summary>
public enum LeftRailSection
{
    /// <summary>Component library with saved groups and PDK macros (default).</summary>
    Library,

    /// <summary>Design hierarchy tree.</summary>
    Hierarchy,

    /// <summary>Loaded PDKs: enable/disable, edit, create, trash.</summary>
    Pdks,
}

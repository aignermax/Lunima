using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CAP.Avalonia.ViewModels.Workspaces.ToolSets;

/// <summary>
/// Which workspace-dependent entries the canvas context menu shows for the element under the
/// cursor. "Edit Component" (component / S-matrix editor) applies only to real components and
/// only where the workspace's tool set offers it (Layout, Optics) — never to groups, gates or
/// dies. Logic offers gate actions instead: "Show in Layout", and "Edit gate…" for a group that
/// already is a gate ("Make gate…" otherwise). Editing entries (copy, group, delete …) stay in
/// every workspace.
/// </summary>
public partial class CanvasContextMenuViewModel : ObservableObject
{
    /// <summary>True when "Edit Component…" is offered.</summary>
    [ObservableProperty]
    private bool _showsEditComponent;

    /// <summary>True when "Show in Layout" is offered (Logic, any element).</summary>
    [ObservableProperty]
    private bool _showsShowInLayout;

    /// <summary>True when the target already is a gate: the gate entry reads "Edit gate…".</summary>
    [ObservableProperty]
    private bool _isGateTarget;

    /// <summary>True when the gate entry reads "Make gate…" (no gate under the cursor).</summary>
    public bool ShowsMakeGate => !IsGateTarget;

    /// <summary>True when the gate entry reads "Edit gate…".</summary>
    public bool ShowsEditGate => IsGateTarget;

    /// <summary>Recomputes the entries for <paramref name="workspace"/> and the element under the cursor.</summary>
    /// <param name="workspace">The active workspace.</param>
    /// <param name="target">The selected (right-clicked) element; null on empty canvas.</param>
    public void Refresh(Workspace workspace, ComponentViewModel? target)
    {
        var toolSet = WorkspaceProfile.For(workspace).ToolSet;
        var component = target?.Component;
        ShowsEditComponent = toolSet.AllowsComponentEditor && component is not null and not ComponentGroup;
        ShowsShowInLayout = workspace == Workspace.Logic && component is not null;
        IsGateTarget = component is ComponentGroup { TruthTablePinAssignment: not null };
    }

    partial void OnIsGateTargetChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsMakeGate));
        OnPropertyChanged(nameof(ShowsEditGate));
    }
}

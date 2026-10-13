using System;
using Avalonia;
using Avalonia.Controls;
using CAP.Avalonia.ViewModels.Workspaces;

namespace CAP.Avalonia.Behaviors;

/// <summary>
/// Gives a subtree the hue of the active workspace: copies the theme's
/// <c>Brush.Workspace.{Workspace}.{Role}</c> tokens (Styles/Tokens.axaml) onto the host's own
/// resources as <c>Brush.Workspace.Current.{Role}</c>, so the toolbar switch, the accent line,
/// the canvas tint, the status-bar chip and Run resolve one set of keys via
/// <c>{DynamicResource}</c>. Re-applies when the workspace or the theme variant changes.
/// </summary>
/// <example>
/// <code>&lt;Grid behaviors:WorkspaceAccentBehavior.Workspace="{Binding WorkspaceSwitch.Current}"/&gt;</code>
/// </example>
public static class WorkspaceAccentBehavior
{
    /// <summary>The token roles every workspace defines.</summary>
    public static readonly string[] Roles = ["Accent", "AccentHover", "Subtle", "CanvasTint", "CanvasEdge", "OnAccent"];

    /// <summary>The workspace whose hue the host's subtree shows; null leaves the resources untouched.</summary>
    public static readonly AttachedProperty<Workspace?> WorkspaceProperty =
        AvaloniaProperty.RegisterAttached<Control, Workspace?>("Workspace", typeof(WorkspaceAccentBehavior));

    static WorkspaceAccentBehavior()
    {
        WorkspaceProperty.Changed.AddClassHandler<Control>(OnWorkspaceChanged);
    }

    /// <summary>Gets the workspace whose hue the subtree shows.</summary>
    public static Workspace? GetWorkspace(Control control) => control.GetValue(WorkspaceProperty);

    /// <summary>Sets the workspace whose hue the subtree shows.</summary>
    public static void SetWorkspace(Control control, Workspace? value) => control.SetValue(WorkspaceProperty, value);

    /// <summary>Token key of <paramref name="role"/> for <paramref name="workspace"/>.</summary>
    public static string TokenKey(Workspace workspace, string role) => $"Brush.Workspace.{workspace}.{role}";

    /// <summary>Key the host publishes the active workspace's <paramref name="role"/> under.</summary>
    public static string CurrentKey(string role) => $"Brush.Workspace.Current.{role}";

    private static void OnWorkspaceChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        control.ActualThemeVariantChanged -= OnThemeVariantChanged;
        if (e.NewValue is not Workspace)
            return;
        control.ActualThemeVariantChanged += OnThemeVariantChanged;
        Apply(control);
    }

    private static void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        if (sender is Control control)
            Apply(control);
    }

    private static void Apply(Control control)
    {
        if (GetWorkspace(control) is not { } workspace)
            return;
        var theme = control.ActualThemeVariant;
        foreach (var role in Roles)
        {
            object? brush = null;
            if (Application.Current?.TryGetResource(TokenKey(workspace, role), theme, out brush) != true)
                continue;
            control.Resources[CurrentKey(role)] = brush;
        }
    }
}

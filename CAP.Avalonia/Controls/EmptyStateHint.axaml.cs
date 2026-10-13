using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CAP.Avalonia.Controls;

/// <summary>
/// Empty state of an analysis tab: an icon, one plain sentence explaining why the tab has
/// nothing to show, and an optional button running the fix ("Place a light source",
/// "Pick output on canvas"). Every dock tab uses this one control so empty tabs explain
/// themselves the same way.
/// </summary>
public partial class EmptyStateHint : UserControl
{
    /// <summary>The sentence explaining the empty state.</summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EmptyStateHint, string?>(nameof(Message));

    /// <summary>Label of the fix button; the button is hidden when null or empty.</summary>
    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<EmptyStateHint, string?>(nameof(ActionText));

    /// <summary>Command run by the fix button.</summary>
    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<EmptyStateHint, ICommand?>(nameof(ActionCommand));

    /// <summary>Large icon above the sentence.</summary>
    public static readonly StyledProperty<Geometry?> IconDataProperty =
        AvaloniaProperty.Register<EmptyStateHint, Geometry?>(nameof(IconData));

    /// <summary>Small icon inside the fix button; omitted when null.</summary>
    public static readonly StyledProperty<Geometry?> ActionIconDataProperty =
        AvaloniaProperty.Register<EmptyStateHint, Geometry?>(nameof(ActionIconData));

    /// <summary>Initializes the control.</summary>
    public EmptyStateHint()
    {
        InitializeComponent();
    }

    /// <summary>The sentence explaining the empty state.</summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Label of the fix button; hidden when null or empty.</summary>
    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    /// <summary>Command run by the fix button.</summary>
    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>Large icon above the sentence.</summary>
    public Geometry? IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    /// <summary>Small icon inside the fix button.</summary>
    public Geometry? ActionIconData
    {
        get => GetValue(ActionIconDataProperty);
        set => SetValue(ActionIconDataProperty, value);
    }
}

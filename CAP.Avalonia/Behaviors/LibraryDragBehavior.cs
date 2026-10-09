using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CAP.Avalonia.Controls.Canvas.LibraryDrop;
using CAP.Avalonia.ViewModels.Library;

namespace CAP.Avalonia.Behaviors;

/// <summary>
/// Drag-source half of library drag&amp;drop (issue #1157). A press-and-move on a library
/// row beyond a small threshold starts an OS drag operation carrying the row's template,
/// so releasing over the canvas drops it there (<see cref="LibraryDropTarget"/>). A plain
/// click stays below the threshold and keeps the existing click-to-place flow untouched.
/// </summary>
/// <remarks>
/// Usage in XAML:
/// <code>
/// &lt;ListBox behaviors:LibraryDragBehavior.IsEnabled="True" ... /&gt;
/// </code>
/// The ListBox's selected item must be a <see cref="ComponentTemplate"/> or a
/// <see cref="GroupTemplateItemViewModel"/>.
/// </remarks>
public static class LibraryDragBehavior
{
    /// <summary>Distance in pixels the pointer must travel before a press becomes a drag.</summary>
    private const double DragThresholdPixels = 4;

    /// <summary>Attached property that enables library drag&amp;drop on a ListBox.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled",
            typeof(LibraryDragBehavior),
            defaultValue: false);

    private static readonly AttachedProperty<Point?> PressPointProperty =
        AvaloniaProperty.RegisterAttached<Control, Point?>(
            "PressPoint",
            typeof(LibraryDragBehavior));

    static LibraryDragBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    /// <summary>Gets whether library drag&amp;drop is enabled for the element.</summary>
    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    /// <summary>Sets whether library drag&amp;drop is enabled for the element.</summary>
    public static void SetIsEnabled(AvaloniaObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            control.PointerPressed += OnPointerPressed;
            control.PointerMoved += OnPointerMoved;
            control.PointerReleased += OnPointerReleased;
            control.PointerCaptureLost += OnPointerCaptureLost;
        }
        else
        {
            control.PointerPressed -= OnPointerPressed;
            control.PointerMoved -= OnPointerMoved;
            control.PointerReleased -= OnPointerReleased;
            control.PointerCaptureLost -= OnPointerCaptureLost;
        }
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control && e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            control.SetValue(PressPointProperty, e.GetPosition(control));
    }

    private static async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.GetValue(PressPointProperty) is not { } start) return;

        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            control.SetValue(PressPointProperty, null);
            return;
        }

        var current = e.GetPosition(control);
        if (Math.Abs(current.X - start.X) < DragThresholdPixels
            && Math.Abs(current.Y - start.Y) < DragThresholdPixels)
            return;

        control.SetValue(PressPointProperty, null);
        var (format, item) = ResolveDragPayload(control);
        if (format == null || item == null) return;

        var data = new DataObject();
        data.Set(format, item);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        (sender as Control)?.SetValue(PressPointProperty, null);

    private static void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) =>
        (sender as Control)?.SetValue(PressPointProperty, null);

    private static (string? Format, object? Item) ResolveDragPayload(Control control)
    {
        if (control is not ListBox listBox) return (null, null);
        return listBox.SelectedItem switch
        {
            ComponentTemplate template => (LibraryDropTarget.ComponentTemplateFormat, template),
            GroupTemplateItemViewModel group => (LibraryDropTarget.GroupTemplateFormat, group.Template),
            _ => (null, null)
        };
    }
}

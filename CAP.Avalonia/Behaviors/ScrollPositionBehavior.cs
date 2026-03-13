using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace CAP.Avalonia.Behaviors;

/// <summary>
/// Attached behavior for two-way binding of ScrollViewer vertical offset.
/// Enables MVVM-compliant scroll position preservation without code-behind.
/// </summary>
/// <remarks>
/// Usage in XAML:
/// <code>
/// &lt;ScrollViewer behaviors:ScrollPositionBehavior.VerticalOffset="{Binding LibraryScrollOffset, Mode=TwoWay}"&gt;
///     &lt;!-- Content --&gt;
/// &lt;/ScrollViewer&gt;
/// </code>
/// </remarks>
public static class ScrollPositionBehavior
{
    private static bool _isUpdatingFromView = false;

    /// <summary>
    /// Attached property for binding the vertical scroll offset.
    /// </summary>
    public static readonly AttachedProperty<double> VerticalOffsetProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>(
            "VerticalOffset",
            typeof(ScrollPositionBehavior),
            defaultValue: 0.0,
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Gets the vertical offset value from a ScrollViewer.
    /// </summary>
    public static double GetVerticalOffset(AvaloniaObject element)
    {
        return element.GetValue(VerticalOffsetProperty);
    }

    /// <summary>
    /// Sets the vertical offset value on a ScrollViewer.
    /// </summary>
    public static void SetVerticalOffset(AvaloniaObject element, double value)
    {
        element.SetValue(VerticalOffsetProperty, value);
    }

    static ScrollPositionBehavior()
    {
        VerticalOffsetProperty.Changed.AddClassHandler<ScrollViewer>(OnVerticalOffsetChanged);
    }

    /// <summary>
    /// Handles changes to the VerticalOffset attached property.
    /// Sets up two-way synchronization between the property and ScrollViewer.Offset.
    /// </summary>
    private static void OnVerticalOffsetChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not double newOffset)
            return;

        // Update scroll position when property changes from ViewModel
        if (!_isUpdatingFromView)
        {
            var currentOffset = scrollViewer.Offset;
            scrollViewer.Offset = currentOffset.WithY(newOffset);
        }

        // Subscribe to ScrollViewer.Offset changes to update the property
        // Only subscribe once per ScrollViewer
        if (e.OldValue is null or 0.0)
        {
            scrollViewer.PropertyChanged += OnScrollViewerOffsetChanged;
        }
    }

    /// <summary>
    /// Handles ScrollViewer.Offset changes to update the attached property.
    /// </summary>
    private static void OnScrollViewerOffsetChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        if (e.Property != ScrollViewer.OffsetProperty)
            return;

        if (e.NewValue is not Vector newOffset)
            return;

        // Update the attached property when user scrolls
        _isUpdatingFromView = true;
        try
        {
            SetVerticalOffset(scrollViewer, newOffset.Y);
        }
        finally
        {
            _isUpdatingFromView = false;
        }
    }
}

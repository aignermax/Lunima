using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// Interface for pin view models (regular pins and group pins).
/// Provides common properties and methods for pin highlighting and interaction.
/// </summary>
public interface IPinViewModel
{
    /// <summary>
    /// The underlying PhysicalPin that this ViewModel represents.
    /// For GroupPins, this is the InternalPin.
    /// </summary>
    PhysicalPin Pin { get; }

    /// <summary>
    /// The parent component ViewModel.
    /// </summary>
    ComponentViewModel ParentComponentViewModel { get; }

    /// <summary>
    /// Absolute X position of the pin in micrometers.
    /// </summary>
    double X { get; }

    /// <summary>
    /// Absolute Y position of the pin in micrometers.
    /// </summary>
    double Y { get; }

    /// <summary>
    /// Pin angle in degrees (0° = east, 90° = north, etc.).
    /// </summary>
    double Angle { get; }

    /// <summary>
    /// Display name of the pin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether this pin is currently highlighted (mouse is near).
    /// </summary>
    bool IsHighlighted { get; set; }

    /// <summary>
    /// Visual scale factor (1.0 = normal, 1.5 = highlighted).
    /// </summary>
    double Scale { get; set; }

    /// <summary>
    /// Whether this pin already has a connection.
    /// </summary>
    bool HasConnection { get; set; }

    /// <summary>
    /// Sets the highlighted state and updates visual scale.
    /// </summary>
    void SetHighlighted(bool highlighted);

    /// <summary>
    /// Notifies that the pin position has changed (triggers property change events).
    /// </summary>
    void NotifyPositionChanged();
}

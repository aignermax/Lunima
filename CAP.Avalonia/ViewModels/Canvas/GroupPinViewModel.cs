using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// ViewModel for a ComponentGroup external pin (GroupPin).
/// Provides correct position calculation for pins on group boundaries.
/// </summary>
public partial class GroupPinViewModel : ObservableObject, IPinViewModel
{
    public GroupPin GroupPin { get; }
    public ComponentGroup ParentGroup { get; }
    public ComponentViewModel ParentComponentViewModel { get; }

    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>
    /// Scale factor for visual size (1.0 = normal, 1.5 = highlighted).
    /// </summary>
    [ObservableProperty]
    private double _scale = 1.0;

    /// <summary>
    /// The underlying PhysicalPin (InternalPin) that this GroupPin exposes.
    /// Used for connection creation.
    /// </summary>
    public PhysicalPin Pin => GroupPin.InternalPin;

    /// <summary>
    /// Absolute X position of the GroupPin on the group boundary.
    /// </summary>
    public double X
    {
        get
        {
            var (x, _) = GroupPinOccupancyChecker.GetAbsolutePosition(GroupPin, ParentGroup);
            return x;
        }
    }

    /// <summary>
    /// Absolute Y position of the GroupPin on the group boundary.
    /// </summary>
    public double Y
    {
        get
        {
            var (_, y) = GroupPinOccupancyChecker.GetAbsolutePosition(GroupPin, ParentGroup);
            return y;
        }
    }

    /// <summary>
    /// Angle of the GroupPin in degrees (0° = east, 90° = north, etc.).
    /// </summary>
    public double Angle => GroupPinOccupancyChecker.GetAbsoluteAngle(GroupPin);

    /// <summary>
    /// Display name of the GroupPin.
    /// </summary>
    public string Name => GroupPin.Name;

    /// <summary>
    /// Whether this pin already has a connection.
    /// </summary>
    public bool HasConnection { get; set; }

    public GroupPinViewModel(GroupPin groupPin, ComponentGroup parentGroup, ComponentViewModel parentVm)
    {
        GroupPin = groupPin;
        ParentGroup = parentGroup;
        ParentComponentViewModel = parentVm;
    }

    public void SetHighlighted(bool highlighted)
    {
        IsHighlighted = highlighted;
        Scale = highlighted ? 1.5 : 1.0;
    }

    public void NotifyPositionChanged()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Angle));
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Canvas;

/// <summary>
/// ViewModel for a physical pin with highlighting support.
/// </summary>
public partial class PinViewModel : ObservableObject
{
    public PhysicalPin Pin { get; }
    public ComponentViewModel ParentComponentViewModel { get; }

    [ObservableProperty] private bool _isHighlighted;
    [ObservableProperty] private double _scale = 1.0;

    public double X => Pin.GetAbsolutePosition().x;
    public double Y => Pin.GetAbsolutePosition().y;
    public double Angle => Pin.GetAbsoluteAngle();
    public string Name => Pin.Name;

    /// <summary>
    /// Whether this pin already has a connection.
    /// </summary>
    public bool HasConnection { get; set; }

    public PinViewModel(PhysicalPin pin, ComponentViewModel parentVm)
    {
        Pin = pin;
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

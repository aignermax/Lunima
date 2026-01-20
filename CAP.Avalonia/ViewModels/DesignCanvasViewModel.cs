using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CAP_Core.Components;

namespace CAP.Avalonia.ViewModels;

public partial class DesignCanvasViewModel : ObservableObject
{
    public ObservableCollection<ComponentViewModel> Components { get; } = new();
    public ObservableCollection<WaveguideConnectionViewModel> Connections { get; } = new();

    public WaveguideConnectionManager ConnectionManager { get; } = new();

    [ObservableProperty]
    private ComponentViewModel? _selectedComponent;

    [ObservableProperty]
    private double _panX;

    [ObservableProperty]
    private double _panY;

    public void MoveComponent(ComponentViewModel component, double deltaX, double deltaY)
    {
        component.X += deltaX;
        component.Y += deltaY;

        // Update the underlying component
        component.Component.PhysicalX = component.X;
        component.Component.PhysicalY = component.Y;

        // Recalculate waveguide transmissions for affected connections
        ConnectionManager.RecalculateTransmissionsForComponent(component.Component);

        // Notify connections to update their paths
        foreach (var conn in Connections)
        {
            if (conn.Connection.StartPin.ParentComponent == component.Component ||
                conn.Connection.EndPin.ParentComponent == component.Component)
            {
                conn.NotifyPathChanged();
            }
        }
    }

    public void AddComponent(Component component)
    {
        Components.Add(new ComponentViewModel(component));
    }

    public void RemoveComponent(ComponentViewModel component)
    {
        ConnectionManager.RemoveConnectionsForComponent(component.Component);

        // Remove connection view models
        var connectionsToRemove = Connections
            .Where(c => c.Connection.StartPin.ParentComponent == component.Component ||
                        c.Connection.EndPin.ParentComponent == component.Component)
            .ToList();

        foreach (var conn in connectionsToRemove)
        {
            Connections.Remove(conn);
        }

        Components.Remove(component);
    }

    public WaveguideConnectionViewModel? ConnectPins(PhysicalPin startPin, PhysicalPin endPin)
    {
        var connection = ConnectionManager.AddConnection(startPin, endPin);
        var vm = new WaveguideConnectionViewModel(connection);
        Connections.Add(vm);
        return vm;
    }
}

public partial class ComponentViewModel : ObservableObject
{
    public Component Component { get; }

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private bool _isSelected;

    public double Width => Component.WidthMicrometers;
    public double Height => Component.HeightMicrometers;
    public string Name => Component.Identifier;

    public ComponentViewModel(Component component)
    {
        Component = component;
        _x = component.PhysicalX;
        _y = component.PhysicalY;
    }
}

public partial class WaveguideConnectionViewModel : ObservableObject
{
    public WaveguideConnection Connection { get; }

    [ObservableProperty]
    private bool _isSelected;

    public double StartX => Connection.StartPin.GetAbsolutePosition().x;
    public double StartY => Connection.StartPin.GetAbsolutePosition().y;
    public double EndX => Connection.EndPin.GetAbsolutePosition().x;
    public double EndY => Connection.EndPin.GetAbsolutePosition().y;

    public double PathLength => Connection.PathLengthMicrometers;
    public double LossDb => Connection.TotalLossDb;

    public WaveguideConnectionViewModel(WaveguideConnection connection)
    {
        Connection = connection;
    }

    public void NotifyPathChanged()
    {
        OnPropertyChanged(nameof(StartX));
        OnPropertyChanged(nameof(StartY));
        OnPropertyChanged(nameof(EndX));
        OnPropertyChanged(nameof(EndY));
        OnPropertyChanged(nameof(PathLength));
        OnPropertyChanged(nameof(LossDb));
    }
}

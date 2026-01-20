using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CAP_Core.Components;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Grid;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;

namespace CAP.Avalonia.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private DesignCanvasViewModel _canvas;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    public MainViewModel()
    {
        _canvas = new DesignCanvasViewModel();

        // Add some test components
        AddTestComponents();
    }

    private void AddTestComponents()
    {
        // Create test components with physical coordinates
        var comp1 = CreateTestComponent("Splitter", 100, 100, 250, 250);
        var comp2 = CreateTestComponent("Coupler", 500, 100, 250, 250);
        var comp3 = CreateTestComponent("Detector", 500, 400, 250, 250);

        Canvas.Components.Add(new ComponentViewModel(comp1));
        Canvas.Components.Add(new ComponentViewModel(comp2));
        Canvas.Components.Add(new ComponentViewModel(comp3));

        // Create test connections
        if (comp1.PhysicalPins.Count > 0 && comp2.PhysicalPins.Count > 0)
        {
            var connection = Canvas.ConnectionManager.AddConnection(
                comp1.PhysicalPins[0],
                comp2.PhysicalPins[0]);
            Canvas.Connections.Add(new WaveguideConnectionViewModel(connection));
        }

        StatusText = $"Loaded {Canvas.Components.Count} components, {Canvas.Connections.Count} connections";
    }

    private Component CreateTestComponent(string name, double x, double y, double width, double height)
    {
        // Create a minimal component for testing
        var parts = new Part[1, 1];
        var pins = new List<Pin>
        {
            new Pin($"{name}_in", 0, MatterType.Light, RectSide.Left),
            new Pin($"{name}_out", 1, MatterType.Light, RectSide.Right)
        };
        parts[0, 0] = new Part(pins);

        var sMatrix = new CAP_Core.LightCalculation.SMatrix(
            pins.SelectMany(p => new[] { p.IDInFlow, p.IDOutFlow }).ToList(),
            new List<(Guid, double)>());

        var wavelengthMap = new Dictionary<int, CAP_Core.LightCalculation.SMatrix>
        {
            { 1550, sMatrix }
        };

        // Create physical pins linked to logical pins
        var physicalPins = new List<PhysicalPin>
        {
            new PhysicalPin
            {
                Name = $"{name}_pin_in",
                OffsetXMicrometers = 0,
                OffsetYMicrometers = height / 2,
                AngleDegrees = 180,
                LogicalPin = pins[0]
            },
            new PhysicalPin
            {
                Name = $"{name}_pin_out",
                OffsetXMicrometers = width,
                OffsetYMicrometers = height / 2,
                AngleDegrees = 0,
                LogicalPin = pins[1]
            }
        };

        var component = new Component(
            wavelengthMap,
            new List<Slider>(),
            $"nazca_{name.ToLower()}",
            "",
            parts,
            0,
            name,
            DiscreteRotation.R0,
            physicalPins);

        component.PhysicalX = x;
        component.PhysicalY = y;
        component.WidthMicrometers = width;
        component.HeightMicrometers = height;

        return component;
    }

    [RelayCommand]
    private void ZoomIn()
    {
        ZoomLevel = Math.Min(ZoomLevel * 1.2, 10.0);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        ZoomLevel = Math.Max(ZoomLevel / 1.2, 0.1);
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomLevel = 1.0;
    }
}

using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Author;

/// <summary>
/// The full structured description of one top-level component for agents: pose and bounds,
/// every connectable pin with its absolute position, facing and what it is wired to, the
/// tunable parameters and the light-source settings. Positions are µm, angles degrees.
/// </summary>
public static class ComponentDescriber
{
    private const int CoordinateDecimals = 3;

    /// <summary>Describes <paramref name="vm"/> on <paramref name="session"/>'s canvas.</summary>
    public static object Describe(AgentDesignSession session, ComponentViewModel vm)
    {
        var component = vm.Component;
        var group = component as ComponentGroup;
        return new
        {
            Id = component.Identifier,
            Name = group?.GroupName ?? vm.Name,
            Type = group != null ? (group.IsDie ? "die" : "group") : component.TemplateName ?? component.NazcaFunctionName,
            Pdk = session.Placement.ResolveComponentPdkSource(component),
            X = Round(vm.X),
            Y = Round(vm.Y),
            Width = Round(component.WidthMicrometers),
            Height = Round(component.HeightMicrometers),
            RotationDeg = component.RotationDegrees,
            Locked = component.IsLocked,
            ChildCount = group?.ChildComponents.Count,
            Pins = DescribePins(session.Canvas, component),
            Parameters = DescribeParameters(component),
            LightSource = DescribeLightSource(vm),
        };
    }

    /// <summary>Every connectable pin with position, facing, domain and its wire (null when free).</summary>
    public static IReadOnlyList<object> DescribePins(DesignCanvasViewModel canvas, Component component)
    {
        var wires = canvas.Connections.Select(c => c.Connection).ToList();
        return ComponentPins.Of(component).Select(pin =>
        {
            var (x, y) = pin.Pin.GetAbsolutePosition();
            var inner = component is ComponentGroup ? pin.Pin.ParentComponent : null;
            return (object)new
            {
                pin.Name,
                X = Round(x),
                Y = Round(y),
                AngleDeg = Math.Round(pin.Pin.GetAbsoluteAngle(), 1),
                Domain = pin.Pin.MatterType.ToString().ToLowerInvariant(),
                ConnectedTo = DescribeWire(wires, pin.Pin),
                Inner = inner == null ? null : $"{(inner is ComponentGroup g ? g.GroupName : inner.Identifier)}.{pin.Pin.Name}",
                Signal = inner is ComponentGroup { TruthTablePinAssignment: { } roles } ? SignalOf(roles, pin.Pin.Name) : null,
            };
        }).ToList();
    }

    /// <summary>The logic signal a gate pin carries (its signal name, or the pin name for a gate input/output).</summary>
    private static string? SignalOf(TruthTablePinAssignment roles, string pin) =>
        roles.InputSignalNames?.GetValueOrDefault(pin) ?? roles.OutputSignalNames?.GetValueOrDefault(pin);

    /// <summary>The far end of the wire on <paramref name="pin"/> as <c>component.pin</c>, or null.</summary>
    private static object? DescribeWire(IReadOnlyList<WaveguideConnection> wires, PhysicalPin pin)
    {
        var wire = wires.FirstOrDefault(w => w.StartPin == pin || w.EndPin == pin);
        if (wire == null)
            return null;
        var other = wire.StartPin == pin ? wire.EndPin : wire.StartPin;
        var top = TopLevelName(other.ParentComponent);
        return new
        {
            ConnectionId = wire.Id,
            Pin = $"{top}.{other.Name}",
            Inner = top == other.ParentComponent.Identifier ? null : $"{other.ParentComponent.Identifier}.{other.Name}",
            Blocked = wire.IsBlockedFallback,
        };
    }

    private static string TopLevelName(Component component)
    {
        var current = component;
        while (current.ParentGroup is Component parent)
            current = parent;
        return current.Identifier;
    }

    /// <summary>Tunable parameters: named parametric ones first, then any further anonymous sliders.</summary>
    public static IReadOnlyList<object> DescribeParameters(Component component)
    {
        var named = component.ParameterDefinitions
            .Where(d => d.SliderNumber.HasValue && component.GetSlider(d.SliderNumber.Value) != null)
            .ToList();
        var namedNumbers = named.Select(d => d.SliderNumber!.Value).ToHashSet();
        var result = named.Select(d =>
        {
            var slider = component.GetSlider(d.SliderNumber!.Value)!;
            return (object)new { Slider = slider.Number, d.Name, d.Label, d.Unit, slider.Value, Min = slider.MinValue, Max = slider.MaxValue };
        }).ToList();
        result.AddRange(component.GetAllSliders()
            .Where(s => !namedNumbers.Contains(s.Number))
            .Select(s => (object)new { Slider = s.Number, Name = (string?)null, Label = (string?)null, Unit = (string?)null, s.Value, Min = s.MinValue, Max = s.MaxValue }));
        return result;
    }

    private static object? DescribeLightSource(ComponentViewModel vm) =>
        vm.LaserConfig is { } laser
            ? new { Enabled = laser.IsEnabled, laser.WavelengthNm, Power = laser.InputPower }
            : null;

    private static double Round(double value) => Math.Round(value, CoordinateDecimals);
}

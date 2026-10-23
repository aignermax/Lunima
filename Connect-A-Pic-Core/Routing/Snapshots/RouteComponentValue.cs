using System.Collections.Immutable;
using CAP_Core.Components.Core;
using CAP_Core.Components.Dies;
using CAP_Core.LightCalculation.BendLoss;
using CAP_Core.Tiles;

namespace CAP_Core.Routing.Snapshots;

internal sealed record RouteComponentValue
{
    internal Guid Id { get; init; }
    internal string Name { get; init; } = "";
    internal string NazcaFunction { get; init; } = "";
    internal string? Template { get; init; }
    internal double X { get; init; }
    internal double Y { get; init; }
    internal double Width { get; init; }
    internal double Height { get; init; }
    internal double Rotation { get; init; }
    internal double OriginalWidth { get; init; }
    internal double OriginalHeight { get; init; }
    internal bool Mirrored { get; init; }
    internal bool Obstacle { get; init; }
    internal bool InsertedCrossing { get; init; }
    internal double? ModeDiameter { get; init; }
    internal BendLossModel? BendLoss { get; init; }
    internal RoutingDispersionValue? Dispersion { get; init; }
    internal ImmutableArray<PolygonValue> Outline { get; init; }
    internal ImmutableArray<RoutePinValue> Pins { get; init; }
    internal bool Group { get; init; }
    internal DieDefinition? Die { get; init; }
    internal ImmutableArray<Guid> Children { get; init; }
    internal ImmutableArray<FrozenRouteValue> FrozenPaths { get; init; }
    internal ImmutableArray<GroupPinValue> ExternalPins { get; init; }

    internal static RouteComponentValue Capture(Component component)
    {
        var group = component as ComponentGroup;
        return new()
        {
            Id = component.Id, Name = component.Name, NazcaFunction = component.NazcaFunctionName,
            Template = component.TemplateName, X = component.PhysicalX, Y = component.PhysicalY,
            Width = component.WidthMicrometers, Height = component.HeightMicrometers,
            Rotation = component.RotationDegrees, Obstacle = component.IsRoutingObstacle,
            OriginalWidth = component.UnrotatedWidthMicrometers, OriginalHeight = component.UnrotatedHeightMicrometers,
            Mirrored = component.IsMirroredHorizontally,
            InsertedCrossing = component.IsInsertedCrossing, ModeDiameter = component.ModeFieldDiameterMicrometers,
            BendLoss = component.WaveguideBendLoss, Dispersion = RoutingDispersionValue.Capture(component.WaveguideDispersion),
            Outline = PolygonValue.Capture(component.OutlinePolygons),
            Pins = component.PhysicalPins.Select(RoutePinValue.Capture).ToImmutableArray(), Group = group != null,
            Die = group?.Die is { } die ? die with { EnabledPdkNames = die.EnabledPdkNames?.ToImmutableArray() } : null,
            Children = group?.ChildComponents.Select(child => child.Id).ToImmutableArray() ?? ImmutableArray<Guid>.Empty,
            FrozenPaths = group?.InternalPaths.Select(FrozenRouteValue.Capture).ToImmutableArray() ?? ImmutableArray<FrozenRouteValue>.Empty,
            ExternalPins = group?.ExternalPins.Select(pin => new GroupPinValue(pin.PinId, pin.Name,
                pin.InternalPin.PinId, pin.RelativeX, pin.RelativeY, pin.AngleDegrees)).ToImmutableArray()
                ?? ImmutableArray<GroupPinValue>.Empty
        };
    }

    internal Component BuildShell(Dictionary<Guid, PhysicalPin> pins, Dictionary<Guid, Pin> logicalPins)
    {
        Component component = Group ? new ComponentGroup(Name)
            : new Component(new(), [], NazcaFunction, "", new Part[0, 0], 0, Name, DiscreteRotation.R0, []);
        component.RestoreSnapshotIdentity(Id);
        foreach (var value in Pins)
        {
            var pin = value.Build(component, logicalPins);
            if (!pins.TryAdd(pin.PinId, pin)) throw new InvalidDataException($"Duplicate routing pin identity '{pin.PinId}'.");
            component.PhysicalPins.Add(pin);
        }
        ApplyGeometry(component);
        return component;
    }

    internal void AttachHierarchy(Component component, IReadOnlyDictionary<Guid, Component> components,
        IReadOnlyDictionary<Guid, PhysicalPin> pins)
    {
        if (component is not ComponentGroup group) return;
        // AddChild updates bounds. Restore the captured origin after all topology is attached.
        foreach (var id in Children) group.AddChild(components[id]);
        foreach (var path in FrozenPaths) group.AddInternalPath(path.Build(pins));
        foreach (var pin in ExternalPins) group.AddExternalPin(new GroupPin
        {
            PinId = pin.Id, Name = pin.Name, InternalPin = pins[pin.Internal], RelativeX = pin.X,
            RelativeY = pin.Y, AngleDegrees = pin.Angle
        });
        ApplyGeometry(component);
    }

    internal void ApplyGeometry(Component component)
    {
        if (component is ComponentGroup group) group.Die = Die is { } die
            ? die with { EnabledPdkNames = die.EnabledPdkNames?.ToArray() } : null;
        component.Name = Name;
        component.NazcaFunctionName = NazcaFunction;
        component.TemplateName = Template;
        component.PhysicalX = X;
        component.PhysicalY = Y;
        component.WidthMicrometers = Width;
        component.HeightMicrometers = Height;
        component.RotationDegrees = Rotation;
        component.UnrotatedWidthMicrometers = OriginalWidth;
        component.UnrotatedHeightMicrometers = OriginalHeight;
        component.IsMirroredHorizontally = Mirrored;
        component.IsRoutingObstacle = Obstacle;
        component.IsInsertedCrossing = InsertedCrossing;
        component.ModeFieldDiameterMicrometers = ModeDiameter;
        component.WaveguideBendLoss = BendLoss;
        component.WaveguideDispersion = Dispersion?.Build();
        component.OutlinePolygons = Outline.Select(polygon => polygon.Build()).ToArray();
    }
}

internal readonly record struct GroupPinValue(Guid Id, string Name, Guid Internal, double X, double Y, double Angle);
internal readonly record struct LogicalPinValue(string Name, int Number, MatterType Kind, RectSide Side,
    Guid In, Guid Out, PolarizationKind Polarization);

internal sealed record RoutePinValue(Guid Id, string Name, string? PortType, double X, double Y,
    double Angle, double? Width, int? Layer, LogicalPinValue? Logical)
{
    internal static RoutePinValue Capture(PhysicalPin pin) => new(pin.PinId, pin.Name, pin.PortType,
        pin.OffsetXMicrometers, pin.OffsetYMicrometers, pin.AngleDegrees, pin.WaveguideWidthMicrometers, pin.Layer,
        pin.LogicalPin is { } logical ? new LogicalPinValue(logical.Name, logical.PinNumber, logical.MatterType,
            logical.Side, logical.IDInFlow, logical.IDOutFlow, logical.Polarization) : null);

    internal PhysicalPin Build(Component parent, Dictionary<Guid, Pin> logicalPins)
    {
        Pin? logical = null;
        if (Logical is { } value && !logicalPins.TryGetValue(value.In, out logical))
        {
            logical = new Pin(value.Name, value.Number, value.Kind, value.Side, value.In, value.Out)
                { Polarization = value.Polarization };
            logicalPins.Add(value.In, logical);
        }
        return new PhysicalPin { PinId = Id, Name = Name, PortType = PortType, OffsetXMicrometers = X,
            OffsetYMicrometers = Y, AngleDegrees = Angle, WaveguideWidthMicrometers = Width, Layer = Layer,
            ParentComponent = parent, LogicalPin = logical! };
    }
}

using System.Text.Json.Nodes;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Routing;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// The routed word cell of the hierarchical-RAM spike (issue #1366) as an instancing
/// template: the gate groups exactly as emitted by <see cref="RamWordCellBuilder"/>, the
/// intra-cell wires captured once from the routed canvas as frozen group-internal paths,
/// and the cell ports (SEL, LOAD, D0–D3 in, M0–M3 out) as group external pins.
/// <see cref="EmitInstance"/> stamps out one word instance: every identifier re-rolled,
/// gate names word-swapped (<see cref="RamWordCellBuilder.InstanceGateName"/>), the gate
/// bodies and all frozen geometry — arc centres included — translated to the instance
/// anchor. The result is plain .lun group JSON: the cell as a top-level group entry, its
/// gates as nested entries bound by <c>ParentGroupIdGuid</c> (#1060/#1065).
/// </summary>
public sealed partial class RamWordCellTemplate
{
    private readonly IReadOnlyList<(string Role, JsonObject Entry)> _gates;
    private readonly IReadOnlyList<TemplatePath> _paths;
    private readonly IReadOnlyDictionary<string, TemplatePort> _ports;

    private RamWordCellTemplate(
        IReadOnlyList<(string Role, JsonObject Entry)> gates,
        IReadOnlyList<TemplatePath> paths,
        IReadOnlyDictionary<string, TemplatePort> ports,
        double originX, double originY, double width, double height)
    {
        _gates = gates;
        _paths = paths;
        _ports = ports;
        OriginX = originX;
        OriginY = originY;
        Width = width;
        Height = height;
    }

    /// <summary>Leftmost gate X of the cell in template coordinates.</summary>
    public double OriginX { get; }

    /// <summary>Topmost gate Y of the cell in template coordinates.</summary>
    public double OriginY { get; }

    /// <summary>Cell body width in micrometers (gate span plus the rightmost pin offset).</summary>
    public double Width { get; }

    /// <summary>Cell body height in micrometers (row span plus one row pitch).</summary>
    public double Height { get; }

    /// <summary>
    /// Freezes the routed cell on the canvas into a template: gate JSON from the emitted
    /// document, wire geometry from the live routed connections, port positions from the
    /// gates' external pins relative to the cell origin.
    /// </summary>
    public static RamWordCellTemplate Extract(DesignCanvasViewModel canvas, RamWordCellDesign cell)
    {
        var document = JsonNode.Parse(cell.Json)!.AsObject();
        var nameToRole = cell.GateRoles.ToDictionary(kv => kv.Value, kv => kv.Key);
        var gates = new List<(string Role, JsonObject Entry)>();
        foreach (var entry in document["Groups"]!.AsArray())
        {
            var name = entry!["GroupDto"]!["GroupName"]!.GetValue<string>();
            gates.Add((nameToRole[name], (JsonObject)entry.DeepClone()));
        }

        var canvasGates = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>().ToList();
        var paths = canvas.Connections.Select(c => ToTemplatePath(c.Connection, canvasGates, nameToRole)).ToList();

        double originX = gates.Min(g => g.Entry["GroupDto"]!["PhysicalX"]!.GetValue<double>());
        double originY = gates.Min(g => g.Entry["GroupDto"]!["PhysicalY"]!.GetValue<double>());
        double width = gates.Max(g => g.Entry["GroupDto"]!["PhysicalX"]!.GetValue<double>()
            + MaxPinOffset(g.Entry, "RelativeX")) - originX;
        double height = gates.Max(g => g.Entry["GroupDto"]!["PhysicalY"]!.GetValue<double>()) - originY
            + RamGateFactory.RowPitch;
        var ports = cell.Ports.ToDictionary(kv => kv.Key, kv => ToTemplatePort(kv.Value, gates, originX, originY));
        return new RamWordCellTemplate(gates, paths, ports, originX, originY, width, height);
    }

    private static JsonObject FindExternalPin(JsonObject gateEntry, string pinName) =>
        gateEntry["GroupDto"]!["ExternalPins"]!.AsArray()
            .Select(p => p!.AsObject())
            .First(p => p["Name"]!.GetValue<string>() == pinName);

    private static TemplatePath ToTemplatePath(
        CAP_Core.Components.Connections.WaveguideConnection connection,
        IReadOnlyList<ComponentGroup> canvasGates,
        IReadOnlyDictionary<string, string> nameToRole)
    {
        var (startRole, startPin) = EndpointRole(connection.StartPin, canvasGates, nameToRole);
        var (endRole, endPin) = EndpointRole(connection.EndPin, canvasGates, nameToRole);
        return new TemplatePath(startRole, startPin, endRole, endPin, SegmentsToJson(connection.RoutedPath!),
            connection.RoutedPath!.IsBlockedFallback);
    }

    /// <summary>Maps a live connection endpoint pin back to its gate role and external pin name.</summary>
    private static (string Role, string Pin) EndpointRole(
        CAP_Core.Components.Core.PhysicalPin pin,
        IReadOnlyList<ComponentGroup> canvasGates,
        IReadOnlyDictionary<string, string> nameToRole)
    {
        foreach (var gate in canvasGates)
        {
            var extPin = gate.ExternalPins.FirstOrDefault(p => ReferenceEquals(p.InternalPin, pin));
            if (extPin != null)
                return (nameToRole[gate.GroupName], extPin.Name);
        }
        throw new InvalidOperationException($"connection endpoint pin '{pin.Name}' belongs to no word-cell gate");
    }

    /// <summary>Converts the routed path into frozen-path segment JSON ("arc"/"straight").</summary>
    private static JsonArray SegmentsToJson(RoutedPath path)
    {
        var segments = new JsonArray();
        foreach (var segment in path.Segments)
        {
            var segmentObject = new JsonObject
            {
                ["StartX"] = segment.StartPoint.X,
                ["StartY"] = segment.StartPoint.Y,
                ["EndX"] = segment.EndPoint.X,
                ["EndY"] = segment.EndPoint.Y,
                ["StartAngleDegrees"] = segment.StartAngleDegrees,
                ["EndAngleDegrees"] = segment.EndAngleDegrees,
            };
            if (segment is BendSegment bend)
            {
                segmentObject["Type"] = "arc";
                segmentObject["CenterX"] = bend.Center.X;
                segmentObject["CenterY"] = bend.Center.Y;
                segmentObject["RadiusMicrometers"] = bend.RadiusMicrometers;
                segmentObject["SweepAngleDegrees"] = bend.SweepAngleDegrees;
            }
            else
            {
                segmentObject["Type"] = "straight";
            }
            segments.Add(segmentObject);
        }
        return segments;
    }

    private static TemplatePort ToTemplatePort(
        (string Role, string Pin) port,
        IReadOnlyList<(string Role, JsonObject Entry)> gates,
        double originX, double originY)
    {
        var gate = gates.First(g => g.Role == port.Role).Entry;
        var extPin = FindExternalPin(gate, port.Pin);
        double relX = gate["GroupDto"]!["PhysicalX"]!.GetValue<double>() + extPin["RelativeX"]!.GetValue<double>() - originX;
        double relY = gate["GroupDto"]!["PhysicalY"]!.GetValue<double>() + extPin["RelativeY"]!.GetValue<double>() - originY;
        return new TemplatePort(port.Role, port.Pin, relX, relY, extPin["AngleDegrees"]!.GetValue<double>());
    }

    private static double MaxPinOffset(JsonObject gateEntry, string property) =>
        gateEntry["GroupDto"]!["ExternalPins"]!.AsArray()
            .Max(p => p![property]!.GetValue<double>());

    private sealed record TemplatePath(string StartRole, string StartPin, string EndRole, string EndPin, JsonArray Segments, bool IsBlockedFallback);

    private sealed record TemplatePort(string Role, string Pin, double RelativeX, double RelativeY, double AngleDegrees);
}

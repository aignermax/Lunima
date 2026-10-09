using System.Text.Json.Nodes;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// The instancing half of <see cref="RamWordCellTemplate"/> (issue #1366):
/// <see cref="EmitInstance"/> stamps out one word instance of the frozen cell — every
/// identifier re-rolled, gate names word-swapped
/// (<see cref="RamWordCellBuilder.InstanceGateName"/>), the gate bodies and all frozen
/// geometry — arc centres included — translated to the instance anchor. The result is
/// plain .lun group JSON: the cell as a top-level group entry, its gates as nested
/// entries bound by <c>ParentGroupIdGuid</c> (#1060/#1065).
/// </summary>
public sealed partial class RamWordCellTemplate
{
    /// <summary>Stamps out the word-<paramref name="word"/> instance of the cell at the anchor.</summary>
    public RamWordCellInstance EmitInstance(int word, double anchorX, double anchorY)
    {
        double dx = anchorX - OriginX;
        double dy = anchorY - OriginY;
        string cellIdentifier = $"group_{Guid.NewGuid():N}";
        string cellGuid = Guid.NewGuid().ToString();
        var gates = new Dictionary<string, JsonObject>();
        foreach (var (role, gateEntry) in _gates)
        {
            var gateName = gateEntry["GroupDto"]!["GroupName"]!.GetValue<string>();
            gates[role] = ReRollGate(gateEntry, RamWordCellBuilder.InstanceGateName(gateName, word), dx, dy, cellIdentifier, cellGuid);
        }

        var crossings = _crossings.ToDictionary(kv => kv.Key, kv => ReRollCrossing(kv.Value, dx, dy));
        var childIds = gates.Values.Select(g => g["GroupDto"]!["Identifier"]!.GetValue<string>())
            .Concat(crossings.Values.Select(c => c["Identifier"]!.GetValue<string>()));
        var childGuids = gates.Values.Select(g => g["GroupDto"]!["IdGuid"]!.GetValue<string>())
            .Concat(crossings.Values.Select(c => c["ComponentGuid"]!.GetValue<string>()));
        var cellDto = new JsonObject
        {
            ["GroupName"] = $"CELL{word}",
            ["Description"] = $"word {word} of the hierarchical RAM: the word cell, routed once and frozen",
            ["Identifier"] = cellIdentifier,
            ["IdGuid"] = cellGuid,
            ["GridX"] = 0,
            ["GridY"] = 0,
            ["PhysicalX"] = anchorX,
            ["PhysicalY"] = anchorY,
            ["Rotation90CounterClock"] = 0,
            ["ChildComponentIds"] = new JsonArray(childIds.Select(id => (JsonNode)id).ToArray()),
            ["ChildComponentGuids"] = new JsonArray(childGuids.Select(guid => (JsonNode)guid).ToArray()),
            ["InternalPaths"] = new JsonArray(_paths.Select(p => (JsonNode)EmitPath(p, gates, crossings, dx, dy)).ToArray()),
            ["ExternalPins"] = new JsonArray(_ports.Select(kv => (JsonNode)EmitPort(kv.Key, kv.Value, gates)).ToArray()),
        };
        var entry = new JsonObject
        {
            ["GroupDto"] = cellDto,
            ["ChildComponents"] = new JsonArray(crossings.Values.Select(c => (JsonNode)c).ToArray()),
            ["CanvasX"] = anchorX,
            ["CanvasY"] = anchorY,
        };
        return new RamWordCellInstance
        {
            CellGroupEntry = entry,
            GateEntriesByRole = gates,
            Identifier = cellIdentifier,
            Right = anchorX + Width,
            Bottom = anchorY + Height,
        };
    }

    /// <summary>Deep-clones one gate entry with every identifier re-rolled and all geometry translated.</summary>
    private static JsonObject ReRollGate(JsonObject templateEntry, string name, double dx, double dy, string parentId, string parentGuid)
    {
        var entry = templateEntry.DeepClone().AsObject();
        var dto = entry["GroupDto"]!.AsObject();
        var guidMap = dto["ChildComponentGuids"]!.AsArray()
            .ToDictionary(g => g!.GetValue<string>(), _ => Guid.NewGuid().ToString());
        var idMap = dto["ChildComponentIds"]!.AsArray()
            .ToDictionary(id => id!.GetValue<string>(), id => $"{id!.GetValue<string>().Split('_')[0]}_{Guid.NewGuid():N}");

        dto["Identifier"] = $"group_{Guid.NewGuid():N}";
        dto["GroupName"] = name;
        dto["IdGuid"] = Guid.NewGuid().ToString();
        dto["ParentGroupId"] = parentId;
        dto["ParentGroupIdGuid"] = parentGuid;
        dto["ChildComponentGuids"] = new JsonArray(dto["ChildComponentGuids"]!.AsArray()
            .Select(g => (JsonNode)guidMap[g!.GetValue<string>()]).ToArray());
        dto["ChildComponentIds"] = new JsonArray(dto["ChildComponentIds"]!.AsArray()
            .Select(id => (JsonNode)idMap[id!.GetValue<string>()]).ToArray());

        foreach (var path in dto["InternalPaths"]!.AsArray())
        {
            var pathObject = path!.AsObject();
            pathObject["PathId"] = Guid.NewGuid().ToString();
            pathObject["StartComponentId"] = idMap[pathObject["StartComponentId"]!.GetValue<string>()];
            pathObject["EndComponentId"] = idMap[pathObject["EndComponentId"]!.GetValue<string>()];
            RamGateFactory.Remap(pathObject, guidMap, "StartComponentGuid");
            RamGateFactory.Remap(pathObject, guidMap, "EndComponentGuid");
            foreach (var segment in pathObject["Segments"]!.AsArray())
                RamGateFactory.TranslateSegment(segment!.AsObject(), dx, dy);
        }
        foreach (var pin in dto["ExternalPins"]!.AsArray())
        {
            var pinObject = pin!.AsObject();
            pinObject["PinId"] = Guid.NewGuid().ToString();
            pinObject["InternalComponentId"] = idMap[pinObject["InternalComponentId"]!.GetValue<string>()];
            RamGateFactory.Remap(pinObject, guidMap, "InternalComponentGuid");
        }
        foreach (var child in entry["ChildComponents"]!.AsArray())
        {
            var childObject = child!.AsObject();
            childObject["Identifier"] = idMap[childObject["Identifier"]!.GetValue<string>()];
            childObject["ComponentGuid"] = guidMap[childObject["ComponentGuid"]!.GetValue<string>()];
            childObject["X"] = childObject["X"]!.GetValue<double>() + dx;
            childObject["Y"] = childObject["Y"]!.GetValue<double>() + dy;
        }

        dto["PhysicalX"] = dto["PhysicalX"]!.GetValue<double>() + dx;
        dto["PhysicalY"] = dto["PhysicalY"]!.GetValue<double>() + dy;
        entry["CanvasX"] = entry["CanvasX"]!.GetValue<double>() + dx;
        entry["CanvasY"] = entry["CanvasY"]!.GetValue<double>() + dy;
        return entry;
    }

    /// <summary>Clones one cell crossing with a fresh identity, translated to the instance.</summary>
    private static JsonObject ReRollCrossing(JsonObject template, double dx, double dy)
    {
        var crossing = template.DeepClone().AsObject();
        crossing["Identifier"] = $"{template["Identifier"]!.GetValue<string>().Split('_')[0]}_{Guid.NewGuid():N}";
        crossing["ComponentGuid"] = Guid.NewGuid().ToString();
        crossing["X"] = template["X"]!.GetValue<double>() + dx;
        crossing["Y"] = template["Y"]!.GetValue<double>() + dy;
        return crossing;
    }

    /// <summary>Emits one frozen intra-cell path with translated segments and instance leaf endpoints.</summary>
    private static JsonObject EmitPath(TemplatePath path, IReadOnlyDictionary<string, JsonObject> gates,
                                       IReadOnlyDictionary<string, JsonObject> crossings, double dx, double dy)
    {
        var segments = path.Segments.DeepClone().AsArray();
        foreach (var segment in segments)
            RamGateFactory.TranslateSegment(segment!.AsObject(), dx, dy);
        var frozen = new JsonObject
        {
            ["PathId"] = Guid.NewGuid().ToString(),
            ["Segments"] = segments,
            ["IsBlockedFallback"] = path.IsBlockedFallback,
            ["IsRouteFrozen"] = true,
            ["ConnectionType"] = "Auto",
            ["WidthMicrometers"] = 0.5,
            ["BendRadiusMicrometers"] = 10,
        };
        EmitEndpoint(frozen, "Start", path.StartRole, path.StartPin, gates, crossings);
        EmitEndpoint(frozen, "End", path.EndRole, path.EndPin, gates, crossings);
        return frozen;
    }

    /// <summary>Writes one path endpoint: a gate's external pin, or a port of a cell crossing.</summary>
    private static void EmitEndpoint(JsonObject path, string prefix, string role, string pin,
                                     IReadOnlyDictionary<string, JsonObject> gates, IReadOnlyDictionary<string, JsonObject> crossings)
    {
        if (!role.StartsWith(CrossingRolePrefix, StringComparison.Ordinal))
        {
            EmitPinTriple(path, prefix, gates[role], pin);
            return;
        }
        var crossing = crossings[role[CrossingRolePrefix.Length..]];
        path[$"{prefix}ComponentId"] = crossing["Identifier"]!.GetValue<string>();
        path[$"{prefix}ComponentGuid"] = crossing["ComponentGuid"]!.GetValue<string>();
        path[$"{prefix}PinName"] = pin;
    }

    /// <summary>Emits one cell port: the instance leaf triple behind the gate pin, template-relative position.</summary>
    private static JsonObject EmitPort(string name, TemplatePort port, IReadOnlyDictionary<string, JsonObject> gates)
    {
        var extPin = FindExternalPin(gates[port.Role], port.Pin);
        return new JsonObject
        {
            ["PinId"] = Guid.NewGuid().ToString(),
            ["Name"] = name,
            ["InternalComponentId"] = extPin["InternalComponentId"]!.GetValue<string>(),
            ["InternalComponentGuid"] = extPin["InternalComponentGuid"]!.GetValue<string>(),
            ["InternalPinName"] = extPin["InternalPinName"]!.GetValue<string>(),
            ["RelativeX"] = port.RelativeX,
            ["RelativeY"] = port.RelativeY,
            ["AngleDegrees"] = port.AngleDegrees,
        };
    }

    /// <summary>Writes the leaf component triple of a gate's external pin under the given endpoint prefix.</summary>
    private static void EmitPinTriple(JsonObject path, string prefix, JsonObject gateEntry, string pinName)
    {
        var extPin = FindExternalPin(gateEntry, pinName);
        path[$"{prefix}ComponentId"] = extPin["InternalComponentId"]!.GetValue<string>();
        path[$"{prefix}ComponentGuid"] = extPin["InternalComponentGuid"]!.GetValue<string>();
        path[$"{prefix}PinName"] = extPin["InternalPinName"]!.GetValue<string>();
    }
}

/// <summary>One stamped-out word cell: the top-level cell entry plus its nested gate entries.</summary>
public sealed class RamWordCellInstance
{
    /// <summary>The cell's own group entry (children are the gate groups plus any crossings between their wires).</summary>
    public required JsonObject CellGroupEntry { get; init; }

    /// <summary>The nested gate entries keyed by cell role (<c>LE2</c>, <c>CSEL_0</c>, …), parents set.</summary>
    public required IReadOnlyDictionary<string, JsonObject> GateEntriesByRole { get; init; }

    /// <summary>The cell group's identifier — the wire endpoint alias of the instance.</summary>
    public required string Identifier { get; init; }

    /// <summary>Right edge of the cell body in micrometers (for chip bounds).</summary>
    public required double Right { get; init; }

    /// <summary>Bottom edge of the cell body in micrometers (for chip bounds).</summary>
    public required double Bottom { get; init; }
}

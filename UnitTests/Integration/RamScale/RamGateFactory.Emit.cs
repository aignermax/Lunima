using System.Text.Json.Nodes;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// The gate-emission half of <see cref="RamGateFactory"/> (issue #1366): instantiates one
/// gate from its shipped template with every identifier re-rolled and the body, external
/// pins and frozen internal paths translated onto the column/row grid — arc centers shift
/// with their endpoints — plus the segment-translation / identifier-remap helpers the
/// instancing template reuses.
/// </summary>
internal sealed partial class RamGateFactory
{
    /// <summary>
    /// Instantiates one gate from its shipped template with every identifier re-rolled
    /// and the body, external pins and frozen internal paths translated onto the next
    /// free row of <paramref name="column"/> — arc centers shift with their endpoints.
    /// </summary>
    public string Emit(string shape, string name, int column, string description,
        Dictionary<string, string>? outputSignals = null, bool isRegister = false)
    {
        var template = _templates[shape];
        var groupDto = template["GroupDto"]!.DeepClone().AsObject();
        var assignment = template["TruthTablePinAssignment"]!.DeepClone().AsObject();

        var guidMap = groupDto["ChildComponentGuids"]!.AsArray()
            .ToDictionary(g => g!.GetValue<string>(), _ => Guid.NewGuid().ToString());
        var idMap = groupDto["ChildComponentIds"]!.AsArray()
            .ToDictionary(id => id!.GetValue<string>(), id => $"{id!.GetValue<string>().Split('_')[0]}_{Guid.NewGuid():N}");

        groupDto["Identifier"] = $"group_{Guid.NewGuid():N}";
        groupDto["GroupName"] = name;
        groupDto["Description"] = description;
        groupDto["IdGuid"] = Guid.NewGuid().ToString();
        groupDto["ChildComponentGuids"] = new JsonArray(groupDto["ChildComponentGuids"]!.AsArray()
            .Select(g => (JsonNode)guidMap[g!.GetValue<string>()]).ToArray());
        groupDto["ChildComponentIds"] = new JsonArray(groupDto["ChildComponentIds"]!.AsArray()
            .Select(id => (JsonNode)idMap[id!.GetValue<string>()]).ToArray());

        int row = _nextRowPerColumn.GetValueOrDefault(column);
        _nextRowPerColumn[column] = row + 1;
        double canvasX = OriginX + column * PitchX;
        double canvasY = OriginY + row * PitchY;
        double dx = canvasX - template["CanvasX"]!.GetValue<double>();
        double dy = canvasY - template["CanvasY"]!.GetValue<double>();

        foreach (var path in groupDto["InternalPaths"]!.AsArray())
        {
            var pathObject = path!.AsObject();
            pathObject["PathId"] = Guid.NewGuid().ToString();
            pathObject["StartComponentId"] = idMap[pathObject["StartComponentId"]!.GetValue<string>()];
            pathObject["EndComponentId"] = idMap[pathObject["EndComponentId"]!.GetValue<string>()];
            Remap(pathObject, guidMap, "StartComponentGuid");
            Remap(pathObject, guidMap, "EndComponentGuid");
            foreach (var segment in pathObject["Segments"]!.AsArray())
                TranslateSegment(segment!.AsObject(), dx, dy);
        }
        foreach (var pin in groupDto["ExternalPins"]!.AsArray())
        {
            var pinObject = pin!.AsObject();
            pinObject["PinId"] = Guid.NewGuid().ToString();
            pinObject["InternalComponentId"] = idMap[pinObject["InternalComponentId"]!.GetValue<string>()];
            Remap(pinObject, guidMap, "InternalComponentGuid");
        }

        var children = new JsonArray();
        foreach (var child in template["ChildComponents"]!.AsArray())
        {
            var childObject = child!.DeepClone().AsObject();
            childObject["Identifier"] = idMap[childObject["Identifier"]!.GetValue<string>()];
            childObject["ComponentGuid"] = guidMap[childObject["ComponentGuid"]!.GetValue<string>()];
            childObject["X"] = childObject["X"]!.GetValue<double>() + dx;
            childObject["Y"] = childObject["Y"]!.GetValue<double>() + dy;
            children.Add(childObject);
        }

        groupDto["PhysicalX"] = canvasX;
        groupDto["PhysicalY"] = canvasY;
        if (isRegister)
            assignment["IsRegister"] = true;
        else
            assignment.Remove("IsRegister");
        if (outputSignals != null)
            assignment["OutputSignalNames"] = new JsonObject(outputSignals.Select(kv =>
                new KeyValuePair<string, JsonNode?>(kv.Key, kv.Value)).ToArray());

        var group = new JsonObject
        {
            ["GroupDto"] = groupDto,
            ["ChildComponents"] = children,
            ["CanvasX"] = canvasX,
            ["CanvasY"] = canvasY,
            ["TruthTablePinAssignment"] = assignment,
        };
        TrackIdentifiers(group);
        _groups.Add(group);
        _groupsByName.Add(name, group);
        return name;
    }

    /// <summary>Translates one path segment by (dx, dy), arc centers included.</summary>
    internal static void TranslateSegment(JsonObject segment, double dx, double dy)
    {
        Shift(segment, "StartX", dx);
        Shift(segment, "EndX", dx);
        Shift(segment, "StartY", dy);
        Shift(segment, "EndY", dy);
        Shift(segment, "CenterX", dx);
        Shift(segment, "CenterY", dy);
    }

    internal static void Remap(JsonObject node, IReadOnlyDictionary<string, string> map, string property)
    {
        var value = node[property]?.GetValue<string>();
        if (value != null && map.TryGetValue(value, out var mapped))
            node[property] = mapped;
    }

    private void TrackIdentifiers(JsonObject group)
    {
        foreach (var id in new[]
                 {
                     group["GroupDto"]!["Identifier"]!.GetValue<string>(),
                     group["GroupDto"]!["IdGuid"]!.GetValue<string>(),
                 })
        {
            if (!_usedIdentifiers.Add(id))
                throw new InvalidOperationException($"duplicate identifier generated: {id}");
        }
    }

    private string NextCopyName(string prefix)
    {
        _copyCounters.TryGetValue(prefix, out int count);
        _copyCounters[prefix] = count + 1;
        return $"{prefix}_{count}";
    }

    private static void Shift(JsonObject node, string property, double delta)
    {
        if (node[property] is JsonValue value && value.TryGetValue<double>(out double coordinate))
            node[property] = coordinate + delta;
    }
}

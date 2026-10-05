using System.Text.Json.Nodes;

namespace UnitTests.Integration.RamScale;

/// <summary>
/// The pre-routed half of <see cref="RamWordCellTemplate"/> (issue #1409): the shipped
/// <c>Logic Gate RAM 2x4.lun</c> example carries the word cell exactly as the spike's
/// bake froze it — the <c>CELL0</c> group with its nested gate entries and the routed
/// intra-cell wires as group <c>InternalPaths</c> — so the feasibility fixture can lift
/// the template straight from the file instead of re-routing the cell on every run. The
/// extraction maps every internal path back onto its gate role and external pin name, so
/// the result is the same template <see cref="Extract"/> produces and instancing works
/// unchanged. The blocked-fallback count of the frozen paths rides along for the
/// floorplan-budget assertion of issue #1400.
/// </summary>
public sealed partial class RamWordCellTemplate
{
    /// <summary>
    /// Lifts the frozen word cell <paramref name="cellGroupName"/> out of a parsed .lun
    /// document: gates, frozen intra-cell paths and ports, all in template coordinates.
    /// </summary>
    public static RamWordCellTemplate FromExample(JsonObject document, string cellGroupName, RamWordCellDesign cell)
    {
        var groups = document["Groups"]!.AsArray();
        var cellEntry = groups.Select(g => g!.AsObject())
            .Single(g => g["GroupDto"]!["GroupName"]!.GetValue<string>() == cellGroupName);
        var cellDto = cellEntry["GroupDto"]!.AsObject();
        string cellGuid = cellDto["IdGuid"]!.GetValue<string>();

        var gateEntries = groups.Select(g => g!.AsObject())
            .Where(g => g["GroupDto"]!["ParentGroupIdGuid"]?.GetValue<string>() == cellGuid)
            .ToList();
        var nameToRole = cell.GateRoles.ToDictionary(kv => kv.Value, kv => kv.Key);
        var gates = gateEntries
            .Select(g => (nameToRole[g["GroupDto"]!["GroupName"]!.GetValue<string>()], (JsonObject)g.DeepClone()))
            .ToList();

        var paths = cellDto["InternalPaths"]!.AsArray()
            .Select(p => ToTemplatePath(p!.AsObject(), gateEntries, nameToRole))
            .ToList();
        int blockedCount = cellDto["InternalPaths"]!.AsArray()
            .Count(p => p!["IsBlockedFallback"]?.GetValue<bool>() == true);

        double originX = gateEntries.Min(g => g["GroupDto"]!["PhysicalX"]!.GetValue<double>());
        double originY = gateEntries.Min(g => g["GroupDto"]!["PhysicalY"]!.GetValue<double>());
        double width = gateEntries.Max(g => g["GroupDto"]!["PhysicalX"]!.GetValue<double>()
            + MaxPinOffset(g, "RelativeX")) - originX;
        double height = gateEntries.Max(g => g["GroupDto"]!["PhysicalY"]!.GetValue<double>()) - originY
            + RamGateFactory.RowPitch;
        var ports = cell.Ports.ToDictionary(kv => kv.Key, kv => ToTemplatePort(kv.Value, gates, originX, originY));
        return new RamWordCellTemplate(gates, paths, ports, originX, originY, width, height, blockedCount);
    }

    /// <summary>Maps a frozen internal path back to its endpoint gate roles and pin names.</summary>
    private static TemplatePath ToTemplatePath(
        JsonObject path,
        IReadOnlyList<JsonObject> gateEntries,
        IReadOnlyDictionary<string, string> nameToRole)
    {
        var (startRole, startPin) = EndpointRole(path, "Start", gateEntries, nameToRole);
        var (endRole, endPin) = EndpointRole(path, "End", gateEntries, nameToRole);
        return new TemplatePath(startRole, startPin, endRole, endPin, path["Segments"]!.DeepClone().AsArray());
    }

    /// <summary>Resolves one frozen-path endpoint to its gate role and external pin name.</summary>
    private static (string Role, string Pin) EndpointRole(
        JsonObject path,
        string prefix,
        IReadOnlyList<JsonObject> gateEntries,
        IReadOnlyDictionary<string, string> nameToRole)
    {
        string componentGuid = path[$"{prefix}ComponentGuid"]!.GetValue<string>();
        string pinName = path[$"{prefix}PinName"]!.GetValue<string>();
        foreach (var gate in gateEntries)
        {
            var extPin = gate["GroupDto"]!["ExternalPins"]!.AsArray()
                .Select(p => p!.AsObject())
                .FirstOrDefault(p => p["InternalComponentGuid"]!.GetValue<string>() == componentGuid
                    && p["InternalPinName"]!.GetValue<string>() == pinName);
            if (extPin != null)
                return (nameToRole[gate["GroupDto"]!["GroupName"]!.GetValue<string>()], extPin["Name"]!.GetValue<string>());
        }
        throw new InvalidOperationException(
            $"frozen path endpoint '{componentGuid}:{pinName}' belongs to no gate of the cell in the shipped example");
    }
}

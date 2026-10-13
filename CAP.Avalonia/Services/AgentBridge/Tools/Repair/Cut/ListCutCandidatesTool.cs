using System.Text.Json;
using CAP_Core.Components.Connections;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Repair.Cut;

/// <summary>
/// <c>list_cut_candidates</c>: places where a crossing could be inserted — where two wires
/// cross (a blocked wire's placeholder line counts) and where a pin's guide line meets a wire
/// (the Cut tool's markers) — with the wire that would be cut, the wire that would pass
/// through, and whether a crossing fits there.
/// </summary>
public sealed class ListCutCandidatesTool : IAgentBridgeTool
{
    private const int DefaultLimit = 50;
    private const int Decimals = 3;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public ListCutCandidatesTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "list_cut_candidates";

    /// <inheritdoc/>
    public string Description =>
        "List places where a waveguide crossing can be inserted (Cut tool): kind=wire_intersection where two " +
        "wires cross (a blocked wire's straight placeholder line counts), kind=pin_guide where a pin's axis meets " +
        "a wire. Each has cut_connection_id (the wire split by the crossing), through_connection_id (the wire " +
        "that would pass straight through it — pass it to insert_crossing to swap two wires' order), x/y in µm, " +
        "and fits (enough straight run and a clear footprint — for wire_intersection spots counting the through wire as re-docked, so pass through_connection_id when inserting) or the reason it does not.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            connection_id = new { type = "string", description = "Only spots where this connection is cut or passes through (e.g. a blocked wire)." },
            fits_only = new { type = "boolean", description = "Only spots where a crossing fits. Default false." },
            limit = new { type = "number", description = "Maximum spots returned (default 50)." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var supply = CrossingSupply.Resolve(_session);
        if (supply == null)
            return Task.FromResult(AgentToolResult.Error("No usable 4-port crossing is loaded in the PDK library."));

        WaveguideConnection? focus = null;
        var id = ToolArguments.GetString(arguments, "connection_id");
        if (!string.IsNullOrWhiteSpace(id))
        {
            focus = _session.FindConnection(id)?.Connection;
            if (focus == null)
                return Task.FromResult(AgentToolResult.Error($"Unknown connection id '{id}'. Use list_connections."));
        }

        var spots = new CutSpotFinder(_session, supply).Find(focus).AsEnumerable();
        if (ToolArguments.GetBool(arguments, "fits_only"))
            spots = spots.Where(s => s.Fits);
        var all = spots.ToList();
        var limit = (int)Math.Max(1, ToolArguments.GetDouble(arguments, "limit") ?? DefaultLimit);

        return Task.FromResult(AgentToolResult.Json(new
        {
            Count = all.Count,
            CrossingSizeUm = Math.Max(supply.Template.WidthMicrometers, supply.Template.HeightMicrometers),
            Spots = all.Take(limit).Select(Describe).ToList(),
        }));
    }

    private static object Describe(CutSpot spot) => new
    {
        spot.Kind,
        CutConnectionId = spot.Cut.Id.ToString(),
        CutConnection = $"{spot.Cut.StartPin.ParentComponent.Identifier}.{spot.Cut.StartPin.Name} → {spot.Cut.EndPin.ParentComponent.Identifier}.{spot.Cut.EndPin.Name}",
        ThroughConnectionId = spot.Through?.Id.ToString(),
        ThroughBlocked = spot.Through?.IsBlockedFallback,
        GuidePin = spot.GuidePin == null ? null : $"{spot.GuidePin.ParentComponent.Identifier}.{spot.GuidePin.Name}",
        X = Math.Round(spot.Point.X, Decimals),
        Y = Math.Round(spot.Point.Y, Decimals),
        spot.Fits,
        spot.Reason,
    };
}

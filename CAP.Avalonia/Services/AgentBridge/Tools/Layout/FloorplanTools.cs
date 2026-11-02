using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Tools.Inspect;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Layout;

/// <summary>
/// <c>get_floorplan</c>: the chip as a floorplan — chip size, the area the design uses, how full
/// it is (parts plus routed waveguides), the dies, and the largest free rectangles where a new
/// block fits.
/// </summary>
public sealed class GetFloorplanTool : IAgentBridgeTool
{
    private const double DefaultClearance = 20;
    private const int DefaultFreeAreas = 5;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public GetFloorplanTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "get_floorplan";

    /// <inheritdoc/>
    public string Description =>
        "Floorplan of the chip: chip bounds (µm), the bounding box the design uses, utilisation (share of the chip " +
        "covered by parts incl. clearance and by routed waveguides), dies, and the largest free rectangles (x, y, " +
        "width, height) where a new block fits. Use before placing large cells.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            clearance_um = new { type = "number", description = $"Keep-out around parts (default {DefaultClearance})." },
            free_areas = new { type = "integer", description = $"How many free rectangles to report (default {DefaultFreeAreas})." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var canvas = _session.Canvas;
        var grid = FloorplanGrid.Build(canvas, ToolArguments.GetDouble(arguments, "clearance_um") ?? DefaultClearance);
        var bounds = canvas.Components.Select(c => DesignDescriber.Describe(c).Bounds).ToList();
        var chipArea = (canvas.ChipMaxX - canvas.ChipMinX) * (canvas.ChipMaxY - canvas.ChipMinY);
        var partArea = canvas.Components.Sum(c => c.Component.WidthMicrometers * c.Component.HeightMicrometers);

        return Task.FromResult(AgentToolResult.Json(new
        {
            Chip = new Area(canvas.ChipMinX, canvas.ChipMinY, canvas.ChipMaxX - canvas.ChipMinX, canvas.ChipMaxY - canvas.ChipMinY),
            Used = bounds.Count == 0 ? null : new Area(bounds.Min(b => b.MinX), bounds.Min(b => b.MinY),
                bounds.Max(b => b.MaxX) - bounds.Min(b => b.MinX), bounds.Max(b => b.MaxY) - bounds.Min(b => b.MinY)),
            ComponentCount = canvas.Components.Count,
            PartAreaFraction = chipArea > 0 ? Math.Round(partArea / chipArea, 4) : 0,
            OccupiedFraction = Math.Round(grid.TakenFraction(), 4),
            GridCellUm = Math.Round(grid.Cell, 2),
            Dies = canvas.Components.Where(c => c.Component is ComponentGroup { IsDie: true })
                .Select(c => new { Id = c.Component.Identifier, Name = ((ComponentGroup)c.Component).GroupName, Bounds = DesignDescriber.Describe(c).Bounds })
                .ToList(),
            FreeAreas = grid.LargestFree((int)(ToolArguments.GetDouble(arguments, "free_areas") ?? DefaultFreeAreas)),
        }));
    }
}

/// <summary>
/// <c>find_free_space</c>: top-left positions where a block of a given size can be placed —
/// inside the chip, clear of every part (by the canvas' own placement rule) and of routed
/// waveguides — nearest to a point of interest first.
/// </summary>
public sealed class FindFreeSpaceTool : IAgentBridgeTool
{
    private const int DefaultCount = 3;
    private const int MaxCandidates = 200_000;
    // The canvas keeps parts at least 5 µm apart; spots reported together must keep it too.
    private const double SpotGapMicrometers = 10;

    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public FindFreeSpaceTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "find_free_space";

    /// <inheritdoc/>
    public string Description =>
        "Find top-left positions (µm) for a width × height block: inside the chip, clear of parts and of routed " +
        "waveguides, closest to near_x/near_y (default: the chip centre). Returns up to count non-overlapping spots; " +
        "pass them to place_component.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            width = new { type = "number", description = "Block width in µm (e.g. a footprint from list_component_types)." },
            height = new { type = "number", description = "Block height in µm." },
            near_x = new { type = "number", description = "Preferred x (µm)." },
            near_y = new { type = "number", description = "Preferred y (µm)." },
            count = new { type = "integer", description = $"How many spots (default {DefaultCount})." },
        },
        required = new[] { "width", "height" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var canvas = _session.Canvas;
        var width = ToolArguments.GetDouble(arguments, "width");
        var height = ToolArguments.GetDouble(arguments, "height");
        if (width is not > 0 || height is not > 0)
            return Task.FromResult(AgentToolResult.Error("Pass a positive width and height (µm)."));
        var nearX = ToolArguments.GetDouble(arguments, "near_x") ?? (canvas.ChipMinX + canvas.ChipMaxX) / 2 - width.Value / 2;
        var nearY = ToolArguments.GetDouble(arguments, "near_y") ?? (canvas.ChipMinY + canvas.ChipMaxY) / 2 - height.Value / 2;
        var count = Math.Max(1, (int)(ToolArguments.GetDouble(arguments, "count") ?? DefaultCount));

        var wires = canvas.Connections.Select(c => c.Connection.RoutedPath).Where(p => p != null)
            .SelectMany(p => p!.Segments).Select(s => (s.StartPoint, s.EndPoint)).ToList();
        var step = Math.Max(1, Math.Min(width.Value, height.Value) / 2);
        var spots = new List<Area>();
        foreach (var (x, y) in Candidates(canvas, nearX, nearY, step, width.Value, height.Value))
        {
            var spot = new Area(x, y, width.Value, height.Value);
            if (spots.Any(s => Overlaps(s, spot)) || wires.Any(w => Crosses(spot, w.StartPoint, w.EndPoint)))
                continue;
            if (!canvas.CanPlaceComponent(x, y, width.Value, height.Value))
                continue;
            spots.Add(spot);
            if (spots.Count == count) break;
        }

        return Task.FromResult(spots.Count == 0
            ? AgentToolResult.Error("No free spot of that size on the chip. Enlarge the chip (set_chip_size) or compact the layout.")
            : AgentToolResult.Json(new { Spots = spots }));
    }

    /// <summary>Grid positions inside the chip, nearest to (nearX, nearY) first.</summary>
    private static IEnumerable<(double X, double Y)> Candidates(
        ViewModels.Canvas.DesignCanvasViewModel canvas, double nearX, double nearY, double step, double width, double height)
    {
        var positions = new List<(double X, double Y, double Distance)>();
        for (var x = canvas.ChipMinX; x + width <= canvas.ChipMaxX && positions.Count < MaxCandidates; x += step)
            for (var y = canvas.ChipMinY; y + height <= canvas.ChipMaxY && positions.Count < MaxCandidates; y += step)
                positions.Add((x, y, (x - nearX) * (x - nearX) + (y - nearY) * (y - nearY)));
        return positions.OrderBy(p => p.Distance).Select(p => (Math.Round(p.X, 2), Math.Round(p.Y, 2)));
    }

    private static bool Overlaps(Area a, Area b) =>
        a.X - SpotGapMicrometers < b.X + b.Width && b.X - SpotGapMicrometers < a.X + a.Width
        && a.Y - SpotGapMicrometers < b.Y + b.Height && b.Y - SpotGapMicrometers < a.Y + a.Height;

    /// <summary>Whether the segment passes through the rectangle (sampled along its length).</summary>
    private static bool Crosses(Area area, (double X, double Y) from, (double X, double Y) to)
    {
        var length = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
        var samples = Math.Max(2, (int)(length / Math.Max(1, Math.Min(area.Width, area.Height) / 4)));
        for (var i = 0; i <= samples; i++)
        {
            var t = i / (double)samples;
            var x = from.X + (to.X - from.X) * t;
            var y = from.Y + (to.Y - from.Y) * t;
            if (x >= area.X && x <= area.X + area.Width && y >= area.Y && y <= area.Y + area.Height)
                return true;
        }
        return false;
    }
}

/// <summary><c>set_chip_size</c>: resizes the chip (the placement area) — like the chip-size field in the toolbar.</summary>
public sealed class SetChipSizeTool : IAgentBridgeTool
{
    private readonly AgentDesignSession _session;

    /// <summary>Creates the tool.</summary>
    public SetChipSizeTool(AgentDesignSession session) => _session = session;

    /// <inheritdoc/>
    public string Name => "set_chip_size";

    /// <inheritdoc/>
    public string Description =>
        "Set the chip (placement area) to width_um × height_um. Not available while the canvas is a multi-die " +
        "assembly (each die has its own size). Returns the applied bounds.";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            width_um = new { type = "number", description = "Chip width in µm." },
            height_um = new { type = "number", description = "Chip height in µm." },
        },
        required = new[] { "width_um", "height_um" },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var chip = _session.Main?.ChipSize;
        if (chip == null)
            return Task.FromResult(AgentToolResult.Error("The main window is not ready yet."));
        if (!chip.CanEditRootChipSize)
            return Task.FromResult(AgentToolResult.Error("The canvas is a multi-die assembly; size the dies instead."));
        var width = ToolArguments.GetDouble(arguments, "width_um") ?? ToolArguments.GetDouble(arguments, "width");
        var height = ToolArguments.GetDouble(arguments, "height_um") ?? ToolArguments.GetDouble(arguments, "height");
        if (width is not > 0 || height is not > 0)
            return Task.FromResult(AgentToolResult.Error("Pass positive width_um and height_um."));

        chip.ApplyFromMicrometers(width.Value, height.Value);
        var canvas = _session.Canvas;
        return Task.FromResult(AgentToolResult.Json(new
        {
            WidthUm = chip.CurrentWidthMicrometers,
            HeightUm = chip.CurrentHeightMicrometers,
            Applied = Math.Abs(chip.CurrentWidthMicrometers - width.Value) < 1 && Math.Abs(chip.CurrentHeightMicrometers - height.Value) < 1,
            Chip = new Area(canvas.ChipMinX, canvas.ChipMinY, canvas.ChipMaxX - canvas.ChipMinX, canvas.ChipMaxY - canvas.ChipMinY),
        }));
    }
}

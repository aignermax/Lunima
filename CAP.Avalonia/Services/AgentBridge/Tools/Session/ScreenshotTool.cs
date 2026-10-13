using System.Globalization;
using System.Text.Json;
using Avalonia;
using CAP.Avalonia.ViewModels.Canvas;

namespace CAP.Avalonia.Services.AgentBridge.Tools.Session;

/// <summary>
/// <c>screenshot</c>: PNG of the whole design or of a µm region, rendered with the real
/// canvas renderer and returned as MCP image content.
/// </summary>
public sealed class ScreenshotTool : IAgentBridgeTool
{
    /// <summary>Image width used when the agent does not ask for one.</summary>
    public const int DefaultWidthPixels = 1600;

    private const int MinWidthPixels = 64;
    private const double DesignPaddingFraction = 0.05;

    private readonly AgentDesignSession _session;
    private readonly CanvasSnapshotRenderer _renderer;

    /// <summary>Creates the tool.</summary>
    public ScreenshotTool(AgentDesignSession session, CanvasSnapshotRenderer renderer)
    {
        _session = session;
        _renderer = renderer;
    }

    /// <inheritdoc/>
    public string Name => "screenshot";

    /// <inheritdoc/>
    public string Description =>
        "PNG screenshot of the design canvas as Lunima draws it (blocked routes are red). Without a region it " +
        "frames the whole design; pass x, y, width, height in µm to zoom into a region (e.g. around a blocked " +
        "connection's endpoints). width_px sets the image width (default 1600, max 4096).";

    /// <inheritdoc/>
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            x = new { type = "number", description = "Region left edge in µm (optional)." },
            y = new { type = "number", description = "Region top edge in µm (optional)." },
            width = new { type = "number", description = "Region width in µm (optional)." },
            height = new { type = "number", description = "Region height in µm (optional)." },
            width_px = new { type = "integer", description = "Image width in pixels (default 1600, max 4096)." },
        },
    };

    /// <inheritdoc/>
    public Task<AgentToolResult> ExecuteAsync(JsonElement arguments)
    {
        var region = ReadRegion(arguments) ?? DesignRegion(_session.Canvas);
        if (region.Width <= 0 || region.Height <= 0)
            return Task.FromResult(AgentToolResult.Error("The region needs a positive width and height."));

        var widthPx = (int)Math.Clamp(ToolArguments.GetDouble(arguments, "width_px") ?? DefaultWidthPixels,
            MinWidthPixels, CanvasSnapshotRenderer.MaxEdgePixels);
        var (png, w, h) = _renderer.Render(_session.Canvas, region, widthPx);
        var caption = string.Create(CultureInfo.InvariantCulture,
            $"Region x={region.X:F1} y={region.Y:F1} width={region.Width:F1} height={region.Height:F1} µm, {w}x{h} px.");
        return Task.FromResult(AgentToolResult.Png(png, caption));
    }

    private static Rect? ReadRegion(JsonElement args)
    {
        var x = ToolArguments.GetDouble(args, "x");
        var y = ToolArguments.GetDouble(args, "y");
        var width = ToolArguments.GetDouble(args, "width");
        var height = ToolArguments.GetDouble(args, "height");
        if (x == null || y == null || width == null || height == null)
            return null;
        return new Rect(x.Value, y.Value, width.Value, height.Value);
    }

    /// <summary>The design's bounding box with a small margin, or the chip area when empty.</summary>
    public static Rect DesignRegion(DesignCanvasViewModel canvas)
    {
        if (BoundingBoxCalculator.Calculate(canvas.Components) is not { Width: > 0, Height: > 0 } box)
            return new Rect(canvas.ChipMinX, canvas.ChipMinY,
                canvas.ChipMaxX - canvas.ChipMinX, canvas.ChipMaxY - canvas.ChipMinY);

        var padded = BoundingBoxCalculator.WithPadding(box, DesignPaddingFraction);
        return new Rect(padded.MinX, padded.MinY, padded.Width, padded.Height);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CAP_Core.Routing;

/// <summary>
/// Defines a deterministic PIC layout for routing tests.
/// Can be serialized to/from JSON for reproducible test cases.
/// </summary>
public class LayoutTestDefinition
{
    /// <summary>
    /// Human-readable name for this test layout.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Components in the layout.
    /// </summary>
    public List<LayoutComponent> Components { get; set; } = new();

    /// <summary>
    /// Connections to route between component pins.
    /// </summary>
    public List<LayoutConnection> Connections { get; set; } = new();

    /// <summary>
    /// Expected minimum bend radius in micrometers.
    /// </summary>
    public double MinBendRadiusMicrometers { get; set; } = 10.0;

    /// <summary>
    /// Serializes the layout definition to JSON.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    /// <summary>
    /// Deserializes a layout definition from JSON.
    /// </summary>
    /// <param name="json">JSON string</param>
    /// <returns>Layout definition</returns>
    public static LayoutTestDefinition FromJson(string json)
    {
        return JsonSerializer.Deserialize<LayoutTestDefinition>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize layout");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// A component in a test layout.
/// </summary>
public class LayoutComponent
{
    /// <summary>
    /// Component type identifier (e.g., "YJunction", "Coupler").
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// X position in micrometers.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y position in micrometers.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Width in micrometers.
    /// </summary>
    public double Width { get; set; } = 50;

    /// <summary>
    /// Height in micrometers.
    /// </summary>
    public double Height { get; set; } = 50;

    /// <summary>
    /// Pin definitions for this component.
    /// </summary>
    public List<LayoutPin> Pins { get; set; } = new();
}

/// <summary>
/// A pin on a test layout component.
/// </summary>
public class LayoutPin
{
    /// <summary>
    /// Pin name (e.g., "output", "input").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// X offset from component origin in micrometers.
    /// </summary>
    public double OffsetX { get; set; }

    /// <summary>
    /// Y offset from component origin in micrometers.
    /// </summary>
    public double OffsetY { get; set; }

    /// <summary>
    /// Pin facing angle in degrees (0=East, 90=North, 180=West, 270=South).
    /// </summary>
    public double AngleDegrees { get; set; }
}

/// <summary>
/// A connection to route between two pins.
/// </summary>
public class LayoutConnection
{
    /// <summary>
    /// Index of the start component in the Components list.
    /// </summary>
    public int FromComponentIndex { get; set; }

    /// <summary>
    /// Name of the start pin.
    /// </summary>
    public string FromPin { get; set; } = "";

    /// <summary>
    /// Index of the end component in the Components list.
    /// </summary>
    public int ToComponentIndex { get; set; }

    /// <summary>
    /// Name of the end pin.
    /// </summary>
    public string ToPin { get; set; } = "";
}

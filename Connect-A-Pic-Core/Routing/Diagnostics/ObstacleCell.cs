namespace CAP_Core.Routing.Diagnostics;

/// <summary>
/// Represents a single blocked cell in the obstacle map extract.
/// </summary>
public class ObstacleCell
{
    /// <summary>
    /// Grid X coordinate.
    /// </summary>
    public int GridX { get; set; }

    /// <summary>
    /// Grid Y coordinate.
    /// </summary>
    public int GridY { get; set; }

    /// <summary>
    /// Cell state: 1 = component obstacle, 2 = waveguide obstacle.
    /// </summary>
    public byte State { get; set; }
}

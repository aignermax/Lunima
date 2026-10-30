using CAP_DataAccess.Persistence.PIR;

namespace CAP_DataAccess.Persistence.NativeDesign;

/// <summary>
/// DTO for serializing waveguide path segments.
/// </summary>
public class PathSegmentData
{
    public string Type { get; set; } = "";
    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }
    public double StartAngleDegrees { get; set; }
    public double EndAngleDegrees { get; set; }
    public double? CenterX { get; set; }
    public double? CenterY { get; set; }
    public double? RadiusMicrometers { get; set; }
    public double? SweepAngleDegrees { get; set; }
}

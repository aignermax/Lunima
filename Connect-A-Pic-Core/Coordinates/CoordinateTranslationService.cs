using CAP_Core.Components.Core;

namespace CAP_Core.Coordinates;

/// <summary>
/// Single source of truth for coordinate transformations between coordinate systems used in Lunima.
///
/// Coordinate Systems:
///
/// 1. Viewport Coordinates (Grid)
///    - Origin: Top-left
///    - Units: Grid cells (integer)
///    - Y-axis: Down
///    - Used in: UI, DesignCanvas
///
/// 2. Physical Coordinates (Micrometers)
///    - Origin: Top-left
///    - Units: Micrometers (double)
///    - Y-axis: Down
///    - Used in: Component placement, simulation
///
/// 3. Nazca Coordinates (GDS Export)
///    - Origin: Bottom-left (relative to component's Nazca origin)
///    - Units: Micrometers (double)
///    - Y-axis: Up (FLIPPED from physical!)
///    - Rotation: Negated from physical (Nazca convention)
///    - Used in: GDS export, Python generation
///
/// Transformation Pipeline:
///   Viewport → Physical → Nazca → GDS
///      (UI)    (Internal)  (Export)
/// </summary>
public class CoordinateTranslationService
{
    /// <summary>
    /// Nazca Y-axis is flipped relative to the physical/editor coordinate system.
    /// </summary>
    private const double NazcaYFlip = -1.0;

    // ===== Nazca Origin Offset =====

    /// <summary>
    /// Calculates the Nazca origin offset for a component based on its type and settings.
    /// Uses NazcaOriginOffset when explicitly set (non-zero) or for known PDK function names.
    /// For parametric straight waveguides, uses the first pin's offset as origin.
    /// Falls back to height-based offset for legacy components with no explicit origin.
    /// </summary>
    /// <param name="comp">The component to calculate the offset for.</param>
    /// <returns>The (OffsetX, OffsetY) origin offset in micrometers, rotated to world space.</returns>
    public (double OffsetX, double OffsetY) CalculateNazcaOriginOffset(Component comp)
    {
        var funcName = comp.NazcaFunctionName;

        bool hasPdkFunctionName = !string.IsNullOrEmpty(funcName) &&
            (IsPdkFunction(funcName) || funcName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase));

        bool hasExplicitOriginOffset = comp.NazcaOriginOffsetX != 0 || comp.NazcaOriginOffsetY != 0;

        if (hasPdkFunctionName || hasExplicitOriginOffset)
        {
            var (offsetX, offsetY) = RotatePoint(
                comp.NazcaOriginOffsetX, comp.NazcaOriginOffsetY, comp.RotationDegrees);
            return (offsetX, offsetY);
        }

        if (IsParametricStraight(funcName, comp.NazcaFunctionParameters))
        {
            var firstPin = comp.PhysicalPins.FirstOrDefault();
            if (firstPin != null)
                return RotatePoint(firstPin.OffsetXMicrometers, firstPin.OffsetYMicrometers, comp.RotationDegrees);
        }

        return (0, comp.HeightMicrometers);
    }

    // ===== Physical → Nazca =====

    /// <summary>
    /// Converts a component's physical position to Nazca placement coordinates.
    /// Returns the (NazcaX, NazcaY, NazcaRotation) used in the .put() call during GDS export.
    /// Y-axis is flipped and rotation is negated per Nazca convention.
    /// </summary>
    /// <param name="comp">The component to convert.</param>
    /// <returns>Tuple of (NazcaX, NazcaY, NazcaRotationDegrees).</returns>
    public (double NazcaX, double NazcaY, double NazcaRot) ComponentToNazca(Component comp)
    {
        var (originOffsetX, originOffsetY) = CalculateNazcaOriginOffset(comp);
        double nazcaX = comp.PhysicalX + originOffsetX;
        double nazcaY = NazcaYFlip * (comp.PhysicalY + originOffsetY);
        double nazcaRot = -comp.RotationDegrees;
        return (nazcaX, nazcaY, nazcaRot);
    }

    /// <summary>
    /// Gets the absolute Nazca-coordinate position of a physical pin.
    /// Accounts for Y-flip, NazcaOriginOffset, and component rotation transformations.
    /// Used for GDS/Nazca export where waveguide coordinates must match stub pin positions.
    /// </summary>
    /// <param name="pin">The physical pin to convert.</param>
    /// <returns>Absolute (X, Y) Nazca coordinates for the pin.</returns>
    public (double X, double Y) GetPinNazcaPosition(PhysicalPin pin)
    {
        var comp = pin.ParentComponent;
        var (originOffsetX, originOffsetY) = CalculateNazcaOriginOffset(comp);

        // Component Nazca placement position
        double nazcaCompX = comp.PhysicalX + originOffsetX;
        double nazcaCompY = NazcaYFlip * (comp.PhysicalY + originOffsetY);

        // Local pin coordinates in unrotated component (Nazca) space.
        // Pins are positioned relative to the Nazca origin (shifted by originOffset).
        double localPinNazcaX = pin.OffsetXMicrometers - originOffsetX;
        double localPinNazcaY = (comp.HeightMicrometers - pin.OffsetYMicrometers) - originOffsetY;

        // Nazca places cells with .put(x, y, -RotationDegrees), so pin world positions
        // must use the same negated rotation to match stub pin locations.
        var (rotatedPinX, rotatedPinY) = RotatePoint(localPinNazcaX, localPinNazcaY, -comp.RotationDegrees);

        return (nazcaCompX + rotatedPinX, nazcaCompY + rotatedPinY);
    }

    // ===== Rotation Helpers =====

    /// <summary>
    /// Rotates a 2D point around the origin by the given angle in degrees.
    /// Uses the standard counter-clockwise rotation convention.
    /// </summary>
    /// <param name="x">X coordinate of the point.</param>
    /// <param name="y">Y coordinate of the point.</param>
    /// <param name="angleDegrees">Rotation angle in degrees (counter-clockwise).</param>
    /// <returns>The rotated (X, Y) coordinates.</returns>
    public (double X, double Y) RotatePoint(double x, double y, double angleDegrees)
    {
        double rad = angleDegrees * Math.PI / 180.0;
        return (
            x * Math.Cos(rad) - y * Math.Sin(rad),
            x * Math.Sin(rad) + y * Math.Cos(rad)
        );
    }

    // ===== PDK Classification Helpers =====

    /// <summary>
    /// Returns true if the function name looks like a real PDK function (e.g., "ebeam_y_1550").
    /// Recognizes SiEPIC EBeam PDK and other standard PDK naming patterns.
    /// </summary>
    /// <param name="name">The Nazca function name to check.</param>
    /// <returns>True if the name follows PDK naming conventions.</returns>
    public static bool IsPdkFunction(string name) =>
        name.StartsWith("ebeam_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("GC_", StringComparison.Ordinal) ||
        name.StartsWith("ANT_", StringComparison.Ordinal) ||
        name.StartsWith("crossing_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("taper_", StringComparison.OrdinalIgnoreCase) ||
        (name.Contains(".", StringComparison.Ordinal) &&
         !name.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns true if the component represents a parametric straight waveguide.
    /// Parametric straights have a "length=" parameter and a function name containing "straight" or "strt".
    /// </summary>
    /// <param name="funcName">The Nazca function name.</param>
    /// <param name="parameters">The function parameters string.</param>
    /// <returns>True if the component is a parametric straight waveguide.</returns>
    public static bool IsParametricStraight(string? funcName, string? parameters)
    {
        if (string.IsNullOrEmpty(parameters) || string.IsNullOrEmpty(funcName))
            return false;

        var lower = funcName.ToLowerInvariant();
        var hasLength = parameters.Contains("length=", StringComparison.OrdinalIgnoreCase);
        var isStraight = lower.Contains("straight") || lower.Contains("strt");

        return hasLength && isStraight;
    }
}

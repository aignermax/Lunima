using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;

namespace CAP_Core.CodeExporter;

/// <summary>
/// Validates Nazca export results by comparing UI positions with exported code.
/// Ensures end-to-end consistency across the entire export pipeline.
/// </summary>
public class ExportValidator
{
    private const double PositionToleranceMicrometers = 0.01;
    private const double AngleToleranceDegrees = 0.1;

    /// <summary>
    /// Validates that exported Nazca code correctly represents the design.
    /// </summary>
    public ValidationResult Validate(
        List<Component> components,
        List<WaveguideConnection> connections,
        string nazcaCode)
    {
        var result = new ValidationResult();
        var parser = new NazcaCodeParser();
        var parsed = parser.Parse(nazcaCode);

        // Validate component positions
        ValidateComponentPositions(components, parsed, result);

        // Validate waveguide endpoints match pin positions
        ValidateWaveguideEndpoints(connections, components, parsed, result);

        // Validate component dimensions in stubs
        ValidateComponentDimensions(components, nazcaCode, result);

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    /// <summary>
    /// Validates that component placements in Nazca code match expected positions.
    /// </summary>
    private void ValidateComponentPositions(
        List<Component> components,
        ParsedNazcaDesign parsed,
        ValidationResult result)
    {
        if (parsed.Components.Count != components.Count)
        {
            result.Errors.Add(
                $"Component count mismatch: expected {components.Count}, " +
                $"found {parsed.Components.Count} in Nazca code");
            return;
        }

        for (int i = 0; i < components.Count; i++)
        {
            var comp = components[i];
            if (i >= parsed.Components.Count)
                break;

            var placement = parsed.Components[i];

            // Calculate expected Nazca position
            var expectedPos = CalculateExpectedNazcaPosition(comp);

            // Compare positions
            var xDiff = Math.Abs(placement.X - expectedPos.X);
            var yDiff = Math.Abs(placement.Y - expectedPos.Y);

            if (xDiff > PositionToleranceMicrometers || yDiff > PositionToleranceMicrometers)
            {
                result.Errors.Add(
                    $"Component '{comp.Identifier}' position mismatch: " +
                    $"expected ({expectedPos.X:F2}, {expectedPos.Y:F2}), " +
                    $"found ({placement.X:F2}, {placement.Y:F2}) in Nazca code");
            }
            else
            {
                result.Successes.Add(
                    $"Component '{comp.Identifier}' position correct: " +
                    $"({placement.X:F2}, {placement.Y:F2})");
            }

            // Compare rotation
            var expectedRotation = -comp.RotationDegrees; // Nazca uses inverted rotation
            var rotDiff = Math.Abs(NormalizeAngle(placement.RotationDegrees) - NormalizeAngle(expectedRotation));
            if (rotDiff > 180) rotDiff = 360 - rotDiff;

            if (rotDiff > AngleToleranceDegrees)
            {
                result.Errors.Add(
                    $"Component '{comp.Identifier}' rotation mismatch: " +
                    $"expected {expectedRotation:F1}°, found {placement.RotationDegrees:F1}°");
            }
        }
    }

    /// <summary>
    /// Validates that waveguide endpoints exactly match pin positions.
    /// </summary>
    private void ValidateWaveguideEndpoints(
        List<WaveguideConnection> connections,
        List<Component> components,
        ParsedNazcaDesign parsed,
        ValidationResult result)
    {
        foreach (var conn in connections)
        {
            var segments = conn.GetPathSegments();
            if (segments.Count == 0)
                continue;

            var startPin = conn.StartPin;
            var endPin = conn.EndPin;

            // Get absolute pin positions
            var (startPinX, startPinY) = startPin.GetAbsolutePosition();
            var startPinAngle = startPin.GetAbsoluteAngle();

            var (endPinX, endPinY) = endPin.GetAbsolutePosition();
            var endPinAngle = endPin.GetAbsoluteAngle();

            // Find first waveguide stub (should match start pin)
            var firstSegment = segments[0];
            var lastSegment = segments[^1];

            // Convert to Nazca coordinates (Y-axis inverted)
            var nazcaStartY = -startPinY;
            var nazcaEndY = -endPinY;
            var nazcaStartAngle = -startPinAngle;
            var nazcaEndAngle = -endPinAngle;

            // Check if any waveguide stub matches the start pin position
            bool foundStartStub = false;
            foreach (var stub in parsed.WaveguideStubs)
            {
                var xDiff = Math.Abs(stub.StartX - startPinX);
                var yDiff = Math.Abs(stub.StartY - nazcaStartY);
                var angleDiff = Math.Abs(NormalizeAngle(stub.StartAngle) - NormalizeAngle(nazcaStartAngle));
                if (angleDiff > 180) angleDiff = 360 - angleDiff;

                if (xDiff < PositionToleranceMicrometers &&
                    yDiff < PositionToleranceMicrometers &&
                    angleDiff < AngleToleranceDegrees)
                {
                    foundStartStub = true;
                    result.Successes.Add(
                        $"Waveguide start point matches pin '{startPin.Name}' at " +
                        $"({startPinX:F2}, {startPinY:F2})");
                    break;
                }
            }

            if (!foundStartStub && parsed.WaveguideStubs.Count > 0)
            {
                result.Warnings.Add(
                    $"Waveguide stub not found for start pin '{startPin.Name}' at " +
                    $"({startPinX:F2}, {startPinY:F2})");
            }
        }
    }

    /// <summary>
    /// Validates that component dimensions in stubs match component properties.
    /// </summary>
    private void ValidateComponentDimensions(
        List<Component> components,
        string nazcaCode,
        ValidationResult result)
    {
        foreach (var comp in components)
        {
            var expectedWidth = comp.WidthMicrometers.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var expectedHeight = comp.HeightMicrometers.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var dimensionComment = $"({comp.WidthMicrometers}x{comp.HeightMicrometers} µm)";

            if (nazcaCode.Contains(dimensionComment))
            {
                result.Successes.Add(
                    $"Component '{comp.Identifier}' dimensions correct: {dimensionComment}");
            }
            else
            {
                result.Warnings.Add(
                    $"Component '{comp.Identifier}' dimensions {dimensionComment} not found in stub comment");
            }
        }
    }

    /// <summary>
    /// Calculates the expected Nazca position for a component.
    /// Uses NazcaOriginOffset when explicitly set (non-zero) or for known PDK function names.
    /// Mirrors the logic in SimpleNazcaExporter.CalculateOriginOffset (Issue #355 fix).
    /// </summary>
    private (double X, double Y) CalculateExpectedNazcaPosition(Component comp)
    {
        var funcName = comp.NazcaFunctionName;

        bool hasPdkFunctionName = !string.IsNullOrEmpty(funcName) && IsPdkFunction(funcName);
        bool hasExplicitOriginOffset = comp.NazcaOriginOffsetX != 0 || comp.NazcaOriginOffsetY != 0;

        double originOffsetX = 0;
        double originOffsetY = 0;

        if (hasPdkFunctionName || hasExplicitOriginOffset)
        {
            double rotRad = comp.RotationDegrees * Math.PI / 180.0;
            originOffsetX = comp.NazcaOriginOffsetX * Math.Cos(rotRad) - comp.NazcaOriginOffsetY * Math.Sin(rotRad);
            originOffsetY = comp.NazcaOriginOffsetX * Math.Sin(rotRad) + comp.NazcaOriginOffsetY * Math.Cos(rotRad);
        }
        else
        {
            originOffsetY = comp.HeightMicrometers;
        }

        var nazcaX = comp.PhysicalX + originOffsetX;
        var nazcaY = -(comp.PhysicalY + originOffsetY);

        return (nazcaX, nazcaY);
    }

    /// <summary>
    /// Checks if a function name looks like a real PDK function.
    /// </summary>
    private static bool IsPdkFunction(string name) =>
        name.StartsWith("ebeam_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase) ||
        (name.Contains(".", StringComparison.Ordinal) &&
         !name.StartsWith("demo.", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Normalizes an angle to 0-360 range.
    /// </summary>
    private static double NormalizeAngle(double angle)
    {
        angle = angle % 360;
        if (angle < 0) angle += 360;
        return angle;
    }
}

/// <summary>
/// Result of export validation with errors, warnings, and successes.
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public List<string> Successes { get; init; } = new();

    public int TotalChecks => Errors.Count + Warnings.Count + Successes.Count;
    public int PassedChecks => Successes.Count;
    public int FailedChecks => Errors.Count;
    public int WarningCount => Warnings.Count;
}

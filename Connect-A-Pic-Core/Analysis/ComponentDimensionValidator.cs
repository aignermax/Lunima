using CAP_Core.Components.Core;

namespace CAP_Core.Analysis;

/// <summary>
/// Validates that component dimensions correctly encompass all physical pins.
/// Detects dimensional mismatches that can cause incorrect GDS export.
/// </summary>
public class ComponentDimensionValidator
{
    /// <summary>
    /// Validation result for a single component.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether the component dimensions are valid.
        /// </summary>
        public bool IsValid { get; init; }

        /// <summary>
        /// Component identifier for reference.
        /// </summary>
        public string ComponentName { get; init; } = "";

        /// <summary>
        /// Description of the validation issue, if any.
        /// </summary>
        public string? Issue { get; init; }

        /// <summary>
        /// Recommended width based on pin positions.
        /// </summary>
        public double RecommendedWidth { get; init; }

        /// <summary>
        /// Recommended height based on pin positions.
        /// </summary>
        public double RecommendedHeight { get; init; }

        /// <summary>
        /// Current stated width from component.
        /// </summary>
        public double CurrentWidth { get; init; }

        /// <summary>
        /// Current stated height from component.
        /// </summary>
        public double CurrentHeight { get; init; }
    }

    private const double ToleranceMicrometers = 0.1;

    /// <summary>
    /// Validates a component's dimensions against its pin positions.
    /// Pins must lie within the component bounding box (0 to width, 0 to height).
    /// No additional margin is required - pins may be exactly at the component edges.
    /// </summary>
    /// <param name="component">Component to validate.</param>
    /// <returns>Validation result with recommended dimensions if invalid.</returns>
    public ValidationResult Validate(Component component)
    {
        if (component.PhysicalPins.Count == 0)
        {
            return new ValidationResult
            {
                IsValid = true,
                ComponentName = component.Identifier,
                CurrentWidth = component.WidthMicrometers,
                CurrentHeight = component.HeightMicrometers,
                RecommendedWidth = component.WidthMicrometers,
                RecommendedHeight = component.HeightMicrometers
            };
        }

        var bbox = CalculatePinBoundingBox(component.PhysicalPins);
        // Pins must fit within component bounds: 0 <= pin.x <= width, 0 <= pin.y <= height
        // No extra margin needed - pins at edges (e.g., width=80, pin.x=80) are valid
        var requiredWidth = bbox.MaxX - bbox.MinX;
        var requiredHeight = bbox.MaxY - bbox.MinY;

        bool widthValid = component.WidthMicrometers >= requiredWidth - ToleranceMicrometers;
        bool heightValid = component.HeightMicrometers >= requiredHeight - ToleranceMicrometers;

        if (widthValid && heightValid)
        {
            return new ValidationResult
            {
                IsValid = true,
                ComponentName = component.Identifier,
                CurrentWidth = component.WidthMicrometers,
                CurrentHeight = component.HeightMicrometers,
                RecommendedWidth = component.WidthMicrometers,
                RecommendedHeight = component.WidthMicrometers
            };
        }

        var issues = new List<string>();
        if (!widthValid)
            issues.Add($"Width {component.WidthMicrometers:F1}µm too small (needs ≥{requiredWidth:F1}µm)");
        if (!heightValid)
            issues.Add($"Height {component.HeightMicrometers:F1}µm too small (needs ≥{requiredHeight:F1}µm)");

        return new ValidationResult
        {
            IsValid = false,
            ComponentName = component.Identifier,
            Issue = string.Join("; ", issues),
            CurrentWidth = component.WidthMicrometers,
            CurrentHeight = component.HeightMicrometers,
            RecommendedWidth = Math.Ceiling(requiredWidth),
            RecommendedHeight = Math.Ceiling(requiredHeight)
        };
    }

    /// <summary>
    /// Calculates the axis-aligned bounding box that contains all physical pins.
    /// </summary>
    public (double MinX, double MaxX, double MinY, double MaxY) CalculatePinBoundingBox(
        List<PhysicalPin> pins)
    {
        if (pins.Count == 0)
            return (0, 0, 0, 0);

        double minX = double.MaxValue;
        double maxX = double.MinValue;
        double minY = double.MaxValue;
        double maxY = double.MinValue;

        foreach (var pin in pins)
        {
            minX = Math.Min(minX, pin.OffsetXMicrometers);
            maxX = Math.Max(maxX, pin.OffsetXMicrometers);
            minY = Math.Min(minY, pin.OffsetYMicrometers);
            maxY = Math.Max(maxY, pin.OffsetYMicrometers);
        }

        return (minX, maxX, minY, maxY);
    }

    /// <summary>
    /// Validates all components in a list and returns those with dimensional issues.
    /// </summary>
    public List<ValidationResult> ValidateAll(IEnumerable<Component> components)
    {
        return components
            .Select(Validate)
            .Where(r => !r.IsValid)
            .ToList();
    }
}

using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using Shouldly;
using System.Collections.ObjectModel;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Tests GDS coordinate accuracy for complex real-world layouts.
/// These tests use actual saved designs to verify coordinate mismatches reported by users.
/// </summary>
public class GdsComplexLayoutTests
{
    private readonly ObservableCollection<ComponentTemplate> _library;

    public GdsComplexLayoutTests()
    {
        _library = new ObservableCollection<ComponentTemplate>(TestPdkLoader.LoadAllTemplates());
    }

    /// <summary>
    /// Tests MMI 2x2 coordinate transformation at various positions.
    /// User reported that MMI 2x2 had "große Unterschiede" (large differences) in coordinates.
    /// </summary>
    [Fact]
    public void MMI2x2_VariousPositions_CoordinatesMustBeConsistent()
    {
        // Arrange
        var mmi2x2Template = _library.FirstOrDefault(t => t.Name == "MMI 2x2");
        if (mmi2x2Template == null) return; // Skip if template not found

        // Test multiple positions from user's actual layout
        var testPositions = new[]
        {
            (527.38417849983, 882.6371776759141),      // From group "test_1"
            (634.5353911975251, 1113.1375899313548),   // MMI 2x2_35
            (619.7178266784542, 1075.6375899313548),   // From another group
            (100.0, 100.0),                             // Simple position
            (0.0, 0.0)                                  // Origin
        };

        foreach (var (x, y) in testPositions)
        {
            var mmi = ComponentTemplates.CreateFromTemplate(mmi2x2Template, x, y);

            // Check all pins
            foreach (var pin in mmi.PhysicalPins)
            {
                var (globalX, globalY) = pin.GetAbsoluteNazcaPosition();

                // Manual calculation to verify
                double compNazcaX = x + mmi2x2Template.NazcaOriginOffsetX;
                double compNazcaY = -(y + mmi2x2Template.NazcaOriginOffsetY);
                double pinLocalNazcaY = mmi2x2Template.HeightMicrometers - pin.OffsetYMicrometers;

                double expectedGlobalX = compNazcaX + pin.OffsetXMicrometers;
                double expectedGlobalY = compNazcaY + pinLocalNazcaY;

                double xDev = Math.Abs(expectedGlobalX - globalX);
                double yDev = Math.Abs(expectedGlobalY - globalY);

                xDev.ShouldBeLessThan(0.01,
                    $"MMI 2x2 at ({x:F2},{y:F2}) pin '{pin.Name}': X deviation {xDev:F4} µm");
                yDev.ShouldBeLessThan(0.01,
                    $"MMI 2x2 at ({x:F2},{y:F2}) pin '{pin.Name}': Y deviation {yDev:F4} µm");
            }
        }
    }

    /// <summary>
    /// Tests MMI 1x2 coordinate transformation.
    /// User layout contained multiple MMI 1x2 instances with connections.
    /// </summary>
    [Fact]
    public void MMI1x2_VariousPositions_CoordinatesMustBeConsistent()
    {
        // Arrange
        var mmi1x2Template = _library.FirstOrDefault(t => t.Name == "MMI 1x2");
        if (mmi1x2Template == null) return;

        // Positions from user's layout
        var testPositions = new[]
        {
            (427.83984375, 424.5115966796875),        // MMI 1x2_1
            (286.8157069899595, 984.4066221943588),   // MMI 1x2_62
            (187.80700318017153, 1058.1375899313548), // MMI 1x2_61
            (236.12046856495712, 1203.567014454094)   // MMI 1x2_60
        };

        foreach (var (x, y) in testPositions)
        {
            var mmi = ComponentTemplates.CreateFromTemplate(mmi1x2Template, x, y);

            foreach (var pin in mmi.PhysicalPins)
            {
                var (globalX, globalY) = pin.GetAbsoluteNazcaPosition();

                // Manual calculation
                double compNazcaX = x + mmi1x2Template.NazcaOriginOffsetX;
                double compNazcaY = -(y + mmi1x2Template.NazcaOriginOffsetY);
                double pinLocalNazcaY = mmi1x2Template.HeightMicrometers - pin.OffsetYMicrometers;

                double expectedGlobalX = compNazcaX + pin.OffsetXMicrometers;
                double expectedGlobalY = compNazcaY + pinLocalNazcaY;

                double xDev = Math.Abs(expectedGlobalX - globalX);
                double yDev = Math.Abs(expectedGlobalY - globalY);

                xDev.ShouldBeLessThan(0.01,
                    $"MMI 1x2 at ({x:F2},{y:F2}) pin '{pin.Name}': X deviation {xDev:F4} µm");
                yDev.ShouldBeLessThan(0.01,
                    $"MMI 1x2 at ({x:F2},{y:F2}) pin '{pin.Name}': Y deviation {yDev:F4} µm");
            }
        }
    }

    /// <summary>
    /// Tests Grating Coupler coordinates from the complex layout.
    /// </summary>
    [Fact]
    public void GratingCoupler_VariousPositions_CoordinatesMustBeConsistent()
    {
        // Arrange
        var gcTemplate = _library.FirstOrDefault(t => t.Name == "Grating Coupler");
        if (gcTemplate == null) return;

        var testPositions = new[]
        {
            (168.04080214819527, 361.6714200638443),  // From user layout
            (762.9330138870511, 953.2440038028168),    // Grating Coupler TE 1550_31
            (733.9110747950451, 886.3595531826309)     // Grating Coupler TE 1550_32
        };

        foreach (var (x, y) in testPositions)
        {
            var gc = ComponentTemplates.CreateFromTemplate(gcTemplate, x, y);

            foreach (var pin in gc.PhysicalPins)
            {
                var (globalX, globalY) = pin.GetAbsoluteNazcaPosition();

                // Manual calculation for stub-based (legacy) component:
                // The stub cell origin is at the bottom-left (Nazca y=0 = editor y=HeightMicrometers).
                // NazcaOriginOffsetY applies to real PDK cells; legacy stubs use HeightMicrometers.
                double compNazcaX = x + gcTemplate.NazcaOriginOffsetX;
                double compNazcaY = -(y + gcTemplate.HeightMicrometers);
                double pinLocalNazcaY = gcTemplate.HeightMicrometers - pin.OffsetYMicrometers;

                double expectedGlobalX = compNazcaX + pin.OffsetXMicrometers;
                double expectedGlobalY = compNazcaY + pinLocalNazcaY;

                double xDev = Math.Abs(expectedGlobalX - globalX);
                double yDev = Math.Abs(expectedGlobalY - globalY);

                xDev.ShouldBeLessThan(0.01,
                    $"GC at ({x:F2},{y:F2}) pin '{pin.Name}': X deviation {xDev:F4} µm");
                yDev.ShouldBeLessThan(0.01,
                    $"GC at ({x:F2},{y:F2}) pin '{pin.Name}': Y deviation {yDev:F4} µm");
            }
        }
    }

    /// <summary>
    /// Comprehensive test for all component types at all positions from the user's complex layout.
    /// This reproduces the exact scenario where "große Unterschiede" were observed.
    /// </summary>
    [Fact]
    public void ComplexUserLayout_AllComponents_AllPositions_MustMatchExactly()
    {
        // This test verifies EVERY component position from the user's provided JSON
        var allPositions = new (string templateName, double x, double y)[]
        {
            // MMI 1x2 instances
            ("MMI 1x2", 427.83984375, 424.5115966796875),
            ("MMI 1x2", 286.8157069899595, 984.4066221943588),
            ("MMI 1x2", 187.80700318017153, 1058.1375899313548),
            ("MMI 1x2", 236.12046856495712, 1203.567014454094),

            // MMI 2x2 instances (the problematic ones)
            ("MMI 2x2", 527.38417849983, 882.6371776759141),
            ("MMI 2x2", 538.3940504906809, 764.3371913019082),
            ("MMI 2x2", 542.2017430189004, 676.7757658158281),
            ("MMI 2x2", 619.7178266784542, 1075.6375899313548),
            ("MMI 2x2", 630.7276986693034, 1163.199015417435),
            ("MMI 2x2", 634.5353911975251, 1075.6375899313548),

            // Grating Couplers
            ("Grating Coupler", 168.04080214819527, 361.6714200638443),
            ("Grating Coupler", 762.9330138870511, 953.2440038028168),
            ("Grating Coupler", 733.9110747950451, 886.3595531826309),
            ("Grating Coupler", 855.2666620656738, 1367.1058279183435),
            ("Grating Coupler", 826.2447229736683, 1300.2213772981577)
        };

        int failureCount = 0;
        var failureDetails = new List<string>();

        foreach (var (templateName, x, y) in allPositions)
        {
            var template = _library.FirstOrDefault(t => t.Name == templateName);
            if (template == null) continue;

            var component = ComponentTemplates.CreateFromTemplate(template, x, y);

            foreach (var pin in component.PhysicalPins)
            {
                var (globalX, globalY) = pin.GetAbsoluteNazcaPosition();

                // Calculate expected position.
                // "Grating Coupler" has no PDK function name → legacy stub with origin at bottom.
                // Legacy fallback: CalculateOriginOffset returns (0, HeightMicrometers).
                bool isPdkFunc = !string.IsNullOrEmpty(template.NazcaFunctionName) &&
                    (template.NazcaFunctionName.StartsWith("ebeam_", StringComparison.OrdinalIgnoreCase) ||
                     template.NazcaFunctionName.StartsWith("demo_pdk.", StringComparison.OrdinalIgnoreCase));
                double effectiveOriginY = isPdkFunc ? template.NazcaOriginOffsetY : template.HeightMicrometers;
                double compNazcaX = x + template.NazcaOriginOffsetX;
                double compNazcaY = -(y + effectiveOriginY);
                double pinLocalNazcaY = template.HeightMicrometers - pin.OffsetYMicrometers;

                double expectedGlobalX = compNazcaX + pin.OffsetXMicrometers;
                double expectedGlobalY = compNazcaY + pinLocalNazcaY;

                double xDev = Math.Abs(expectedGlobalX - globalX);
                double yDev = Math.Abs(expectedGlobalY - globalY);

                if (xDev >= 0.01 || yDev >= 0.01)
                {
                    failureCount++;
                    failureDetails.Add(
                        $"{templateName} at ({x:F1},{y:F1}) pin '{pin.Name}': ΔX={xDev:F4}, ΔY={yDev:F4}");
                }
            }
        }

        if (failureCount > 0)
        {
            var message = $"Found {failureCount} pin coordinate mismatches:\n" +
                         string.Join("\n", failureDetails.Take(10));
            Assert.Fail(message);
        }
    }
}

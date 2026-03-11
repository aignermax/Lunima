using CAP.Avalonia.ViewModels;
using CAP_Core.Components;
using CAP_Core.Components.FormulaReading;
using CAP_Core.LightCalculation;
using CAP_Core.Tiles;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for ComponentDimensionDiagnosticsViewModel.
/// Tests that the diagnostic panel correctly reports component dimensions
/// from the Core layer through the ViewModel layer.
/// Addresses issue #69: MMI 2x2 incorrect length in GDS export.
/// </summary>
public class ComponentDimensionDiagnosticsIntegrationTests
{
    [Fact]
    public void RefreshDiagnostics_WithSingleComponent_ShowsCorrectDimensions()
    {
        // Arrange - Create a canvas with one component
        var canvas = new DesignCanvasViewModel();
        var diagnostics = new ComponentDimensionDiagnosticsViewModel(canvas);

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "ebeam_test_mmi",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: "TestMMI",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>()
        );

        component.WidthMicrometers = 120;
        component.HeightMicrometers = 50;
        component.PhysicalX = 100;
        component.PhysicalY = 200;
        component.RotationDegrees = 0;

        canvas.AddComponent(component, "TestMMI");

        // Act
        diagnostics.RefreshDiagnosticsCommand.Execute(null);

        // Assert
        diagnostics.ComponentDimensions.Count.ShouldBe(1);
        var dim = diagnostics.ComponentDimensions[0];
        dim.Identifier.ShouldBe("TestMMI");
        dim.WidthMicrometers.ShouldBe(120);
        dim.HeightMicrometers.ShouldBe(50);
        dim.PhysicalX.ShouldBe(100);
        dim.PhysicalY.ShouldBe(200);
        dim.RotationDegrees.ShouldBe(0);

        // Verify text output contains key information
        diagnostics.DiagnosticsText.ShouldContain("TestMMI");
        diagnostics.DiagnosticsText.ShouldContain("120.00 × 50.00 µm");
        diagnostics.DiagnosticsText.ShouldContain("(100.00, 200.00)");
    }

    [Fact]
    public void RefreshDiagnostics_WithRotatedComponent_ShowsRotation()
    {
        var canvas = new DesignCanvasViewModel();
        var diagnostics = new ComponentDimensionDiagnosticsViewModel(canvas);

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "ebeam_rotated",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: "Rotated",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>()
        );

        component.WidthMicrometers = 100;
        component.HeightMicrometers = 50;
        component.RotationDegrees = 90;

        canvas.AddComponent(component, "Rotated");

        diagnostics.RefreshDiagnosticsCommand.Execute(null);

        diagnostics.ComponentDimensions[0].RotationDegrees.ShouldBe(90);
        diagnostics.DiagnosticsText.ShouldContain("90.0°");
    }

    [Fact]
    public void RefreshDiagnostics_WithMultipleComponents_ShowsAll()
    {
        var canvas = new DesignCanvasViewModel();
        var diagnostics = new ComponentDimensionDiagnosticsViewModel(canvas);

        // Add three components with different dimensions
        for (int i = 0; i < 3; i++)
        {
            var parts = new Part[1, 1];
            parts[0, 0] = new Part(new List<Pin>());

            var component = new Component(
                laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
                sliders: new List<Slider>(),
                nazcaFunctionName: $"ebeam_comp_{i}",
                nazcaFunctionParams: "",
                parts: parts,
                typeNumber: 0,
                identifier: $"Comp{i}",
                rotationCounterClock: DiscreteRotation.R0,
                physicalPins: new List<PhysicalPin>()
            );

            component.WidthMicrometers = 100 + i * 20;
            component.HeightMicrometers = 50 + i * 10;
            canvas.AddComponent(component, $"Comp{i}");
        }

        diagnostics.RefreshDiagnosticsCommand.Execute(null);

        diagnostics.ComponentDimensions.Count.ShouldBe(3);
        diagnostics.DiagnosticsText.ShouldContain("Total Components: 3");

        // Verify each component appears
        for (int i = 0; i < 3; i++)
        {
            diagnostics.DiagnosticsText.ShouldContain($"Comp{i}");
            diagnostics.DiagnosticsText.ShouldContain($"{100 + i * 20}.00 × {50 + i * 10}.00 µm");
        }
    }

    [Fact]
    public void RefreshDiagnostics_WithPhysicalPins_ShowsPinCount()
    {
        var canvas = new DesignCanvasViewModel();
        var diagnostics = new ComponentDimensionDiagnosticsViewModel(canvas);

        var parts = new Part[1, 1];
        parts[0, 0] = new Part(new List<Pin>());

        var component = new Component(
            laserWaveLengthToSMatrixMap: new Dictionary<int, SMatrix>(),
            sliders: new List<Slider>(),
            nazcaFunctionName: "ebeam_mmi",
            nazcaFunctionParams: "",
            parts: parts,
            typeNumber: 0,
            identifier: "MMI",
            rotationCounterClock: DiscreteRotation.R0,
            physicalPins: new List<PhysicalPin>
            {
                new PhysicalPin { Name = "a0", OffsetXMicrometers = 0, OffsetYMicrometers = 12.5, AngleDegrees = 180 },
                new PhysicalPin { Name = "a1", OffsetXMicrometers = 0, OffsetYMicrometers = 37.5, AngleDegrees = 180 },
                new PhysicalPin { Name = "b0", OffsetXMicrometers = 120, OffsetYMicrometers = 12.5, AngleDegrees = 0 },
                new PhysicalPin { Name = "b1", OffsetXMicrometers = 120, OffsetYMicrometers = 37.5, AngleDegrees = 0 }
            }
        );

        component.WidthMicrometers = 120;
        component.HeightMicrometers = 50;
        canvas.AddComponent(component, "MMI");

        diagnostics.RefreshDiagnosticsCommand.Execute(null);

        diagnostics.ComponentDimensions[0].PinCount.ShouldBe(4);
        diagnostics.DiagnosticsText.ShouldContain("Pins: 4");
    }

    [Fact]
    public void RefreshDiagnostics_EmptyCanvas_ShowsZeroComponents()
    {
        var canvas = new DesignCanvasViewModel();
        var diagnostics = new ComponentDimensionDiagnosticsViewModel(canvas);

        diagnostics.RefreshDiagnosticsCommand.Execute(null);

        diagnostics.ComponentDimensions.Count.ShouldBe(0);
        diagnostics.DiagnosticsText.ShouldContain("Total Components: 0");
    }
}

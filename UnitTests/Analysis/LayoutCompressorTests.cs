using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Unit tests for the LayoutCompressor class.
/// </summary>
public class LayoutCompressorTests
{
    [Fact]
    public void CompressLayout_WithNoComponents_ReturnsEmptyDictionary()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var components = new List<Component>();
        var connections = new List<WaveguideConnection>();

        // Act
        var result = compressor.CompressLayout(components, connections);

        // Assert
        result.ShouldBeEmpty();
    }

    [Fact]
    public void CompressLayout_WithSingleComponent_PositionsNearOrigin()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = 1000;
        component.PhysicalY = 1000;

        var components = new List<Component> { component };
        var connections = new List<WaveguideConnection>();

        // Act
        var result = compressor.CompressLayout(components, connections);

        // Assert
        result.ShouldNotBeEmpty();
        result[component].X.ShouldBeLessThan(200); // Should be near margin (50µm)
        result[component].Y.ShouldBeLessThan(200);
    }

    [Fact]
    public void CompressLayout_WithTwoConnectedComponents_ReducesBoundingBox()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 1000; // Far apart
        comp2.PhysicalY = 1000;

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection> { connection };

        // Calculate original bounds
        var originalBounds = compressor.CalculateBoundingBox(components);

        // Act
        var result = compressor.CompressLayout(components, connections);

        // Assert
        var newBounds = compressor.CalculateBoundingBox(components);

        // Bounding box should be smaller after compression
        newBounds.width.ShouldBeLessThan(originalBounds.width);
        newBounds.height.ShouldBeLessThan(originalBounds.height);
        newBounds.area.ShouldBeLessThan(originalBounds.area);
    }

    [Fact]
    public void CompressLayout_WithLockedComponent_DoesNotMoveLockedComponent()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp1.IsLocked = true; // Lock first component

        comp2.PhysicalX = 1000;
        comp2.PhysicalY = 1000;

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection> { connection };

        var originalComp1X = comp1.PhysicalX;
        var originalComp1Y = comp1.PhysicalY;

        // Act
        var result = compressor.CompressLayout(components, connections);

        // Assert
        comp1.PhysicalX.ShouldBe(originalComp1X); // Locked component unchanged
        comp1.PhysicalY.ShouldBe(originalComp1Y);
        comp2.PhysicalX.ShouldNotBe(1000.0); // Unlocked component moved
    }

    [Fact]
    public void CompressLayout_WithMultipleComponents_MaintainsMinimumSpacing()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var comp3 = TestComponentFactory.CreateBasicComponent();

        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;
        comp3.PhysicalX = 0;
        comp3.PhysicalY = 500;

        var components = new List<Component> { comp1, comp2, comp3 };
        var connections = new List<WaveguideConnection>();

        // Act
        var result = compressor.CompressLayout(components, connections);

        // Assert
        // Check that no two components overlap
        foreach (var c1 in components)
        {
            foreach (var c2 in components)
            {
                if (c1 == c2) continue;

                double dx = c2.PhysicalX - c1.PhysicalX;
                double dy = c2.PhysicalY - c1.PhysicalY;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                // Minimum spacing should be maintained
                distance.ShouldBeGreaterThan(10.0); // MinComponentSpacing = 20µm, allowing some tolerance
            }
        }
    }

    [Fact]
    public void CalculateBoundingBox_WithEmptyList_ReturnsZero()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var components = new List<Component>();

        // Act
        var result = compressor.CalculateBoundingBox(components);

        // Assert
        result.width.ShouldBe(0);
        result.height.ShouldBe(0);
        result.area.ShouldBe(0);
    }

    [Fact]
    public void CalculateBoundingBox_WithSingleComponent_ReturnsComponentSize()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var component = TestComponentFactory.CreateBasicComponent();
        component.PhysicalX = 100;
        component.PhysicalY = 200;
        component.WidthMicrometers = 250;
        component.HeightMicrometers = 300;

        var components = new List<Component> { component };

        // Act
        var result = compressor.CalculateBoundingBox(components);

        // Assert
        result.minX.ShouldBe(100);
        result.minY.ShouldBe(200);
        result.maxX.ShouldBe(350); // 100 + 250
        result.maxY.ShouldBe(500); // 200 + 300
        result.width.ShouldBe(250);
        result.height.ShouldBe(300);
        result.area.ShouldBe(75000); // 250 * 300
    }

    [Fact]
    public void CalculateBoundingBox_WithMultipleComponents_CalculatesCorrectBounds()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp1.WidthMicrometers = 100;
        comp1.HeightMicrometers = 100;

        comp2.PhysicalX = 200;
        comp2.PhysicalY = 300;
        comp2.WidthMicrometers = 150;
        comp2.HeightMicrometers = 200;

        var components = new List<Component> { comp1, comp2 };

        // Act
        var result = compressor.CalculateBoundingBox(components);

        // Assert
        result.minX.ShouldBe(0);
        result.minY.ShouldBe(0);
        result.maxX.ShouldBe(350); // 200 + 150
        result.maxY.ShouldBe(500); // 300 + 200
        result.width.ShouldBe(350);
        result.height.ShouldBe(500);
    }

    [Fact]
    public void CompressLayout_WithAllLockedComponents_ReturnsOriginalPositions()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        comp1.PhysicalX = 100;
        comp1.PhysicalY = 200;
        comp1.IsLocked = true;

        comp2.PhysicalX = 500;
        comp2.PhysicalY = 600;
        comp2.IsLocked = true;

        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection>();

        // Act
        var result = compressor.CompressLayout(components, connections);

        // Assert
        result[comp1].X.ShouldBe(100);
        result[comp1].Y.ShouldBe(200);
        result[comp2].X.ShouldBe(500);
        result[comp2].Y.ShouldBe(600);
    }
}

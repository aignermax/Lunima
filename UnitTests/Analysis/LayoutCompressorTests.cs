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

    [Fact]
    public void CompressLayout_ActuallyCompresses_ComponentsGetCloserTogether()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        // Start components far apart
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 1000;
        comp2.PhysicalY = 1000;

        // Calculate original distance
        double originalDistance = Math.Sqrt(
            Math.Pow(comp2.PhysicalX - comp1.PhysicalX, 2) +
            Math.Pow(comp2.PhysicalY - comp1.PhysicalY, 2));

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection> { connection };

        // Act
        var result = compressor.CompressLayout(components, connections);

        // Assert
        // Calculate new distance
        double newDistance = Math.Sqrt(
            Math.Pow(comp2.PhysicalX - comp1.PhysicalX, 2) +
            Math.Pow(comp2.PhysicalY - comp1.PhysicalY, 2));

        // The core bug fix: components should get CLOSER, not farther apart
        newDistance.ShouldBeLessThan(originalDistance,
            $"Compression should reduce distance from {originalDistance:F2}µm to less, but got {newDistance:F2}µm");
    }

    [Fact]
    public void CompressLayout_WithThreeComponentsInLine_CompressesTogether()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        var comp3 = TestComponentFactory.CreateBasicComponent();

        // Place in a line with large gaps
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 800;
        comp2.PhysicalY = 0;
        comp3.PhysicalX = 1600;
        comp3.PhysicalY = 0;

        var conn1 = TestComponentFactory.CreateConnection(comp1, comp2);
        var conn2 = TestComponentFactory.CreateConnection(comp2, comp3);

        var components = new List<Component> { comp1, comp2, comp3 };
        var connections = new List<WaveguideConnection> { conn1, conn2 };

        // Calculate original total span
        double originalSpan = comp3.PhysicalX - comp1.PhysicalX;

        // Act
        var result = compressor.CompressLayout(components, connections);

        // Assert
        // Calculate new span
        double newSpan = Math.Abs(comp3.PhysicalX - comp1.PhysicalX);

        // Layout should compress - span should be smaller
        newSpan.ShouldBeLessThan(originalSpan,
            $"Compression should reduce span from {originalSpan:F2}µm, but got {newSpan:F2}µm");
    }

    [Fact]
    public void CompressLayout_WithProgressCallback_InvokesCallback()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 1000;
        comp2.PhysicalY = 1000;

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection> { connection };

        var callbackIterations = new List<int>();

        // Act
        compressor.CompressLayout(components, connections, iteration =>
        {
            callbackIterations.Add(iteration);
        });

        // Assert
        // Progress callback should have been called at least once
        callbackIterations.Count.ShouldBeGreaterThanOrEqualTo(1,
            "Progress callback should be called at least once during compression");

        // First callback should be at iteration 0
        callbackIterations.First().ShouldBe(0);

        // Iterations should be in ascending order (if multiple callbacks)
        for (int i = 1; i < callbackIterations.Count; i++)
        {
            callbackIterations[i].ShouldBeGreaterThanOrEqualTo(callbackIterations[i - 1]);
        }
    }

    [Fact]
    public void CompressLayout_WithoutProgressCallback_WorksNormally()
    {
        // Arrange
        var compressor = new LayoutCompressor();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();

        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 1000;
        comp2.PhysicalY = 1000;

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        var components = new List<Component> { comp1, comp2 };
        var connections = new List<WaveguideConnection> { connection };

        // Act - no callback provided
        var result = compressor.CompressLayout(components, connections, null);

        // Assert
        result.ShouldNotBeEmpty();
        result.Count.ShouldBe(2);
    }
}

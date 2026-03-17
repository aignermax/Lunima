using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.LightCalculation;

/// <summary>
/// Unit tests for ComponentGroupSMatrixBuilder.
/// Verifies that S-Matrices are correctly computed for ComponentGroups
/// by combining child component matrices and frozen internal paths.
/// </summary>
public class ComponentGroupSMatrixBuilderTests
{
    private readonly ComponentGroupSMatrixBuilder _builder;

    public ComponentGroupSMatrixBuilderTests()
    {
        _builder = new ComponentGroupSMatrixBuilder();
    }

    [Fact]
    public void BuildGroupSMatrix_EmptyGroup_ReturnsNull()
    {
        // Arrange
        var group = new ComponentGroup("Empty");

        // Act
        var result = _builder.BuildGroupSMatrixAllWavelengths(group);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void BuildGroupSMatrix_GroupWithNoExternalPins_ReturnsNull()
    {
        // Arrange
        var group = new ComponentGroup("NoExternal");
        var child = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(child);

        // Act
        var result = _builder.BuildGroupSMatrixAllWavelengths(group);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void BuildGroupSMatrix_SingleChildWithExternalPins_ComputesMatrix()
    {
        // Arrange
        var group = new ComponentGroup("SingleChild");
        var child = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(child);

        // Add external pins exposing child's pins
        var pin1 = child.PhysicalPins[0];
        var pin2 = child.PhysicalPins[1];

        group.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = pin1,
            RelativeX = 0,
            RelativeY = 0,
            AngleDegrees = 0
        });

        group.AddExternalPin(new GroupPin
        {
            Name = "Out",
            InternalPin = pin2,
            RelativeX = 10,
            RelativeY = 0,
            AngleDegrees = 180
        });

        // Act
        var result = _builder.BuildGroupSMatrixAllWavelengths(group);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThan(0);

        // Verify that matrix contains external pin IDs
        var firstMatrix = result.Values.First();
        firstMatrix.ShouldNotBeNull();
        firstMatrix.PinReference.ShouldContainKey(pin1.LogicalPin.IDInFlow);
        firstMatrix.PinReference.ShouldContainKey(pin2.LogicalPin.IDOutFlow);
    }

    [Fact]
    public void BuildGroupSMatrix_TwoChildrenWithInternalPath_ComputesMatrix()
    {
        // Arrange
        var group = new ComponentGroup("TwoChildren");
        var child1 = TestComponentFactory.CreateSimpleTwoPortComponent();
        var child2 = TestComponentFactory.CreateSimpleTwoPortComponent();

        child1.PhysicalX = 0;
        child1.PhysicalY = 0;
        child2.PhysicalX = 20;
        child2.PhysicalY = 0;

        group.AddChild(child1);
        group.AddChild(child2);

        // Create frozen path connecting child1.pin[1] to child2.pin[0]
        var path = new FrozenWaveguidePath
        {
            StartPin = child1.PhysicalPins[1],
            EndPin = child2.PhysicalPins[0],
            Path = new RoutedPath
            {
                Segments = new List<PathSegment>
                {
                    new StraightSegment(10, 0, 20, 0, 0)
                }
            }
        };
        group.AddInternalPath(path);

        // Add external pins at the ends
        group.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = child1.PhysicalPins[0],
            RelativeX = 0,
            RelativeY = 0
        });

        group.AddExternalPin(new GroupPin
        {
            Name = "Out",
            InternalPin = child2.PhysicalPins[1],
            RelativeX = 30,
            RelativeY = 0
        });

        // Act
        var result = _builder.BuildGroupSMatrixAllWavelengths(group);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThan(0);

        // Verify matrix includes both external pins
        var matrix = result.Values.First();
        matrix.PinReference.Count.ShouldBe(4); // 2 external pins × 2 (in/out flow)
    }

    [Fact]
    public void BuildGroupSMatrix_SupportsMultipleWavelengths()
    {
        // Arrange
        var group = new ComponentGroup("MultiWavelength");
        var child = TestComponentFactory.CreateStraightWaveGuide();
        group.AddChild(child);

        group.AddExternalPin(new GroupPin
        {
            Name = "A",
            InternalPin = child.PhysicalPins[0]
        });

        group.AddExternalPin(new GroupPin
        {
            Name = "B",
            InternalPin = child.PhysicalPins[1]
        });

        // Act
        var result = _builder.BuildGroupSMatrixAllWavelengths(group);

        // Assert
        result.ShouldNotBeNull();

        // Should have matrices for red, green, and blue wavelengths
        result.ShouldContainKey(StandardWaveLengths.RedNM);
        result.ShouldContainKey(StandardWaveLengths.GreenNM);
        result.ShouldContainKey(StandardWaveLengths.BlueNM);
    }

    [Fact]
    public void BuildGroupSMatrix_NearestWavelengthFallback_WorksCorrectly()
    {
        // Arrange
        var group = new ComponentGroup("WavelengthFallback");
        var child = TestComponentFactory.CreateStraightWaveGuide(); // Has 650, 550, 450 nm
        group.AddChild(child);

        group.AddExternalPin(new GroupPin
        {
            Name = "A",
            InternalPin = child.PhysicalPins[0]
        });

        group.AddExternalPin(new GroupPin
        {
            Name = "B",
            InternalPin = child.PhysicalPins[1]
        });

        // Act - Request a wavelength that doesn't exactly exist
        var result = _builder.BuildGroupSMatrix(group, 655); // Close to 650nm

        // Assert
        result.ShouldNotBeNull();
        result.ShouldContainKey(655);
    }

    [Fact]
    public void BuildGroupSMatrix_NestedGroup_ComputesRecursively()
    {
        // Arrange
        var outerGroup = new ComponentGroup("Outer");
        var innerGroup = new ComponentGroup("Inner");

        var component = TestComponentFactory.CreateSimpleTwoPortComponent();
        innerGroup.AddChild(component);

        innerGroup.AddExternalPin(new GroupPin
        {
            Name = "In",
            InternalPin = component.PhysicalPins[0]
        });

        innerGroup.AddExternalPin(new GroupPin
        {
            Name = "Out",
            InternalPin = component.PhysicalPins[1]
        });

        // Add inner group to outer group
        outerGroup.AddChild(innerGroup);

        // Outer group exposes inner group's pins
        outerGroup.AddExternalPin(new GroupPin
        {
            Name = "Input",
            InternalPin = component.PhysicalPins[0]
        });

        outerGroup.AddExternalPin(new GroupPin
        {
            Name = "Output",
            InternalPin = component.PhysicalPins[1]
        });

        // Act
        var result = _builder.BuildGroupSMatrixAllWavelengths(outerGroup);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ComponentGroup_InvalidatesSMatrix_WhenChildAdded()
    {
        // Arrange
        var group = new ComponentGroup("TestInvalidation");
        var child1 = TestComponentFactory.CreateSimpleTwoPortComponent();

        group.AddChild(child1);
        group.AddExternalPin(new GroupPin
        {
            Name = "A",
            InternalPin = child1.PhysicalPins[0]
        });

        group.ComputeSMatrix();
        int initialCount = group.WaveLengthToSMatrixMap.Count;
        initialCount.ShouldBeGreaterThan(0);

        // Act - Add another child, should invalidate matrix
        var child2 = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(child2);

        // Assert - Matrix should be cleared
        group.WaveLengthToSMatrixMap.Count.ShouldBe(0);
    }

    [Fact]
    public void ComponentGroup_EnsureSMatrixComputed_ComputesWhenNeeded()
    {
        // Arrange
        var group = new ComponentGroup("EnsureTest");
        var child = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(child);

        group.AddExternalPin(new GroupPin
        {
            Name = "A",
            InternalPin = child.PhysicalPins[0]
        });

        // Act
        group.EnsureSMatrixComputed();

        // Assert
        group.WaveLengthToSMatrixMap.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ComponentGroup_EnsureSMatrixComputed_DoesNotRecomputeWhenValid()
    {
        // Arrange
        var group = new ComponentGroup("NoRecompute");
        var child = TestComponentFactory.CreateSimpleTwoPortComponent();
        group.AddChild(child);

        group.AddExternalPin(new GroupPin
        {
            Name = "A",
            InternalPin = child.PhysicalPins[0]
        });

        group.ComputeSMatrix();
        var firstMatrix = group.WaveLengthToSMatrixMap.Values.First();

        // Act
        group.EnsureSMatrixComputed();

        // Assert - Should be same reference (not recomputed)
        var secondMatrix = group.WaveLengthToSMatrixMap.Values.First();
        ReferenceEquals(firstMatrix, secondMatrix).ShouldBeTrue();
    }
}

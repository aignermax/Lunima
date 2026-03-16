using CAP_Core.Components.Core;
using CAP_Core.LightCalculation;
using Shouldly;
using System.ComponentModel;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Unit tests for ComponentGroup property change notifications.
/// Tests that GroupName and Description changes raise PropertyChanged events.
/// </summary>
public class ComponentGroupPropertyChangeTests
{
    [Fact]
    public void GroupName_WhenChanged_RaisesPropertyChangedEvent()
    {
        // Arrange
        var group = new ComponentGroup("Original Name");
        bool eventRaised = false;
        string? changedPropertyName = null;

        group.PropertyChanged += (sender, e) =>
        {
            eventRaised = true;
            changedPropertyName = e.PropertyName;
        };

        // Act
        group.GroupName = "New Name";

        // Assert
        eventRaised.ShouldBeTrue();
        changedPropertyName.ShouldBe(nameof(ComponentGroup.GroupName));
        group.GroupName.ShouldBe("New Name");
    }

    [Fact]
    public void GroupName_WhenSetToSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var group = new ComponentGroup("Same Name");
        int eventCount = 0;

        group.PropertyChanged += (sender, e) =>
        {
            eventCount++;
        };

        // Act
        group.GroupName = "Same Name";

        // Assert
        eventCount.ShouldBe(0);
    }

    [Fact]
    public void Description_WhenChanged_RaisesPropertyChangedEvent()
    {
        // Arrange
        var group = new ComponentGroup("Test Group");
        bool eventRaised = false;
        string? changedPropertyName = null;

        group.PropertyChanged += (sender, e) =>
        {
            eventRaised = true;
            changedPropertyName = e.PropertyName;
        };

        // Act
        group.Description = "New Description";

        // Assert
        eventRaised.ShouldBeTrue();
        changedPropertyName.ShouldBe(nameof(ComponentGroup.Description));
        group.Description.ShouldBe("New Description");
    }

    [Fact]
    public void Description_WhenSetToSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var group = new ComponentGroup("Test Group")
        {
            Description = "Same Description"
        };
        int eventCount = 0;

        group.PropertyChanged += (sender, e) =>
        {
            eventCount++;
        };

        // Act
        group.Description = "Same Description";

        // Assert
        eventCount.ShouldBe(0);
    }

    [Fact]
    public void GroupName_MultipleChanges_RaisesEventEachTime()
    {
        // Arrange
        var group = new ComponentGroup("Initial");
        var changedNames = new List<string>();

        group.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(ComponentGroup.GroupName))
            {
                changedNames.Add(group.GroupName);
            }
        };

        // Act
        group.GroupName = "First";
        group.GroupName = "Second";
        group.GroupName = "Third";

        // Assert
        changedNames.Count.ShouldBe(3);
        changedNames[0].ShouldBe("First");
        changedNames[1].ShouldBe("Second");
        changedNames[2].ShouldBe("Third");
    }

    [Fact]
    public void ComponentGroup_ImplementsINotifyPropertyChanged()
    {
        // Arrange & Act
        var group = new ComponentGroup("Test");

        // Assert
        (group is INotifyPropertyChanged).ShouldBeTrue();
    }

    [Fact]
    public void GroupName_UpdatesLabelBounds_WhenChanged()
    {
        // Arrange
        var group = new ComponentGroup("Short");

        // Add a child to establish initial bounds
        var child = CreateTestComponent(0, 0);
        group.AddChild(child);

        var initialLabelBounds = group.LabelBounds;

        // Act
        group.GroupName = "Much Longer Group Name";

        // Assert
        // Label width should increase due to longer name
        group.LabelBounds.Width.ShouldBeGreaterThan(initialLabelBounds.Width);
    }

    /// <summary>
    /// Creates a simple test component for use in groups.
    /// </summary>
    private CAP_Core.Components.Core.Component CreateTestComponent(double x, double y)
    {
        return new CAP_Core.Components.Core.Component(
            new Dictionary<int, SMatrix>(),
            new List<Slider>(),
            "test_component",
            "",
            new Part[1, 1] { { new Part() } },
            -1,
            $"comp_{Guid.NewGuid():N}",
            DiscreteRotation.R0,
            new List<PhysicalPin>
            {
                new PhysicalPin
                {
                    Name = "a0",
                    OffsetXMicrometers = 0,
                    OffsetYMicrometers = 0,
                    AngleDegrees = 180
                }
            })
        {
            PhysicalX = x,
            PhysicalY = y,
            WidthMicrometers = 50,
            HeightMicrometers = 30
        };
    }
}

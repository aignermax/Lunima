using Avalonia.Controls;
using CAP.Avalonia.Behaviors;
using Shouldly;

namespace UnitTests.Behaviors;

/// <summary>
/// Unit tests for ScrollPositionBehavior.
/// Tests MVVM-compliant scroll position binding.
/// Note: Full integration testing of scroll behavior requires UI framework initialization.
/// These tests validate the attached property mechanism.
/// </summary>
public class ScrollPositionBehaviorTests
{
    [Fact]
    public void VerticalOffset_DefaultsToZero()
    {
        var scrollViewer = new ScrollViewer();

        var offset = ScrollPositionBehavior.GetVerticalOffset(scrollViewer);

        offset.ShouldBe(0.0);
    }

    [Fact]
    public void SetVerticalOffset_UpdatesProperty()
    {
        var scrollViewer = new ScrollViewer();

        ScrollPositionBehavior.SetVerticalOffset(scrollViewer, 100.5);

        var offset = ScrollPositionBehavior.GetVerticalOffset(scrollViewer);
        offset.ShouldBe(100.5);
    }

    [Fact]
    public void GetVerticalOffset_ReturnsCorrectValue()
    {
        var scrollViewer = new ScrollViewer();

        ScrollPositionBehavior.SetVerticalOffset(scrollViewer, 250.75);

        var result = ScrollPositionBehavior.GetVerticalOffset(scrollViewer);
        result.ShouldBe(250.75);
    }

    [Fact]
    public void VerticalOffset_SupportsMultipleUpdates()
    {
        var scrollViewer = new ScrollViewer();

        // Multiple updates to the attached property should work
        ScrollPositionBehavior.SetVerticalOffset(scrollViewer, 50.0);
        ScrollPositionBehavior.GetVerticalOffset(scrollViewer).ShouldBe(50.0);

        ScrollPositionBehavior.SetVerticalOffset(scrollViewer, 100.0);
        ScrollPositionBehavior.GetVerticalOffset(scrollViewer).ShouldBe(100.0);

        ScrollPositionBehavior.SetVerticalOffset(scrollViewer, 200.0);
        ScrollPositionBehavior.GetVerticalOffset(scrollViewer).ShouldBe(200.0);
    }

    [Fact]
    public void AttachedProperty_IsRegistered()
    {
        // Verify the attached property is properly registered
        var property = ScrollPositionBehavior.VerticalOffsetProperty;

        property.ShouldNotBeNull();
        property.Name.ShouldBe("VerticalOffset");
        property.PropertyType.ShouldBe(typeof(double));
    }

    [Fact]
    public void AttachedProperty_DefaultValueIsZero()
    {
        var property = ScrollPositionBehavior.VerticalOffsetProperty;

        property.GetDefaultValue(typeof(ScrollViewer)).ShouldBe(0.0);
    }
}

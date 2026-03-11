using CAP.Avalonia.ViewModels.Canvas;
using Shouldly;

namespace UnitTests.ViewModels;

public class GridSnapSettingsTests
{
    [Fact]
    public void IsEnabled_DefaultsToFalse()
    {
        var settings = new GridSnapSettings();
        settings.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void GridSizeMicrometers_DefaultsTo50()
    {
        var settings = new GridSnapSettings();
        settings.GridSizeMicrometers.ShouldBe(GridSnapSettings.DefaultGridSizeMicrometers);
        settings.GridSizeMicrometers.ShouldBe(50.0);
    }

    [Fact]
    public void Snap_WhenDisabled_ReturnsOriginalValue()
    {
        var settings = new GridSnapSettings { IsEnabled = false };
        settings.Snap(123.456).ShouldBe(123.456);
    }

    [Fact]
    public void Snap_WhenEnabled_SnapsToNearestGridPoint()
    {
        var settings = new GridSnapSettings
        {
            IsEnabled = true,
            GridSizeMicrometers = 50.0
        };

        settings.Snap(0.0).ShouldBe(0.0);
        settings.Snap(24.9).ShouldBe(0.0);
        settings.Snap(25.0).ShouldBe(50.0);
        settings.Snap(49.9).ShouldBe(50.0);
        settings.Snap(50.0).ShouldBe(50.0);
        settings.Snap(74.9).ShouldBe(50.0);
        settings.Snap(75.0).ShouldBe(100.0);
    }

    [Fact]
    public void Snap_NegativeValues_SnapsCorrectly()
    {
        var settings = new GridSnapSettings
        {
            IsEnabled = true,
            GridSizeMicrometers = 50.0
        };

        settings.Snap(-24.9).ShouldBe(0.0);
        settings.Snap(-25.0).ShouldBe(-50.0); // AwayFromZero: -0.5 rounds to -1
        settings.Snap(-25.1).ShouldBe(-50.0);
        settings.Snap(-75.0).ShouldBe(-100.0);
    }

    [Fact]
    public void Snap_CustomGridSize_SnapsCorrectly()
    {
        var settings = new GridSnapSettings
        {
            IsEnabled = true,
            GridSizeMicrometers = 100.0
        };

        settings.Snap(49.9).ShouldBe(0.0);
        settings.Snap(50.0).ShouldBe(100.0);
        settings.Snap(150.0).ShouldBe(200.0);
    }

    [Fact]
    public void SnapXY_WhenEnabled_SnapsBothCoordinates()
    {
        var settings = new GridSnapSettings
        {
            IsEnabled = true,
            GridSizeMicrometers = 50.0
        };

        var (x, y) = settings.Snap(123.0, 276.0);
        x.ShouldBe(100.0);
        y.ShouldBe(300.0);
    }

    [Fact]
    public void SnapXY_WhenDisabled_ReturnsOriginalValues()
    {
        var settings = new GridSnapSettings { IsEnabled = false };

        var (x, y) = settings.Snap(123.456, 789.012);
        x.ShouldBe(123.456);
        y.ShouldBe(789.012);
    }

    [Fact]
    public void Toggle_FlipsIsEnabled()
    {
        var settings = new GridSnapSettings();

        settings.IsEnabled.ShouldBeFalse();
        settings.Toggle();
        settings.IsEnabled.ShouldBeTrue();
        settings.Toggle();
        settings.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void GridSizeMicrometers_ZeroOrNegative_ResetsToDefault()
    {
        var settings = new GridSnapSettings();

        settings.GridSizeMicrometers = 0;
        settings.GridSizeMicrometers.ShouldBe(GridSnapSettings.DefaultGridSizeMicrometers);

        settings.GridSizeMicrometers = -10;
        settings.GridSizeMicrometers.ShouldBe(GridSnapSettings.DefaultGridSizeMicrometers);
    }

    [Fact]
    public void Snap_ExactGridPoints_ReturnsSameValue()
    {
        var settings = new GridSnapSettings
        {
            IsEnabled = true,
            GridSizeMicrometers = 50.0
        };

        settings.Snap(0.0).ShouldBe(0.0);
        settings.Snap(50.0).ShouldBe(50.0);
        settings.Snap(100.0).ShouldBe(100.0);
        settings.Snap(5000.0).ShouldBe(5000.0);
    }

    [Fact]
    public void Snap_SmallGridSize_WorksCorrectly()
    {
        var settings = new GridSnapSettings
        {
            IsEnabled = true,
            GridSizeMicrometers = 10.0
        };

        settings.Snap(4.9).ShouldBe(0.0);
        settings.Snap(5.0).ShouldBe(10.0);
        settings.Snap(14.9).ShouldBe(10.0);
        settings.Snap(15.0).ShouldBe(20.0);
    }
}

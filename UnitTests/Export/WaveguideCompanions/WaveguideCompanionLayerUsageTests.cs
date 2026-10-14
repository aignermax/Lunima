using CAP.Avalonia.Services.GdsImport.LayerVisibility;
using CAP.Avalonia.Services.WaveguideCompanions;
using CAP.Avalonia.ViewModels.GdsImport.LayerVisibility;
using Shouldly;
using Xunit;

namespace UnitTests.Export.WaveguideCompanions;

/// <summary>
/// The band layers of routed waveguides appear in the layer panel next to imported layers, so
/// they can be hidden / faded with the same per-layer controls.
/// </summary>
public class WaveguideCompanionLayerUsageTests
{
    [Fact]
    public void Collect_CountsEveryRoutedWaveguidePerBandLayer()
    {
        var (canvas, _) = CompanionLayerTestCanvas.Create(CompanionLayerTestCanvas.TrenchSpec());

        var usage = WaveguideCompanionLayerUsage.Collect(canvas).ShouldHaveSingleItem();

        usage.ShouldBe(new DesignLayerUsage(2, 0, 1));
    }

    [Fact]
    public void Collect_WithoutProvider_IsEmpty()
    {
        var (canvas, _) = CompanionLayerTestCanvas.Create(spec: null);

        WaveguideCompanionLayerUsage.Collect(canvas).ShouldBeEmpty();
    }

    [Fact]
    public void Merge_AddsCountsOfSharedPairsAndKeepsOrder()
    {
        var imported = new[] { new DesignLayerUsage(1, 0, 4), new DesignLayerUsage(2, 0, 2) };
        var companions = new[] { new DesignLayerUsage(2, 0, 3), new DesignLayerUsage(40, 0, 3) };

        WaveguideCompanionLayerUsage.Merge(imported, companions).ShouldBe(new[]
        {
            new DesignLayerUsage(1, 0, 4), new DesignLayerUsage(2, 0, 5), new DesignLayerUsage(40, 0, 3),
        });
    }

    [Fact]
    public void LayerPanel_ListsTheBandLayer()
    {
        var (canvas, _) = CompanionLayerTestCanvas.Create(CompanionLayerTestCanvas.TrenchSpec());
        var panel = new GdsLayerVisibilityViewModel(canvas);

        panel.Refresh();

        panel.Rows.ShouldContain(row => row.Layer == 2 && row.DataType == 0);
        panel.HasLayers.ShouldBeTrue();
    }
}

using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Canvas.ConnectionLoss;
using CAP_Core.Components.Connections;
using CAP_Core.LightCalculation.BendLoss;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.ConnectionLoss;

/// <summary>
/// The insertion-loss section of the Properties panel: itemizes propagation, bend and junction
/// loss of the selected connection, lists each bend with its radius, hides for metal traces
/// and follows the connection when its route (and loss) changes.
/// </summary>
[Collection("LocalizationSingleton")]
public class ConnectionLossViewModelTests
{
    public ConnectionLossViewModelTests() =>
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);

    [Fact]
    public void NoSelection_HidesTheSection()
    {
        var vm = new ConnectionLossViewModel();

        vm.HasConnection.ShouldBeFalse();
        vm.BendRows.ShouldBeEmpty();
    }

    [Fact]
    public void SelectedCorner_ShowsTheItemizedBreakdown()
    {
        var vm = new ConnectionLossViewModel { SelectedConnection = Select(Corner(5)) };

        vm.HasConnection.ShouldBeTrue();
        vm.BendsText.ShouldEndWith("dB");
        vm.BendsDetailText.ShouldContain("R 5");
        vm.JunctionsDetailText.ShouldBe("2 transitions");
        vm.PropagationDetailText.ShouldContain("µm path");
        vm.ModelText.ShouldBe("Bend model: generic (no process)");
        vm.BendRows.ShouldHaveSingleItem().ShouldStartWith("R 5 µm · 90°");
        double.Parse(vm.TotalText.Replace(" dB", ""), System.Globalization.CultureInfo.CurrentCulture)
            .ShouldBe(BendLossPresets.Generic.CornerLossDb(5, 90) + 0.5 * 300.0 / 10_000, 0.001);
    }

    [Fact]
    public void NegligibleLoss_IsShownBelowTheDisplayResolution_NotAsZero()
    {
        ConnectionLossViewModel.FormatDb(0.0004).ShouldBe("< 0.001 dB");
        ConnectionLossViewModel.FormatDb(0).ShouldNotContain("<");
    }

    [Fact]
    public void RouteChange_RefreshesTheBreakdown()
    {
        var connectionVm = Select(Corner(5));
        var vm = new ConnectionLossViewModel { SelectedConnection = connectionVm };
        string tightTotal = vm.TotalText;

        connectionVm.Connection.RestoreCachedPath(Corner(50));
        connectionVm.NotifyPathChanged();

        vm.TotalText.ShouldNotBe(tightTotal);
        vm.BendsDetailText.ShouldContain("R 50");
    }

    [Fact]
    public void StraightRoute_SaysNoBends()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        var vm = new ConnectionLossViewModel { SelectedConnection = Select(path) };

        vm.BendsDetailText.ShouldBe("no bends");
        vm.JunctionsDetailText.ShouldBe("0 transitions");
    }

    private static WaveguideConnectionViewModel Select(RoutedPath path)
    {
        var connection = new WaveguideConnection { PropagationLossDbPerCm = 0.5 };
        connection.RestoreCachedPath(path);
        return new WaveguideConnectionViewModel(connection);
    }

    /// <summary>100 µm in, a 90° arc, 200 µm out (300 µm of straights plus the arc).</summary>
    private static RoutedPath Corner(double radius)
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        path.Segments.Add(new BendSegment(100, radius, radius, 0, 90));
        path.Segments.Add(new StraightSegment(100 + radius, radius, 100 + radius, 200 + radius, 90));
        return path;
    }
}

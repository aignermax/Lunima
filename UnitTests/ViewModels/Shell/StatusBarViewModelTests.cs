using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Shell;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels.Shell;

/// <summary>The status bar's mode and selection fields follow the editor state live.</summary>
[Collection("LocalizationSingleton")]
public class StatusBarViewModelTests
{
    [Fact]
    public void ModeLabel_FollowsTheCanvasMode()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var loc = LocalizationService.Instance;

        vm.StatusBar.ModeLabel.ShouldBe(loc.Translate("Canvas.Mode.Select"));
        vm.CanvasInteraction.CurrentMode = InteractionMode.Connect;
        vm.StatusBar.ModeLabel.ShouldBe(loc.Translate("Canvas.Mode.Connect"));
    }

    [Theory]
    [InlineData(InteractionMode.Select, "Canvas.Mode.Select")]
    [InlineData(InteractionMode.PlaceComponent, "Canvas.Mode.Place")]
    [InlineData(InteractionMode.PlaceGroupTemplate, "Canvas.Mode.Place")]
    [InlineData(InteractionMode.Connect, "Canvas.Mode.Connect")]
    [InlineData(InteractionMode.Delete, "Canvas.Mode.Delete")]
    [InlineData(InteractionMode.Probe, "Canvas.Mode.Probe")]
    [InlineData(InteractionMode.PickAnalysisOutput, "Canvas.Mode.PickOutput")]
    [InlineData(InteractionMode.Cut, "Canvas.Mode.Cut")]
    public void ModeKey_MapsEveryMode(InteractionMode mode, string key) =>
        StatusBarViewModel.ModeKey(mode).ShouldBe(key);

    [Fact]
    public void Selection_IsSummarized_ForNoneOneAndManyComponents()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var loc = LocalizationService.Instance;
        vm.StatusBar.SelectionSummary.ShouldBe(loc.Translate("StatusBar.Selection.None"));
        vm.StatusBar.HasSelection.ShouldBeFalse();

        var first = vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());
        var second = vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins());

        vm.Canvas.Selection.SelectSingle(first);
        vm.StatusBar.SelectionSummary.ShouldBe(first.DisplayName);
        vm.StatusBar.HasSelection.ShouldBeTrue();

        vm.Canvas.Selection.AddToSelection(second);
        vm.StatusBar.SelectionSummary.ShouldBe(
            string.Format(loc.Translate("StatusBar.Selection.Components"), 2));

        vm.Canvas.Selection.ClearSelection();
        vm.StatusBar.HasSelection.ShouldBeFalse();
    }
}

using CAP.Avalonia.ViewModels.Shell;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels.Shell;

/// <summary>The left icon rail shows one section at a time, the library by default.</summary>
public class LeftRailViewModelTests
{
    [Fact]
    public void Library_IsTheDefaultSection()
    {
        var rail = new LeftRailViewModel();

        rail.ActiveSection.ShouldBe(LeftRailSection.Library);
        rail.IsLibraryActive.ShouldBeTrue();
        rail.IsHierarchyActive.ShouldBeFalse();
        rail.IsPdksActive.ShouldBeFalse();
    }

    [Fact]
    public void Commands_SwitchTheSection_AndExactlyOneFlagIsSet()
    {
        var rail = new LeftRailViewModel();

        rail.ShowHierarchyCommand.Execute(null);
        rail.IsHierarchyActive.ShouldBeTrue();
        rail.IsLibraryActive.ShouldBeFalse();

        rail.ShowPdksCommand.Execute(null);
        rail.IsPdksActive.ShouldBeTrue();
        rail.IsHierarchyActive.ShouldBeFalse();

        rail.ShowLibraryCommand.Execute(null);
        rail.IsLibraryActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("ComponentLibraryList")]
    [InlineData("UserGroupsListBox")]
    public void RevealTourTarget_SwitchesToTheLibrary_ForLibraryControls(string target)
    {
        var rail = new LeftRailViewModel();
        rail.ShowPdksCommand.Execute(null);

        rail.RevealTourTarget(target);

        rail.IsLibraryActive.ShouldBeTrue();
    }

    [Theory]
    [InlineData("RunSimulationButton")]
    [InlineData(null)]
    public void RevealTourTarget_LeavesThePane_ForControlsOutsideTheRail(string? target)
    {
        var rail = new LeftRailViewModel();
        rail.ShowHierarchyCommand.Execute(null);

        rail.RevealTourTarget(target);

        rail.IsHierarchyActive.ShouldBeTrue();
    }

    [Fact]
    public void StartingTheFirstStepsTour_RevealsTheLibraryItPointsAt()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.LeftRail.ShowHierarchyCommand.Execute(null);

        vm.Tutorial.Start();

        vm.Tutorial.CurrentTargetName.ShouldBe("ComponentLibraryList");
        vm.LeftRail.IsLibraryActive.ShouldBeTrue();
    }
}

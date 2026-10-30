using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Workspaces.ToolSets;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// User story: each workspace's left panel offers its own tool set. Optics lists instruments
/// only (no saved groups) with a "Place circuit parts in Layout" hint; Logic shows the gate
/// library instead of the component library; clicking the hint switches to Layout with the
/// full library — one click, no silent refusal.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default runs.
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowWorkspaceToolSetsTests
{
    [AvaloniaFact]
    public void LeftPanel_FollowsTheWorkspaceToolSet_AndTheHintSwitchesToLayout()
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var win = host.Window;
        UiInput.RunJobs();

        UiInput.Click(win, Segment(win, "Optics"));
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Optics);
        var library = host.LibraryListBox;
        library.IsEffectivelyVisible.ShouldBeTrue("Optics keeps its (instrument) library");
        var items = library.ItemsSource!.Cast<ComponentTemplate>().ToList();
        items.ShouldNotBeEmpty("the bundled PDKs carry couplers and detectors");
        items.ShouldAllBe(t => InstrumentTemplates.IsInstrument(t));
        Named<ListBox>(win, "UserGroupsListBox").IsEffectivelyVisible.ShouldBeFalse("saved groups are circuit parts");
        VisibleHint(win).ShouldNotBeNull("Optics points to Layout for circuit parts");

        UiInput.Click(win, Segment(win, "Logic"));
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Logic);
        library.IsEffectivelyVisible.ShouldBeFalse("Logic shows the gate library instead");
        vm.LeftRail.IsGatesActive.ShouldBeTrue();
        UiInput.Descendants<Button>(win).First(b => b.Name == "GatesMakeGateButton")
            .IsEffectivelyVisible.ShouldBeTrue();

        UiInput.Click(win, VisibleHint(win).ShouldNotBeNull());
        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout, "the hint is the one-click way to Layout");
        vm.LeftRail.IsLibraryActive.ShouldBeTrue();
        library.IsEffectivelyVisible.ShouldBeTrue();
        Named<ListBox>(win, "UserGroupsListBox").IsEffectivelyVisible.ShouldBeTrue();
        library.ItemsSource!.Cast<ComponentTemplate>().Count().ShouldBeGreaterThan(items.Count,
            "Layout lists the full library");
    }

    private static Button? VisibleHint(Window win) =>
        UiInput.Descendants<Button>(win).FirstOrDefault(b => b.Name == "PlaceInLayoutButton" && b.IsEffectivelyVisible);

    private static Button Segment(Window win, string label)
    {
        var bar = UiInput.Named<Border>(win, "WorkspaceSwitchBar").ShouldNotBeNull();
        return UiInput.Descendants<Button>(bar).First(b => Equals(b.Content, label));
    }

    private static T Named<T>(Window win, string name) where T : Control =>
        UiInput.Named<T>(win, name).ShouldNotBeNull($"{name} must exist");
}

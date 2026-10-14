using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.Dies.WorkSurface;

/// <summary>
/// In the System workspace the unit of work is the die: die content is locked (a selected
/// part hands its selection to its die); every other workspace works on the parts directly.
/// Selecting a part on a die scopes the library to that die, double-click frames the die.
/// </summary>
[Collection("LocalizationSingleton")]
public class SystemWorkspaceDieLockTests
{
    [Fact]
    public async Task System_LocksDieContent_LayoutUnlocksIt()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        await TwoDieExampleBuilder.BuildAsync(vm);
        var sin = vm.Canvas.Dies.RootDies.Single(d => d.GroupName == "SiN die");
        var part = vm.Canvas.Surface.ViewModelFor(sin.ChildComponents[0]);
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        vm.Canvas.Selection.SelectSingle(part);
        vm.Canvas.SelectedComponent = part;
        vm.ScopeDie.ShouldBe(sin, "selecting a part on a die scopes the library to that die");

        vm.WorkspaceSwitch.Current = Workspace.System;

        vm.Canvas.Surface.IsContentLocked.ShouldBeTrue();
        vm.Canvas.Surface.IsEditable.ShouldBeFalse();
        vm.Canvas.SelectedComponent!.Component.ShouldBe(sin, "the part's selection goes to its die");
        part.IsSelected.ShouldBeFalse();
        vm.Canvas.InteractiveComponents.ShouldAllBe(c => c.Component is CAP_Core.Components.Core.ComponentGroup);

        vm.WorkspaceSwitch.Current = Workspace.Layout;

        vm.Canvas.Surface.IsContentLocked.ShouldBeFalse();
        vm.Canvas.InteractiveComponents.ShouldContain(part);
    }

    [Fact]
    public async Task ZoomToDie_FramesTheDie_WithoutEnteringIt()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        await TwoDieExampleBuilder.BuildAsync(vm);
        var sin = vm.Canvas.Dies.RootDies.Single(d => d.GroupName == "SiN die");
        double zoomBefore = vm.ViewportControl.ZoomLevel;

        vm.ZoomToDie(sin);

        vm.Canvas.IsInGroupEditMode.ShouldBeFalse();
        vm.Canvas.SelectedComponent!.Component.ShouldBe(sin);
        vm.ViewportControl.ZoomLevel.ShouldNotBe(zoomBefore);
    }
}

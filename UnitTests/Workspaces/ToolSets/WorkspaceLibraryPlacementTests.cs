using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Library;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Workspaces.ToolSets;
using CAP_Core.Components.Creation;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using UnitTests.Helpers;
using Xunit;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;

namespace UnitTests.Workspaces.ToolSets;

/// <summary>
/// The workspace tool sets on the real view model: the library list is filtered per
/// workspace, every placement path (armed template, drag &amp; drop, a template still armed from
/// another workspace) follows the same rule, refusals say where the part is placed, and the
/// one-click switch to Layout reaches the full library.
/// </summary>
[Collection("LocalizationSingleton")]
public class WorkspaceLibraryPlacementTests
{
    [AvaloniaTheory]
    [InlineData(Workspace.System)]
    [InlineData(Workspace.Optics)]
    [InlineData(Workspace.Logic)]
    public void WorkspaceSwitch_WithBoundLibrarySelection_DisarmsRefusedPlacement(Workspace target)
    {
        var vm = CreateWithLibrary("MMI 2x2");
        var list = BoundLibrary(vm);
        list.SelectedItem = vm.LeftPanel.FilteredTemplates.Single(t => t.Name == "MMI 2x2");
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.PlaceComponent);

        vm.WorkspaceSwitch.Current = target;

        vm.CanvasInteraction.SelectedTemplate.ShouldBeNull();
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.Select);
    }

    [AvaloniaFact]
    public void WorkspaceSwitch_WithBoundLibrarySelection_KeepsAllowedEdgeCouplerArmed()
    {
        var vm = CreateWithLibrary("Test Edge Coupler", "MMI 2x2");
        var list = BoundLibrary(vm);
        var template = vm.LeftPanel.FilteredTemplates.Single(t => t.Name == "Test Edge Coupler");
        list.SelectedItem = template;

        vm.WorkspaceSwitch.Current = Workspace.System;

        vm.CanvasInteraction.SelectedTemplate.ShouldBeSameAs(template);
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.PlaceComponent);
        list.SelectedItem.ShouldBeSameAs(template);
    }

    private static ListBox BoundLibrary(MainViewModel vm)
    {
        var list = new ListBox { ItemsSource = vm.LeftPanel.FilteredTemplates };
        list.Bind(ListBox.SelectedItemProperty, new Binding("CanvasInteraction.SelectedTemplate")
        {
            Source = vm, Mode = BindingMode.TwoWay,
        });
        return list;
    }

    private const string Pdk = "Demo";

    private static ComponentTemplate T(string name) => new() { Name = name, PdkSource = Pdk, Category = "Test" };

    private static MainViewModel CreateWithLibrary(params string[] names)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        vm.LeftPanel.PdkManager.RegisterPdk(Pdk, null, true, names.Length);
        foreach (var name in names)
            vm.LeftPanel.AllTemplates.Add(T(name));
        vm.WorkspaceSwitch.Current = Workspace.System;
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        return vm;
    }

    private static string[] Library(MainViewModel vm) => vm.LeftPanel.FilteredTemplates.Select(t => t.Name).ToArray();

    [Fact]
    public void Library_ListsWhatTheWorkspacePlaces()
    {
        string[] ours = ["Test Grating Coupler", "Test MMI 2x2", "Test Photodetector", "Test Straight Waveguide"];
        var vm = CreateWithLibrary(ours);
        ours.ShouldAllBe(name => Library(vm).Contains(name), "Layout lists the full library");

        vm.WorkspaceSwitch.Current = Workspace.Optics;
        vm.LeftPanel.FilteredTemplates.ShouldAllBe(t => InstrumentTemplates.IsInstrument(t));
        Library(vm).ShouldContain("Test Grating Coupler");
        Library(vm).ShouldContain("Test Photodetector");
        Library(vm).ShouldNotContain("Test MMI 2x2");
        Library(vm).ShouldNotContain("Test Straight Waveguide");

        vm.WorkspaceSwitch.Current = Workspace.Logic;
        Library(vm).ShouldBeEmpty("Logic places gates from the gate library, not raw components");

        vm.WorkspaceSwitch.Current = Workspace.Layout;
        ours.ShouldAllBe(name => Library(vm).Contains(name));
    }

    [Fact]
    public void Optics_RefusesToArmACircuitPart_WithAHintToLayout()
    {
        var vm = CreateWithLibrary("MMI 2x2", "Grating Coupler TE 1550");
        vm.WorkspaceSwitch.Current = Workspace.Optics;

        vm.CanvasInteraction.SelectedTemplate = T("MMI 2x2");

        vm.CanvasInteraction.SelectedTemplate.ShouldBeNull();
        vm.CanvasInteraction.CurrentMode.ShouldNotBe(InteractionMode.PlaceComponent);
        vm.StatusText.ShouldContain("Layout");

        vm.CanvasInteraction.SelectedTemplate = T("Grating Coupler TE 1550");
        vm.CanvasInteraction.SelectedTemplate.ShouldNotBeNull("instruments stay placeable in Optics");
    }

    [Fact]
    public void DragAndDrop_FollowsTheSameRule()
    {
        var vm = CreateWithLibrary("MMI 2x2");
        vm.WorkspaceSwitch.Current = Workspace.Logic;

        vm.CanvasInteraction.DropComponentTemplateAt(T("MMI 2x2"), 500, 300);

        vm.Canvas.Components.ShouldBeEmpty("a drop from anywhere must not bypass the workspace");
        vm.StatusText.ShouldContain("Layout");
    }

    [Fact]
    public void Logic_PlacesGatesOnly()
    {
        var vm = CreateWithLibrary();
        vm.WorkspaceSwitch.Current = Workspace.Logic;

        vm.CanvasInteraction.SelectedGroupTemplate = new GroupTemplate
        {
            Name = "Splitter tree", TemplateGroup = LogicGateFixtureFactory.CreateCombinerGroup(),
        };
        vm.CanvasInteraction.SelectedGroupTemplate.ShouldBeNull("a plain group is a circuit part");

        var gate = new GroupTemplate { Name = "My NAND", TemplateGroup = WorkspaceToolSetTests.Gate() };
        vm.CanvasInteraction.SelectedGroupTemplate = gate;
        vm.CanvasInteraction.SelectedGroupTemplate.ShouldBe(gate);
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.PlaceGroupTemplate);
    }

    [Fact]
    public void SwitchingWorkspace_DisarmsATemplateTheNewWorkspaceDoesNotPlace()
    {
        var vm = CreateWithLibrary("MMI 2x2");
        vm.CanvasInteraction.SelectedTemplate = T("MMI 2x2");
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.PlaceComponent);

        vm.WorkspaceSwitch.Current = Workspace.Optics;

        vm.CanvasInteraction.SelectedTemplate.ShouldBeNull();
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.Select);
    }

    [Fact]
    public void System_PlacesTheEdgeCouplersThatLinkDies_AndNothingElse()
    {
        var vm = CreateWithLibrary("Edge Coupler", "Grating Coupler TE 1550", "MMI 2x2");
        vm.WorkspaceSwitch.Current = Workspace.System;

        vm.IsDieFacetLibrary.ShouldBeTrue();
        vm.LeftRail.IsLibraryAvailable.ShouldBeTrue("the die-edge couplers are reachable from System's rail");
        Library(vm).ShouldContain("Edge Coupler");
        Library(vm).ShouldAllBe(name => name.Contains("Edge Coupler", StringComparison.OrdinalIgnoreCase));

        vm.CanvasInteraction.SelectedTemplate = T("MMI 2x2");
        vm.CanvasInteraction.SelectedTemplate.ShouldBeNull();
        vm.StatusText.ShouldContain("Layout");

        vm.CanvasInteraction.SelectedTemplate = T("Edge Coupler");
        vm.CanvasInteraction.SelectedTemplate.ShouldNotBeNull("an edge coupler on a die edge is what links two dies");
        vm.CanvasInteraction.CurrentMode.ShouldBe(InteractionMode.PlaceComponent);
    }

    [Theory]
    [InlineData(Workspace.Logic)]
    public void ShowLibrary_FromAWorkspaceWithoutIt_SwitchesToLayout(Workspace workspace)
    {
        var vm = CreateWithLibrary();
        vm.WorkspaceSwitch.Current = workspace;
        vm.LeftRail.IsLibraryAvailable.ShouldBeFalse();

        vm.LeftRail.ShowLibraryCommand.Execute(null);

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
        vm.LeftRail.IsLibraryActive.ShouldBeTrue();
    }

    [Fact]
    public void SwitchToLayout_OpensTheFullLibrary()
    {
        var vm = CreateWithLibrary("MMI 2x2");
        vm.WorkspaceSwitch.Current = Workspace.Optics;
        vm.LeftRail.ShowHierarchyCommand.Execute(null);

        vm.SwitchToLayoutCommand.Execute(null);

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Layout);
        vm.LeftRail.IsLibraryActive.ShouldBeTrue();
        vm.IsFullLibrary.ShouldBeTrue();
        Library(vm).ShouldContain("MMI 2x2");
    }

    [Fact]
    public void PlaceLightSource_FromLogic_HandsOverToOptics()
    {
        var vm = CreateWithLibrary("Grating Coupler TE 1550");
        vm.WorkspaceSwitch.Current = Workspace.Logic;

        vm.BottomPanel.Analysis.Readiness.PlaceLightSourceCommand.Execute(null);

        vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Optics, "a light source is an instrument");
        vm.CanvasInteraction.SelectedTemplate.ShouldNotBeNull();
    }
}

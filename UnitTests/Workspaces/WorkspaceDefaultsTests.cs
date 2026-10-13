using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>The content-based workspace a design opens in when its file stores none.</summary>
public class WorkspaceDefaultsTests
{
    private static readonly WaveguideConnection[] NoConnections = [];

    [Fact]
    public void EmptyDesign_OpensInLayout() =>
        WorkspaceDefaults.ChooseFor([], NoConnections).ShouldBe(Workspace.Layout);

    [Fact]
    public void PassiveDesignWithoutLightSource_OpensInLayout() =>
        WorkspaceDefaults.ChooseFor([TestComponentFactory.CreateBasicComponent()], NoConnections)
            .ShouldBe(Workspace.Layout);

    [Fact]
    public void DesignWithLightSource_OpensInOptics()
    {
        var source = TestComponentFactory.CreateBasicComponent();
        source.IsUserMarkedLightSource = true;

        WorkspaceDefaults.ChooseFor([source], NoConnections).ShouldBe(Workspace.Optics);
    }

    [Fact]
    public void NestedGateGroup_OpensInLogic()
    {
        var gate = TestComponentFactory.CreateComponentGroup("NAND", addChildren: true);
        gate.TruthTablePinAssignment = new TruthTablePinAssignment();
        var cell = TestComponentFactory.CreateComponentGroup("Cell");
        cell.AddChild(gate);
        var source = TestComponentFactory.CreateBasicComponent();
        source.IsUserMarkedLightSource = true;

        WorkspaceDefaults.ChooseFor([cell, source], NoConnections)
            .ShouldBe(Workspace.Logic, "a gate beats a light source: logic designs carry couplers too");
    }

    [Fact]
    public void TwoProcessBoundChiplets_OpenInSystem()
    {
        var a = TestComponentFactory.CreateComponentGroup("Die A", addChildren: true);
        var b = TestComponentFactory.CreateComponentGroup("Die B", addChildren: true);
        a.ProcessBinding = Process("SiN");
        b.ProcessBinding = Process("InP");
        b.TruthTablePinAssignment = new TruthTablePinAssignment();

        WorkspaceDefaults.ChooseFor([a, b], NoConnections)
            .ShouldBe(Workspace.System, "a multi-chiplet assembly beats logic: logic across chiplets is a system");
    }

    [Fact]
    public void OneProcessBoundGroup_IsNotAMultiChipletSystem()
    {
        var a = TestComponentFactory.CreateComponentGroup("Die A", addChildren: true);
        a.ProcessBinding = Process("SiN");

        WorkspaceDefaults.IsMultiChiplet([a], NoConnections).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Two Chiplets - Edge-Coupler Link.lun", Workspace.System)]
    [InlineData("Mach-Zehnder Interferometer.lun", Workspace.Optics)]
    [InlineData("EBeam Mach-Zehnder Interferometer.lun", Workspace.Optics)]
    [InlineData("Logic Gate NOT-NAND.lun", Workspace.Logic)]
    public async Task ShippedExample_OpensInItsWorkspace(string fileName, Workspace expected)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), fileName);

        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue($"'{fileName}' must load");

        vm.WorkspaceSwitch.Current.ShouldBe(expected);
    }

    private static ActiveProcessSelection Process(string name) =>
        new(name, Fingerprint: null, MemberPdkNames: [], IsPlayground: false);
}

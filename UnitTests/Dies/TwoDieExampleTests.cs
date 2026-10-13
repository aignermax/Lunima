using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Dies;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.Dies;

/// <summary>
/// End to end through the main view model: build an SOI chip, add a SiN die, put an edge
/// coupler on it, snap the dies together, Run in System — the link and its loss (including
/// the SOI/SiN spot-size mismatch) show up in the Chiplets panel rows. Also authors the
/// shipped two-die example and re-saves the two legacy chiplet examples in the die model
/// when <c>CAP_AUTHOR_EXAMPLES=1</c>.
/// </summary>
public class TwoDieExampleTests
{
    private const string AuthorEnableVariable = "CAP_AUTHOR_EXAMPLES";

    [Fact]
    public async Task NewDie_EdgeCouplers_SnapLink_Run_ShowsTheLinkLoss()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();

        var sinDie = await TwoDieExampleBuilder.BuildAsync(vm);

        vm.Canvas.Dies.RootDies.Count.ShouldBe(2);
        var link = vm.Canvas.ConnectionManager.Connections.Single(c => c.IsCrossChipletFacetLink);
        DieAssembly.DieOf(link.EndPin.ParentComponent).ShouldNotBeNull();
        new CAP_Core.Analysis.DieAssemblyChecker().Check(vm.Canvas.Components.Select(c => c.Component))
            .ShouldBeEmpty("the built assembly is clean");

        vm.WorkspaceSwitch.Current = Workspace.System;
        await vm.RunWorkspaceCommand.ExecuteAsync(null);

        var row = vm.SystemOverview.Links.ShouldHaveSingleItem();
        row.LossText.ShouldEndWith("dB");
        row.IsOverBudget.ShouldBeFalse(row.LossText);
        vm.SystemOverview.Chiplets.Select(c => c.Name).ShouldBe(new[] { "SOI die", "SiN die" }, ignoreOrder: true);
        vm.SystemOverview.Chiplets.All(c => c.IsDie).ShouldBeTrue();
        sinDie.ProcessBinding!.MemberPdkNames.ShouldContain(TwoDieExampleBuilder.SinPdk);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Author_TwoDieExample_AndResaveChipletExamples()
    {
        if (Environment.GetEnvironmentVariable(AuthorEnableVariable) != "1")
            return;
        var examples = ExampleDesignFilesTests.ExamplesDirectory();

        var vm = ShowcaseCircuit.CreateStagedViewModel();
        await TwoDieExampleBuilder.BuildAsync(vm);
        vm.WorkspaceSwitch.Current = Workspace.System;
        (await vm.FileOperations.SaveDesignToPathAsync(Path.Combine(examples, TwoDieExampleBuilder.ExampleFileName))).ShouldBeTrue();

        foreach (var legacy in new[] { "Two Chiplets - Edge-Coupler Link.lun", "Logic Gate Across Two Chiplets.lun" })
        {
            var resave = MainViewModelTestHelper.CreateMainViewModel();
            var path = Path.Combine(examples, legacy);
            (await resave.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue(legacy);
            await resave.FileOperations.PostLoadRouting;
            resave.Canvas.Dies.IsAssemblyRoot.ShouldBeTrue($"{legacy} must upgrade to dies");
            (await resave.FileOperations.SaveDesignToPathAsync(path)).ShouldBeTrue(legacy);
        }
    }
}

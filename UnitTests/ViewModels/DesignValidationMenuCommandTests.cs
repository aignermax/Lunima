using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Analysis;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Headless VM-level tests for the "Check design" menu entry (Export menu). The menu
/// item binds the same <c>RunDesignChecksCommand</c> as the Design Checks panel button,
/// so executing the command must run validation and populate the diagnostics
/// collection the panel shows.
/// </summary>
public class DesignValidationMenuCommandTests
{
    [Fact]
    public async Task RunDesignChecksCommand_UnconnectedOpticalPin_PopulatesDiagnosticsCollection()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var comp = TestComponentFactory.CreateStraightWaveGuideWithPhysicalPins();
        comp.HumanReadableName = "LonelyWaveguide";
        vm.Canvas.Components.Add(new ComponentViewModel(comp));

        // The command backgrounds the validator passes (issue #1150), so the issues
        // land only after the returned task completes — ExecuteAsync, not Execute.
        await vm.RunDesignChecksCommand.ExecuteAsync(null);

        var issues = vm.RightPanel.DesignValidation.Issues;
        issues.Count.ShouldBe(2, "both optical pins of the placed waveguide are unconnected");
        issues.ShouldAllBe(i => i.Type == DesignIssueType.UnconnectedPin);
        issues.ShouldContain(i => i.Description.Contains($"{comp.Identifier}.in"));
        issues.ShouldContain(i => i.Description.Contains($"{comp.Identifier}.out"));
        vm.RightPanel.DesignValidation.HasIssues.ShouldBeTrue();
    }

    [Fact]
    public async Task RunDesignChecksCommand_EmptyDesign_ReportsNoIssues()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();

        await vm.RunDesignChecksCommand.ExecuteAsync(null);

        vm.RightPanel.DesignValidation.HasIssues.ShouldBeFalse();
        vm.RightPanel.DesignValidation.Issues.ShouldBeEmpty();
    }
}

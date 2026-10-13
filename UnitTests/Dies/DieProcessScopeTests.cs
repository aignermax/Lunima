using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>
/// In a die assembly every part answers to its own die's process and PDKs — never to the
/// design-level process — in the design checks, the canvas-wide process scope and the
/// status-bar process chip.
/// </summary>
public class DieProcessScopeTests
{
    private static readonly Dictionary<Component, string?> NoSources = new();

    [Fact]
    public void PartOfAnotherProcess_OnItsOwnDie_IsNotFlagged()
    {
        var sinPart = Part("sin", 100, 100);
        var die = Die("SiN die", 0, 0, 1000, 1000, Process("SiN 300", "SiN", "sin-pdk"), sinPart);

        var issues = new ComponentPdkCompatibilityChecker().Check(
            new Component[] { die }, new Dictionary<Component, string?> { [sinPart] = "sin-pdk" },
            Array.Empty<string>(), enabledPdkNames: new[] { "soi-pdk" });

        issues.ShouldBeEmpty("the design-level (SOI) lock does not apply to a SiN die's parts");
    }

    [Fact]
    public void PartNotMatchingItsDie_IsFlagged_WithTheDieInTheMessage()
    {
        var soiPart = Part("soi", 100, 100);
        var die = Die("SiN die", 0, 0, 1000, 1000, Process("SiN 300", "SiN", "sin-pdk"), soiPart);

        var issue = new ComponentPdkCompatibilityChecker().Check(
            new Component[] { die }, new Dictionary<Component, string?> { [soiPart] = "soi-pdk" },
            Array.Empty<string>(), enabledPdkNames: new[] { "soi-pdk" }).Single();

        issue.Type.ShouldBe(DesignIssueType.PdkProcessMismatch);
        issue.Description.ShouldContain("SiN die");
        issue.LocalizationKey.ShouldBe("DesignChecks.PdkDieProcessMismatch");
    }

    [Fact]
    public void PartOutsideTheDiesEnabledPdkSubset_IsFlagged()
    {
        var part = Part("b", 100, 100);
        var die = Die("SiN die", 0, 0, 1000, 1000, Process("SiN 300", "SiN", "sin-a", "sin-b"), part);
        die.Die = die.Die! with { EnabledPdkNames = new[] { "sin-a" } };

        new ComponentPdkCompatibilityChecker().Check(
                new Component[] { die }, new Dictionary<Component, string?> { [part] = "sin-b" },
                Array.Empty<string>(), enabledPdkNames: new[] { "sin-a", "sin-b" })
            .Count.ShouldBe(1);
    }

    [Fact]
    public void PartsOfAnUnboundDie_HaveNoProcessToMismatch()
    {
        var part = Part("x", 100, 100);
        var die = Die("Free die", 0, 0, 1000, 1000, null, part);

        new ComponentPdkCompatibilityChecker().Check(
                new Component[] { die }, new Dictionary<Component, string?> { [part] = "any-pdk" },
                Array.Empty<string>(), enabledPdkNames: Array.Empty<string>())
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ShippedSoiSinExample_HasNoDesignCheckProblems()
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), TwoDieExampleBuilder.ExampleFileName);
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;

        await vm.RunDesignChecksCommand.ExecuteAsync(null);

        vm.RightPanel.DesignValidation.Issues
            .Where(i => i.Severity == DesignIssueSeverity.Problem)
            .Select(i => i.Description)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task ProcessChip_ShowsTheDieProcesses_AndFollowsTheSelectedDie()
    {
        var vm = ShowcaseCircuit.CreateStagedViewModel();
        await TwoDieExampleBuilder.BuildAsync(vm);
        vm.Canvas.SelectedComponent = null;

        vm.ActiveProcessLabel.ShouldContain("2");
        vm.ActiveProcessTip.ShouldContain("SOI die");
        vm.ActiveProcessTip.ShouldContain("SiN die");
        vm.CanvasScopeProcess.ShouldBeNull("at the assembly root each die answers for its own parts");

        var sin = vm.Canvas.Dies.RootDies.Single(d => d.GroupName == "SiN die");
        vm.Canvas.SelectedComponent = vm.Canvas.Components.Single(c => c.Component == sin);
        vm.ActiveProcessLabel.ShouldContain("SiN die");
        vm.ActiveProcessLabel.ShouldContain(TwoDieExampleBuilder.SinPdk);

        vm.OpenDie(sin);
        vm.CanvasScopeProcess!.MemberPdkNames.ShouldContain(TwoDieExampleBuilder.SinPdk);
    }
}

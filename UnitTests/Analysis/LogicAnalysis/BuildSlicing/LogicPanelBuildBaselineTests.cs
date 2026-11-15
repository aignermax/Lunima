using System.Globalization;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Regression;
using Xunit;

namespace UnitTests.Analysis.LogicAnalysis.BuildSlicing;

/// <summary>
/// Pins everything the Logic workspace's Run (the first press builds the network) publishes for
/// the largest shipped logic design, so moving the build's capture between dispatcher frames can
/// never change what the student sees: inputs and outputs (names, order, raw pins, delays), bus
/// rows, gate count, every gate's re-extracted truth table and delay, the critical path, fan-out
/// warnings, canvas badges, status text, the outputs of one input vector and one clock step. The
/// build runs once under the headless dispatcher (the capture is cut into frames) and once without
/// a dispatcher (one synchronous capture); both must match the same baseline. The baseline lives
/// next to the golden designs and is regenerated with <c>LUNIMA_UPDATE_GOLDEN=1</c>.
/// </summary>
[Collection("LocalizationSingleton")]
public class LogicPanelBuildBaselineTests
{
    private const string ExampleFileName = "Logic Gate 4-Bit Adder.lun";
    private const string BaselineSuffix = ".logic-build.txt";

    [Fact]
    public async Task RunInLogic_WithoutDispatcher_MatchesPinnedBaseline()
    {
        var actual = await BuildAndDescribeAsync();

        if (GoldenManifest.IsUpdateMode())
        {
            await File.WriteAllLinesAsync(BaselinePath(), actual);
            return;
        }
        await AssertMatchesBaselineAsync(actual);
    }

    [AvaloniaFact]
    public async Task RunInLogic_OnDispatcher_MatchesPinnedBaseline()
    {
        await CaptureSlicingFixture.ContinueInsideDispatcherJobAsync();
        var actual = await BuildAndDescribeAsync();

        await AssertMatchesBaselineAsync(actual);
    }

    private static async Task<IReadOnlyList<string>> BuildAndDescribeAsync()
    {
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        // The panel formats delays and split ratios with the current culture; pin it so the
        // baseline reads the same on every runner locale.
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var provider = new BuiltLogicNetworkProvider();
        var vm = MainViewModelTestHelper.CreateMainViewModel(logicNetworkProvider: provider);
        try
        {
            var path = Path.Combine(GoldenManifest.ExamplesDirectory(GoldenManifest.LocateRepositoryRoot()), ExampleFileName);
            (await vm.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue($"'{ExampleFileName}' must load");
            await vm.FileOperations.PostLoadRouting;
            vm.WorkspaceSwitch.Current = Workspace.Logic;

            await vm.RunWorkspaceCommand.ExecuteAsync(null);

            vm.RightPanel.Logic.HasNetwork.ShouldBeTrue(vm.RightPanel.Logic.StatusText);
            var network = provider.Network.ShouldNotBeNull("the build must publish its network to the shared hand-off");
            return LogicBuildDescription.Describe(vm, network);
        }
        finally
        {
            await vm.DisposeAsync();
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static async Task AssertMatchesBaselineAsync(IReadOnlyList<string> actual)
    {
        var baselinePath = BaselinePath();
        File.Exists(baselinePath).ShouldBeTrue(
            $"missing baseline '{baselinePath}' — run once with {GoldenManifest.UpdateSwitchName}=1 and review it");
        var expected = await File.ReadAllLinesAsync(baselinePath);
        int shared = Math.Min(actual.Count, expected.Length);
        int firstDifference = Enumerable.Range(0, shared).FirstOrDefault(i => actual[i] != expected[i], -1);
        firstDifference.ShouldBe(-1, firstDifference < 0 ? "" :
            $"line #{firstDifference} differs.\n  expected: {expected[firstDifference]}\n  actual:   {actual[firstDifference]}");
        actual.Count.ShouldBe(expected.Length, "the build must publish the pinned number of lines");
    }

    private static string BaselinePath() =>
        Path.Combine(GoldenManifest.GoldenDirectory(GoldenManifest.LocateRepositoryRoot()),
            Path.GetFileNameWithoutExtension(ExampleFileName) + BaselineSuffix);
}

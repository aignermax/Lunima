using System.Globalization;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels;
using CAP_Core.Analysis;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Regression;
using Xunit;

namespace UnitTests.ViewModels.Shell;

/// <summary>
/// Pins the complete design-check output of shipped examples so moving work between the UI
/// thread and the validation worker can never change what the user is told: same findings,
/// same order, same localized (English) messages, same marker positions. Covered: the largest
/// logic design (Playground), a process-locked single-die design, and two "diverged PDK"
/// variants — a process edit that drops the examples' PDK from the allowed set — which drive
/// the PDK-source resolution through placed template snapshots, the live-library fallback and
/// nested group children. Baselines live next to the golden designs and are regenerated with
/// <c>LUNIMA_UPDATE_GOLDEN=1</c>; review the text diff like any other snapshot.
/// </summary>
[Collection("LocalizationSingleton")]
public class DesignValidationFindingsBaselineTests
{
    private const string BaselineSuffix = ".design-checks.txt";
    private const string DivergedSuffix = ".diverged-pdk";
    private const string DivergedPdkName = "Demo PDK";
    private const string EnglishLanguageCode = "en";
    private const string CoordinateFormat = "0.###";

    /// <summary>Pinned cases: example file name and whether its PDK is diverged from the process.</summary>
    public static TheoryData<string, bool> PinnedCases => new()
    {
        { "Logic Gate 4-Bit Adder.lun", false },
        { "Length Matching - Fiber Array Fan-In.lun", false },
        { "Length Matching - Fiber Array Fan-In.lun", true },
        { "Logic Gate Half Adder.lun", true },
    };

    [Theory]
    [MemberData(nameof(PinnedCases))]
    public async Task RunDesignChecks_OnShippedExample_MatchesPinnedBaseline(string exampleFileName, bool divergePdk)
    {
        var actual = await RunChecksAsync(exampleFileName, divergePdk);
        var baselinePath = BaselinePath(exampleFileName, divergePdk);

        if (GoldenManifest.IsUpdateMode())
        {
            await File.WriteAllLinesAsync(baselinePath, actual);
            return;
        }

        File.Exists(baselinePath).ShouldBeTrue(
            $"missing baseline '{baselinePath}' — run once with {GoldenManifest.UpdateSwitchName}=1 and review it");
        var expected = await File.ReadAllLinesAsync(baselinePath);
        AssertSameFindings(Path.GetFileName(baselinePath), actual, expected);
    }

    private static async Task<IReadOnlyList<string>> RunChecksAsync(string exampleFileName, bool divergePdk)
    {
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        try
        {
            var path = Path.Combine(GoldenManifest.ExamplesDirectory(GoldenManifest.LocateRepositoryRoot()), exampleFileName);
            (await vm.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue($"'{exampleFileName}' must load");
            await vm.FileOperations.PostLoadRouting;
            if (divergePdk)
                DivergePdkFromProcess(vm);

            await vm.RunDesignChecksCommand.ExecuteAsync(null);

            return Describe(vm);
        }
        finally
        {
            await vm.DisposeAsync();
        }
    }

    /// <summary>
    /// Drops <see cref="DivergedPdkName"/> from the allowed PDK set, as a process edit would,
    /// and makes the first placed part forget its template snapshot so it is resolved through
    /// the live library instead.
    /// </summary>
    private static void DivergePdkFromProcess(MainViewModel vm)
    {
        var pdks = vm.LeftPanel.PdkManager;
        pdks.LoadedPdks.ShouldContain(p => p.Name == DivergedPdkName);
        pdks.ApplyProcessLock(pdks.LoadedPdks.Select(p => p.Name).Where(name => name != DivergedPdkName).ToList());
        if (vm.Canvas.Components.FirstOrDefault(c => c.TemplatePdkSource == DivergedPdkName) is { } placed)
            placed.TemplatePdkSource = null;
    }

    private static IReadOnlyList<string> Describe(MainViewModel vm)
    {
        var english = new LocalizationService();
        english.SetLanguage(EnglishLanguageCode);
        var findings = vm.RightPanel.DesignValidation.AllFindings;
        vm.Canvas.ViewSettings.DesignCheckFindings.ShouldBe(findings,
            "the canvas markers must show exactly the findings of the run, in order");
        return findings.Select(issue => DescribeFinding(issue, english)).ToList();
    }

    private static string DescribeFinding(DesignIssue issue, LocalizationService english) =>
        string.Join("|",
            issue.Severity,
            issue.Type,
            issue.X.ToString(CoordinateFormat, CultureInfo.InvariantCulture),
            issue.Y.ToString(CoordinateFormat, CultureInfo.InvariantCulture),
            DesignIssueFormatter.Format(issue, english));

    private static void AssertSameFindings(string baseline, IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        int shared = Math.Min(actual.Count, expected.Count);
        int firstDifference = Enumerable.Range(0, shared).FirstOrDefault(i => actual[i] != expected[i], -1);
        firstDifference.ShouldBe(-1, firstDifference < 0 ? "" :
            $"'{baseline}' finding #{firstDifference} differs.\n  expected: {expected[firstDifference]}\n  actual:   {actual[firstDifference]}");
        actual.Count.ShouldBe(expected.Count, $"'{baseline}' must report the pinned number of findings");
    }

    private static string BaselinePath(string exampleFileName, bool divergePdk) =>
        Path.Combine(GoldenManifest.GoldenDirectory(GoldenManifest.LocateRepositoryRoot()),
            Path.GetFileNameWithoutExtension(exampleFileName) + (divergePdk ? DivergedSuffix : "") + BaselineSuffix);
}

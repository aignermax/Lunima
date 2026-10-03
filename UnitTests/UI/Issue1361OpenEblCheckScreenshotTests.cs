using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services.OpenEblCheck;
using CAP.Avalonia.Views.Dialogs;
using Shouldly;
using UnitTests.Export.OpenEbl;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1361 "Check for openEBL…" dialog: renders the dialog with a
/// fake checker in the three result states (pass / fail with typed errors / toolchain missing
/// with the pip hint) in English and German. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1361/</c> (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir.
/// Same pattern as <see cref="Issue1349SubNmSweepScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1361OpenEblCheckScreenshotTests
{
    private sealed record ManifestEntry(string File, string Caption);

    private static readonly (string Key, string Caption, OpenEblCheckReport Report)[] States =
    {
        ("pass", "Pass: both checks green, die size shown, checked-file path at the bottom.",
            new OpenEblCheckReport
            {
                Status = OpenEblCheckStatus.Passed,
                SubmissionChecksPassed = true,
                VerificationPassed = true,
                SubmissionErrorCount = 0,
                VerificationErrorCount = 0,
                DieBoundingBox = new OpenEblDieBoundingBox(605.0, 410.0),
            }),
        ("fail", "Fail: per-check outcome rows, typed error list with localized category chips and a µm location.",
            new OpenEblCheckReport
            {
                Status = OpenEblCheckStatus.Failed,
                SubmissionChecksPassed = false,
                VerificationPassed = true,
                SubmissionErrorCount = 3,
                VerificationErrorCount = 0,
                DieBoundingBox = new OpenEblDieBoundingBox(700.0, 450.0),
                Errors = new[]
                {
                    new OpenEblCheckError(OpenEblCheckCategories.TopCell, "Layout has 2 top cells."),
                    new OpenEblCheckError(
                        OpenEblCheckCategories.DieSize,
                        "Die size 700 um x 450 um exceeds allowed size 605 um x 410 um",
                        XMicrometers: 350.0, YMicrometers: 225.0),
                    new OpenEblCheckError(
                        OpenEblCheckCategories.LayerConformity,
                        "Layer 1111/0 is not a defined SiEPIC EBeam layer"),
                },
            }),
        ("toolchain-missing", "Toolchain missing: amber headline, actionable message, pip hint with copy button.",
            new OpenEblCheckReport
            {
                Status = OpenEblCheckStatus.ToolchainMissing,
                SubmissionChecksPassed = false,
                VerificationPassed = false,
                ToolchainMessage =
                    "The openEBL check needs a Python toolchain with klayout, the SiEPIC EBeam " +
                    "PDK and SiEPIC-Tools. Install them with: " + OpenEblPythonLocator.PipInstallHint,
            }),
    };

    /// <summary>Captures all three result states in English and German.</summary>
    [AvaloniaFact]
    public async Task CaptureResultStatesEnglishAndGerman()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1361");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            var index = 0;
            foreach (var state in States)
            {
                foreach (var languageCode in new[] { SupportedLanguage.English.Code, SupportedLanguage.German.Code })
                {
                    index++;
                    LocalizationService.Instance.SetLanguage(languageCode);
                    var filename = $"{index:00}-{state.Key}-{languageCode}.png";
                    await CaptureStateAsync(dir, filename, state.Caption, state.Report, manifest);
                }
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        manifest.Count.ShouldBe(6);
    }

    private static async Task CaptureStateAsync(
        string dir, string filename, string caption, OpenEblCheckReport report,
        List<ManifestEntry> manifest)
    {
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            OpenEblCheckTestDoubles.CanvasWithComponent(),
            new FakeGdsExportService(),
            FakeOpenEblSubmissionChecker.Returning(report));
        vm.Username = "jsmith";
        vm.DesignName = "MZI v2";
        await vm.ExportAndCheckCommand.ExecuteAsync(null);
        vm.HasResult.ShouldBeTrue($"the fake {filename} report must produce a result state");

        var dialog = new OpenEblCheckDialog { DataContext = vm };
        dialog.Show();
        try
        {
            PumpRenderLoop();
            var bitmap = dialog.CaptureRenderedFrame();
            bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");
            using (bitmap)
                ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(dir, filename));
            manifest.Add(new ManifestEntry(filename, caption));
        }
        finally
        {
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Advances the headless render timer so property changes actually paint before capture.</summary>
    private static void PumpRenderLoop()
    {
        for (int i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }
}

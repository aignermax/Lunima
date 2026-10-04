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
/// Visual documentation for the #1375 Lunima pre-flight of the "Check for openEBL…" dialog:
/// renders the dialog with a blocked connection (errors hold the run, "Check anyway" offered)
/// and with a bend-radius warning over the passed external result, in English and German.
/// PNGs + manifest.json land in <c>docs/pr-media/issue-1375/</c> (with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>) or a temp dir. Same pattern as
/// <see cref="Issue1361OpenEblCheckScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1375OpenEblPreflightScreenshotTests
{
    private sealed record ManifestEntry(string File, string Caption);

    private static readonly (string Key, string Caption)[] States =
    {
        ("preflight-errors",
            "Pre-flight errors: a blocked connection holds the external check — "
            + "fix list plus the 'Check anyway' escape hatch, no result yet."),
        ("preflight-warnings",
            "Pre-flight warnings only: the external check ran straight through, "
            + "the warning stays visible above the green result."),
    };

    private static readonly OpenEblCheckReport PassedReport = new()
    {
        Status = OpenEblCheckStatus.Passed,
        SubmissionChecksPassed = true,
        VerificationPassed = true,
        SubmissionErrorCount = 0,
        VerificationErrorCount = 0,
        DieBoundingBox = new OpenEblDieBoundingBox(605.0, 410.0),
    };

    /// <summary>Captures both pre-flight states in English and German.</summary>
    [AvaloniaFact]
    public async Task CapturePreflightStatesEnglishAndGerman()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1375");
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
                    await CaptureStateAsync(dir, filename, state.Caption, state.Key, manifest);
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
        manifest.Count.ShouldBe(4);
    }

    private static async Task CaptureStateAsync(
        string dir, string filename, string caption, string stateKey,
        List<ManifestEntry> manifest)
    {
        var canvas = stateKey == "preflight-errors"
            ? OpenEblCheckTestDoubles.CanvasWithBlockedConnection()
            : OpenEblCheckTestDoubles.CanvasWithWarningConnection();
        var vm = OpenEblCheckTestDoubles.CreateViewModel(
            canvas, new FakeGdsExportService(),
            FakeOpenEblSubmissionChecker.Returning(PassedReport));
        vm.Username = "jsmith";
        vm.DesignName = "MZI v2";
        await vm.ExportAndCheckCommand.ExecuteAsync(null);
        vm.PreflightIssues.ShouldNotBeEmpty($"the {filename} state must show the pre-flight list");

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

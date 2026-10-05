using System.Text.Json;
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
/// Visual documentation for #1390 (Design Checks speak the user's language): renders the
/// openEBL pre-flight dialog with a blocked connection in German, proving the pre-flight
/// finding now resolves through <c>DesignIssueFormatter</c> and its localization key
/// instead of showing the English <c>DesignIssue.Description</c>. PNG + manifest.json
/// land in <c>docs/pr-media/issue-1390/</c> (with <c>CAP_UPDATE_PR_MEDIA=1</c>) or a
/// temp dir. Same pattern as <see cref="Issue1375OpenEblPreflightScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1390PreflightGermanScreenshotTests
{
    private sealed record ManifestEntry(string File, string Caption);

    private static readonly OpenEblCheckReport PassedReport = new()
    {
        Status = OpenEblCheckStatus.Passed,
        SubmissionChecksPassed = true,
        VerificationPassed = true,
        SubmissionErrorCount = 0,
        VerificationErrorCount = 0,
        DieBoundingBox = new OpenEblDieBoundingBox(605.0, 410.0),
    };

    /// <summary>Captures the blocked-connection pre-flight in German — no English sentence left.</summary>
    [AvaloniaFact]
    public async Task CaptureBlockedPreflightInGerman()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1390");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        try
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguage.German.Code);

            var vm = OpenEblCheckTestDoubles.CreateViewModel(
                OpenEblCheckTestDoubles.CanvasWithBlockedConnection(),
                new FakeGdsExportService(),
                FakeOpenEblSubmissionChecker.Returning(PassedReport));
            vm.Username = "jsmith";
            vm.DesignName = "MZI v2";
            await vm.ExportAndCheckCommand.ExecuteAsync(null);
            vm.PreflightIssues.ShouldNotBeEmpty("the blocked connection must surface a pre-flight finding");
            vm.PreflightIssues.ShouldAllBe(
                issue => !issue.Message.Contains("Blocked connection"),
                "pre-flight findings must be rendered in German, not the English Description");

            const string filename = "01-preflight-blocked-de.png";
            var dialog = new OpenEblCheckDialog { DataContext = vm };
            dialog.Show();
            try
            {
                PumpRenderLoop();
                var bitmap = dialog.CaptureRenderedFrame();
                bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");
                using (bitmap)
                    ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(dir, filename));

                var manifest = new List<ManifestEntry>
                {
                    new(filename,
                        "openEBL-Pre-Flight in German: the blocked connection is reported through "
                        + "the DesignChecks localization key — no English sentence left."),
                };
                ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
                    JsonSerializer.Serialize(manifest,
                        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            finally
            {
                dialog.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
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

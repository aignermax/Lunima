using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CAP.Avalonia.Services.Localization;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>
/// Field repro (round 5): with the REAL MainWindow open, switching the UI language must
/// re-translate the visible texts live — the exact same control instances, no window
/// rebuild, no app restart. Covers the AXAML <c>{loc:Localize}</c> mass (toolbar
/// "Mode:" label as witness); the VM-side one-time <c>Translate</c> strings are covered
/// by <c>MainViewModelLocalizationTests</c>.
/// Runs in the parallelization-free LocalizationSingleton collection because it mutates
/// the process-wide language.
/// </summary>
[Trait("Category", "UiFlows")]
// Boots the real MainWindow through the input pipeline — too heavy for local default
// runs (CI covers it, the local runners exclude Category=Slow).
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class UiFlowLanguageSwitchTests
{
    [AvaloniaFact]
    public void SwitchingLanguage_UpdatesOpenMainWindowLive()
    {
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        using var host = new UiFlowTestHost();
        try
        {
            // The Run label names the workspace action (a new design opens in Layout).
            var runLabel = UiInput.Descendants<TextBlock>(host.Window)
                .First(t => t.Text == "Run checks");
            var layoutSegment = UiInput.Descendants<Button>(host.Window)
                .First(b => Equals(b.Content, "Layout"));
            var opticsSegment = UiInput.Descendants<Button>(host.Window)
                .First(b => Equals(b.Content, "Optics"));

            LocalizationService.Instance.SetLanguage("es");
            UiInput.RunJobs();

            runLabel.Text.ShouldBe("Comprobar",
                "the workspace-dependent Run label must re-read live on a language switch");
            opticsSegment.Content.ShouldBe("Óptica",
                "an open window's {loc:Localize} bindings must re-read live on a language switch");
            layoutSegment.Content.ShouldBe("Diseño");
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
            UiInput.RunJobs();
        }
    }
}

using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis;
using CAP.Avalonia.Views.Panels;
using CAP.Avalonia.Services.Localization;
using CAP_Core.Analysis.LogicAnalysis;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for #1445 (logic level report charges chiplet edge-coupler
/// link loss): renders the Logic panel over the misaligned two-chiplet journey design
/// (<see cref="LogicAcrossChipletLinkJourneyDesign"/>) so the new link-loss warning
/// row (<c>LogicPanel.FanOutWarning.LinkLine</c> / <c>LinkSplitLine</c>) is captured
/// in English and German. The warning wraps the real <see cref="LogicFanOutWarning"/>
/// produced by the production assembler — no fabricated levels. PNGs + manifest.json
/// land in <c>docs/pr-media/issue-1445/</c> (only with <c>CAP_UPDATE_PR_MEDIA=1</c>).
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1445LinkLossWarningScreenshotTests
{
    private const double PanelWidth = 460;
    private const double PanelHeight = 620;
    private const double AxialShiftMicrometers = 10.0;
    private const double LateralShiftMicrometers = 2.0;
    private const string ExpectedLinkName = "'Chiplet A' / 'Chiplet B'";

    private sealed record ManifestEntry(string File, string Caption);

    /// <summary>
    /// Captures the Logic panel's link-loss warning on the misaligned journey design
    /// in English and German and asserts the long link name renders unclipped.
    /// </summary>
    [AvaloniaFact]
    public async Task CaptureLinkLossWarningInEnglishAndGerman()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1445");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);

        var warning = await BuildMisalignedLinkWarningAsync();
        warning.LinkDisplayName.ShouldBe(ExpectedLinkName);

        var manifest = new List<ManifestEntry>();
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        var vm = MainViewModelTestHelper.CreateMainViewModel();
        var window = new Window { Width = PanelWidth, Height = PanelHeight, Content = new LogicPanel { DataContext = vm } };
        window.Show();
        try
        {
            CaptureLanguage(window, vm, warning, "en", "01-logic-link-warning-en.png",
                "Logic panel on the misaligned two-chiplet journey design (English): the "
                + "link warning names 'Chiplet A' / 'Chiplet B' and the level report shows "
                + "the delivered 1-level after the link's coupling loss.", dir, manifest);
            CaptureLanguage(window, vm, warning, "de", "02-logic-link-warning-de.png",
                "Same warning in German: LinkLine and LinkSplitLine resolve through the "
                + "localization keys — the long link name stays unclipped.", dir, manifest);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(
            Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));
        manifest.Count.ShouldBe(2);
    }

    /// <summary>
    /// Builds the journey design, misaligns chiplet B by the #1257 offset through the
    /// canvas move path and returns the single link-loss warning the production
    /// assembler reports at the journey wavelength.
    /// </summary>
    private static async Task<LogicFanOutWarning> BuildMisalignedLinkWarningAsync()
    {
        var design = await LogicAcrossChipletLinkJourneyDesign.BuildComposedAsync();
        var chipletBVm = design.Canvas.Components.Single(c => c.Component == design.ChipletB);
        design.Canvas.BeginDragComponent(chipletBVm);
        design.Canvas.MoveComponent(chipletBVm, AxialShiftMicrometers, LateralShiftMicrometers);
        new CommandManager().ExecuteCommand(new GroupMoveCommand(
            design.Canvas, new[] { chipletBVm }, AxialShiftMicrometers, LateralShiftMicrometers));
        design.Canvas.EndDragComponent(chipletBVm);
        await design.Canvas.RecalculateRoutesAsync();

        var network = await new LogicNetworkAssembler().AssembleAsync(
            design.Canvas.Components.Select(c => c.Component).ToList(),
            design.Canvas.ConnectionManager.Connections,
            LogicAcrossChipletLinkJourneyDesign.WavelengthNm);
        return network.FanOutWarnings.ShouldHaveSingleItem(
            "the misaligned link must produce exactly one link-loss warning");
    }

    /// <summary>
    /// Shows the warning in the given language, asserts the link rows render without
    /// clipping the long link name, and saves the capture.
    /// </summary>
    private static void CaptureLanguage(
        Window window,
        CAP.Avalonia.ViewModels.MainViewModel vm,
        LogicFanOutWarning warning,
        string language,
        string filename,
        string caption,
        string dir,
        List<ManifestEntry> manifest)
    {
        LocalizationService.Instance.SetLanguage(language);
        var logic = vm.RightPanel.Logic;
        logic.FanOutWarnings.Clear();
        var row = new LogicFanOutWarningViewModel(warning);
        logic.FanOutWarnings.Add(row);
        logic.HasFanOutWarnings = true;
        PumpRenderLoop();

        row.WarningText.ShouldContain(ExpectedLinkName,
            customMessage: $"the {language} warning line must name the link");
        AssertRendersUnclipped(window, row.WarningText, language);
        AssertRendersUnclipped(window, row.SplitLine, language);

        var bitmap = window.CaptureRenderedFrame();
        bitmap.ShouldNotBeNull($"CaptureRenderedFrame returned null for {filename}");
        using (bitmap)
            ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(dir, filename));
        manifest.Add(new ManifestEntry(filename, caption));
    }

    /// <summary>
    /// The rendered TextBlock showing <paramref name="text"/> must wrap and fully fit
    /// its laid-out lines — a clipped long link name would fail here.
    /// </summary>
    private static void AssertRendersUnclipped(Window window, string text, string language)
    {
        var block = window.GetVisualDescendants().OfType<TextBlock>()
            .SingleOrDefault(t => t.Text == text);
        block.ShouldNotBeNull($"the {language} Logic panel must render '{text}'");
        block!.TextWrapping.ShouldBe(TextWrapping.Wrap);
        block.Bounds.Width.ShouldBeLessThanOrEqualTo(PanelWidth,
            $"'{text}' must stay inside the {language} panel width");
        block.TextLayout.Height.ShouldBeLessThanOrEqualTo(block.Bounds.Height + 0.5,
            $"'{text}' must not be vertically clipped in {language}");
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

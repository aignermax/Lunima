using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the Logic tab's per-cell collapse (issue #1399): renders
/// the shipped hierarchical <c>Logic Gate RAM 2x4.lun</c> — loaded through the real
/// load path, driven through the same write/read demo as #1389 (5 into word 0, 10 into
/// word 1, then LOAD low reading word 1) — with the two word-cell instances collapsed
/// under their headers in English and in German, plus one English capture with
/// <c>CELL0</c> expanded showing the prefix-stripped gate rows. PNGs + manifest.json
/// land in <c>docs/pr-media/issue-1399/</c> when refreshed with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp dir. Same pattern as
/// <see cref="Issue1389Ram2x4ScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1399CellGroupScreenshotTests
{
    private const int DockWindowWidth = 1200;
    private const int DockWindowHeight = 700;
    private const int CaptureAttempts = 3;

    [AvaloniaFact]
    public async Task CaptureRam2x4CellGroups()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1399");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate RAM 2x4.lun");

        foreach (var language in new[] { SupportedLanguage.English, SupportedLanguage.German })
            await CaptureLogicTab(path, dir, language, expandCell0: false, manifest);
        await CaptureLogicTab(path, dir, SupportedLanguage.English, expandCell0: true, manifest);

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        manifest.Count.ShouldBe(3);
    }

    /// <summary>
    /// Drives the RAM demo through the real panel — write 5 into word 0, write 10 into
    /// word 1, then LOAD low with A=1 reading word 1 — and captures the dock Logic tab
    /// with the cell groups collapsed (or CELL0 expanded).
    /// </summary>
    private static async Task CaptureLogicTab(
        string path, string dir, SupportedLanguage language, bool expandCell0, List<object> manifest)
    {
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(language.Code);
        try
        {
            var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
            var vm = Helpers.MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
            var logic = vm.RightPanel.Logic;
            await logic.BuildNetworkCommand.ExecuteAsync(null);
            logic.HasNetwork.ShouldBeTrue(logic.StatusText);

            void Set(string pin, bool on) => logic.Inputs.Single(i => i.PinName == pin).IsOn = on;

            // Write 5 (D0+D2) into word 0: A=0, LOAD=1, one clock commit.
            Set("D0", true); Set("D2", true); Set("LOAD", true);
            logic.StepClockCommand.Execute(null);
            Set("LOAD", false); Set("D0", false); Set("D2", false);

            // Write 10 (D1+D3) into word 1: A=1, LOAD=1, one clock commit.
            Set("A", true); Set("D1", true); Set("D3", true); Set("LOAD", true);
            logic.StepClockCommand.Execute(null);

            // Read word 1 back: LOAD=0, A stays 1 — Q reads 10.
            Set("LOAD", false); Set("D1", false); Set("D3", false);
            logic.OutputRows.OfType<LogicSignalBusOutputViewModel>().Single(b => b.Prefix == "Q")
                .DecimalValue.ShouldBe(10, "after the write/read demo the read bus Q shows word 1 = 10");

            var cell0 = logic.OutputRows.OfType<LogicCellGroupViewModel>().Single(g => g.CellName == "CELL0");
            if (expandCell0)
                cell0.ToggleExpandedCommand.Execute(null);

            vm.BottomPanel.Analysis.IsVisible = true;
            vm.BottomPanel.Analysis.SetDockHeight(560);
            var dock = new AnalysisDockPanel { DataContext = vm };
            var dockWindow = new Window { Width = DockWindowWidth, Height = DockWindowHeight, Content = dock };
            dockWindow.Show();
            try
            {
                vm.BottomPanel.Analysis.OpenLogic();
                Dispatcher.UIThread.RunJobs();

                // Scroll inside the tab so the capture shows the outputs: the collapsed
                // cell headers (or the expanded CELL0 rows) instead of the warnings box.
                var logicPanel = dock.GetVisualDescendants().OfType<LogicPanel>().Single();
                var outputsList = logicPanel.GetVisualDescendants().OfType<ItemsControl>()
                    .First(ic => ReferenceEquals(ic.ItemsSource, logic.OutputRows));
                outputsList.BringIntoView();
                Dispatcher.UIThread.RunJobs();

                var file = expandCell0
                    ? $"ram2x4-cell0-expanded-{language.Code}.png"
                    : $"ram2x4-cell-groups-collapsed-{language.Code}.png";
                CaptureWithRetry(dockWindow, Path.Combine(dir, file));
                manifest.Add(new
                {
                    file,
                    caption = expandCell0
                        ? $"The Logic tab with CELL0 expanded ({language.Code}): the cell's gate rows show " +
                          "with the prefix stripped (REG00.Y, not CELL0/REG00.Y), so the column stays narrow."
                        : $"The Logic tab after the write/read demo ({language.Code}): the two word-cell " +
                          "instances collapse under one header each (CELL0 — 4 registers, … gates); the named " +
                          "read bus Q and the register readout stay on top, always visible (issue #1399).",
                });
            }
            finally
            {
                dockWindow.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            LocalizationService.Instance.SetLanguage(previousLanguage);
        }
    }

    /// <summary>Captures the window, pumping the dispatcher and keeping the last good frame.</summary>
    private static void CaptureWithRetry(Window window, string path)
    {
        WriteableBitmap? bitmap = null;
        for (var attempt = 0; attempt < CaptureAttempts; attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            var frame = window.CaptureRenderedFrame();
            if (frame == null)
                continue;
            bitmap?.Dispose();
            bitmap = frame;
        }

        bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after {CaptureAttempts} attempts for {path}");
        using (bitmap)
        {
            ScreenshotArtifacts.SavePng(bitmap, path);
        }
    }
}

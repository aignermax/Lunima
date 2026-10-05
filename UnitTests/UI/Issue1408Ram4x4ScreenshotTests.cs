using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Analysis.LogicAnalysis.BusView;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.Views.Panels;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using Xunit;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation for the #1408 example <c>Logic Gate RAM 4x4.lun</c> (rung 5 of
/// the memory ladder — the ISA-sized data memory: the re-floorplanned word cell from
/// #1400 instanced four times): renders the shipped file — loaded through the real load
/// path — as one canvas overview (<see cref="Issue1158SceneControl"/>) plus the dock
/// Logic tab after a real write/read demo (3 into word 0, 12 into word 3, then LOAD low
/// reading word 3), in English and in German. PNGs + manifest.json land in
/// <c>docs/pr-media/issue-1408/</c> when refreshed with <c>CAP_UPDATE_PR_MEDIA=1</c>,
/// otherwise in a temp dir. Same pattern as <see cref="Issue1389Ram2x4ScreenshotTests"/>.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class Issue1408Ram4x4ScreenshotTests
{
    private const int MinDistinctSampledColors = 4;
    private const int SampleGridSize = 64;
    private const int DockWindowWidth = 1200;
    private const int DockWindowHeight = 700;
    private const int CaptureAttempts = 3;

    [AvaloniaFact]
    public async Task CaptureRam4x4Example()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("issue-1408");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();

        var path = Path.Combine(
            ExampleDesignFilesTests.ExamplesDirectory(), "Logic Gate RAM 4x4.lun");
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);

        var bounds = ComputeSceneBounds(canvas).Inflate(60);
        Capture(new Issue1158SceneControl(canvas, bounds), bounds, dir,
            "ram4x4-overview.png", canvasWidthPixels: 2000);
        manifest.Add(new
        {
            file = "ram4x4-overview.png",
            caption = "The shipped 'Logic Gate RAM 4x4' example (issue #1408): the ISA-sized hierarchical RAM — " +
                "the 2-bit address decode, the LOAD/data copy trees and the read-MUX combines at top level, " +
                "the four word-cell instances CELL0–CELL3 routed once and frozen; only the inter-cell wires " +
                "route at top level.",
        });

        foreach (var language in new[] { SupportedLanguage.English, SupportedLanguage.German })
            await CaptureLogicTabAfterWriteRead(path, dir, language, manifest);

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        manifest.Count.ShouldBe(3);
    }

    /// <summary>
    /// Drives the RAM demo through the real panel — write 3 into word 0, write 12 into
    /// word 3, then LOAD low with A0=A1=1 reading word 3 — and captures the dock Logic
    /// tab in the given language.
    /// </summary>
    private static async Task CaptureLogicTabAfterWriteRead(
        string path, string dir, SupportedLanguage language, List<object> manifest)
    {
        var previousLanguage = LocalizationService.Instance.ActiveLanguageCode;
        LocalizationService.Instance.SetLanguage(language.Code);
        try
        {
            var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(path);
            var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
            var logic = vm.RightPanel.Logic;
            await logic.BuildNetworkCommand.ExecuteAsync(null);
            logic.HasNetwork.ShouldBeTrue(logic.StatusText);

            void Set(string pin, bool on) => logic.Inputs.Single(i => i.PinName == pin).IsOn = on;

            // Write 3 (D0+D1) into word 0: A0=A1=0, LOAD=1, one clock commit.
            Set("D0", true); Set("D1", true); Set("LOAD", true);
            logic.StepClockCommand.Execute(null);
            Set("LOAD", false); Set("D0", false); Set("D1", false);

            // Write 12 (D2+D3) into word 3: A0=A1=1, LOAD=1, one clock commit.
            Set("A0", true); Set("A1", true); Set("D2", true); Set("D3", true); Set("LOAD", true);
            logic.StepClockCommand.Execute(null);

            // Read word 3 back: LOAD=0, A0/A1 stay 1 — Q reads 12.
            Set("LOAD", false); Set("D2", false); Set("D3", false);
            logic.OutputRows.OfType<LogicSignalBusOutputViewModel>().Single(b => b.Prefix == "Q")
                .DecimalValue.ShouldBe(12, "after the write/read demo the read bus Q shows word 3 = 12");

            vm.BottomPanel.Analysis.IsVisible = true;
            vm.BottomPanel.Analysis.SetDockHeight(560);
            var dock = new AnalysisDockPanel { DataContext = vm };
            var dockWindow = new Window { Width = DockWindowWidth, Height = DockWindowHeight, Content = dock };
            dockWindow.Show();
            try
            {
                vm.BottomPanel.Analysis.OpenLogic();
                Dispatcher.UIThread.RunJobs();

                // Scroll inside the tab so the capture shows the outputs and the register
                // readout (the fan-out warning box fills the top of the tab).
                var logicPanel = dock.GetVisualDescendants().OfType<LogicPanel>().Single();
                var registersList = logicPanel.GetVisualDescendants().OfType<ItemsControl>()
                    .First(ic => ReferenceEquals(ic.ItemsSource, logic.RegisterStates));
                registersList.BringIntoView();
                Dispatcher.UIThread.RunJobs();

                var file = $"ram4x4-logic-panel-{language.Code}.png";
                CaptureWithRetry(dockWindow, Path.Combine(dir, file));
                manifest.Add(new
                {
                    file,
                    caption = $"The Logic tab after the write/read demo ({language.Code}): 3 was stored in " +
                        "word 0 and 12 in word 3 (one LOAD + clock each), then LOAD went low — with A0=A1=1 " +
                        "the read bus Q shows 12; the sixteen register bits of the four instanced word " +
                        "cells are listed in the register readout.",
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

    private static Rect ComputeSceneBounds(DesignCanvasViewModel canvas)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var comp in canvas.Components)
        {
            minX = Math.Min(minX, comp.X);
            minY = Math.Min(minY, comp.Y);
            maxX = Math.Max(maxX, comp.X + comp.Width);
            maxY = Math.Max(maxY, comp.Y + comp.Height);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static void Capture(Issue1158SceneControl scene, Rect world, string outputDir,
        string filename, int canvasWidthPixels)
    {
        scene.Width = canvasWidthPixels;
        scene.Height = canvasWidthPixels * world.Height / world.Width;
        var window = new Window
        {
            Width = scene.Width,
            Height = scene.Height,
            Content = scene,
            Background = Brushes.Black,
        };
        window.Show();
        try
        {
            WriteableBitmap? bitmap = null;
            for (var attempt = 0; attempt < CaptureAttempts && bitmap == null; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                bitmap = window.CaptureRenderedFrame();
            }
            bitmap.ShouldNotBeNull($"CaptureRenderedFrame stayed null after {CaptureAttempts} attempts for {filename}");
            using (bitmap)
            {
                CountDistinctSampledColors(bitmap).ShouldBeGreaterThan(MinDistinctSampledColors,
                    $"Near-blank render for {filename} — likely a missing Skia setup.");
                ScreenshotArtifacts.SavePng(bitmap!, Path.Combine(outputDir, filename));
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Captures the window, pumping the dispatcher and keeping the last good frame.</summary>
    private static void CaptureWithRetry(Window window, string path)
    {
        WriteableBitmap? bitmap = null;
        for (int attempt = 0; attempt < CaptureAttempts; attempt++)
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

    private static int CountDistinctSampledColors(WriteableBitmap bitmap)
    {
        using var fb = bitmap.Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        if (width <= 0 || height <= 0) return 0;

        int stepX = Math.Max(1, width / SampleGridSize);
        int stepY = Math.Max(1, height / SampleGridSize);
        var colors = new HashSet<int>();
        for (int y = 0; y < height; y += stepY)
        {
            var rowAddr = fb.Address + y * fb.RowBytes;
            for (int x = 0; x < width; x += stepX)
                colors.Add(Marshal.ReadInt32(rowAddr, x * 4));
        }
        return colors.Count;
    }
}

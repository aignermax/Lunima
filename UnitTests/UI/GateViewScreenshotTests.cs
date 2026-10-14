using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.Controls;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;
using AvaloniaFactAttribute = Avalonia.Headless.XUnit.AvaloniaFactAttribute;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation of the Logic workspace's gate view: the shipped Half Adder,
/// 2-bit Counter and ALU + RAM + ACC examples rendered through the real
/// <see cref="DesignCanvas"/> as gate symbols and signal nets — before Run (no levels)
/// and after the logic network is built (nets coloured 0/1 like the Logic tab).
/// PNGs + manifest.json land in <c>docs/pr-media/gate-view/</c> with
/// <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp dir.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Collection("LocalizationSingleton")]
public class GateViewScreenshotTests
{
    private const int CaptureWidth = 1600;
    private const double FrameMargin = 140;

    [AvaloniaFact]
    public async Task CaptureGateViews()
    {
        var dir = ScreenshotArtifacts.ResolvePrMediaDirectory("gate-view");
        Directory.CreateDirectory(dir);
        foreach (var stale in Directory.GetFiles(dir, "*.png"))
            File.Delete(stale);
        var manifest = new List<object>();
        var localization = CAP.Avalonia.Services.Localization.LocalizationService.Instance;
        var previousLanguage = localization.ActiveLanguageCode;
        localization.SetLanguage("en");
        try
        {
            await CaptureExample(dir, manifest, "Logic Gate Half Adder.lun", "half-adder", build: true,
                setInputs: vm => SetInput(vm, "A", true));
            await CaptureExample(dir, manifest, "Logic Gate Counter 2-bit.lun", "counter-2bit", build: true,
                setInputs: vm => SetInput(vm, "S̄", true));
            await CaptureExample(dir, manifest, "Logic Gate ALU + RAM + ACC.lun", "alu-ram-acc", build: false, setInputs: null,
                detail: new Rect(250, 0, 4900, 1050));
        }
        finally
        {
            localization.SetLanguage(previousLanguage);
        }

        ScreenshotArtifacts.WriteText(Path.Combine(dir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        Directory.GetFiles(dir, "*.png").Length.ShouldBeGreaterThanOrEqualTo(5);
    }

    private static async Task CaptureExample(
        string dir, List<object> manifest, string file, string slug, bool build, Action<MainViewModel>? setInputs,
        Rect? detail = null)
    {
        var canvas = await LogicGateHalfAdderExampleTests.LoadCanvas(
            Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), file));
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        await vm.GateView.RebuildNowAsync();
        vm.GateView.IsShown.ShouldBeTrue();
        vm.GateView.Graph.Nodes.ShouldNotBeEmpty();

        var world = WorldBounds(vm);
        Capture(vm, world, Path.Combine(dir, $"{slug}-gates.png"));
        manifest.Add(new { file = $"{slug}-gates.png", caption = $"{file}: gate view before Run (no levels yet)." });
        if (detail is { } crop)
        {
            Capture(vm, crop, Path.Combine(dir, $"{slug}-detail.png"));
            manifest.Add(new { file = $"{slug}-detail.png", caption = $"{file}: zoomed into the first adder stages." });
        }

        if (build)
        {
            await vm.RightPanel.Logic.BuildNetworkCommand.ExecuteAsync(null);
            vm.RightPanel.Logic.HasNetwork.ShouldBeTrue(vm.RightPanel.Logic.StatusText);
            setInputs?.Invoke(vm);
            Capture(vm, world, Path.Combine(dir, $"{slug}-levels.png"));
            manifest.Add(new { file = $"{slug}-levels.png", caption = $"{file}: after Run — nets coloured by live level." });
        }
    }

    private static void SetInput(MainViewModel vm, string name, bool on)
    {
        var input = vm.RightPanel.Logic.Inputs.FirstOrDefault(i => i.PinName == name);
        if (input != null)
            input.IsOn = on;
    }

    /// <summary>The area the gate symbols occupy, with room for flags and labels.</summary>
    internal static Rect WorldBounds(MainViewModel vm)
    {
        var rects = vm.GateView.Graph.Nodes
            .Select(n => CAP.Avalonia.Controls.Rendering.ComponentGroupRenderer.CalculateGroupBounds(n.Group))
            .ToList();
        var union = rects.Aggregate((a, b) => a.Union(b));
        return union.Inflate(FrameMargin);
    }

    private static void Capture(MainViewModel vm, Rect world, string path)
    {
        var height = (int)Math.Clamp(CaptureWidth * world.Height / world.Width, 300, 1400);
        var zoom = Math.Min(CaptureWidth / world.Width, height / world.Height);
        var control = new DesignCanvas { ViewModel = vm.Canvas, MainViewModel = vm, Zoom = zoom };
        vm.Canvas.PanX = -world.X * zoom + (CaptureWidth - world.Width * zoom) / 2;
        vm.Canvas.PanY = -world.Y * zoom + (height - world.Height * zoom) / 2;
        var window = new Window { Width = CaptureWidth, Height = height, Content = control, Background = Brushes.Black };
        window.Show();
        try
        {
            WriteableBitmap? bitmap = null;
            for (var attempt = 0; attempt < 3 && bitmap == null; attempt++)
            {
                Dispatcher.UIThread.RunJobs();
                bitmap = window.CaptureRenderedFrame();
            }
            bitmap.ShouldNotBeNull();
            using (bitmap)
                ScreenshotArtifacts.SavePng(bitmap!, path);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}

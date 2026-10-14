using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Process;
using Shouldly;
using UnitTests.Integration;
using UnitTests.UI.Showcase;
using Xunit;

namespace UnitTests.UI;

/// <summary>
/// Visual documentation of the per-workspace tool sets: the left panel (rail + pane) in Layout
/// (full library), Optics (instruments + hint), Logic (gate library + hint) and System
/// (Chiplets). Opt-in (UI_SHOT_DIR); PNGs land in <c>docs/pr-media/workspace-tool-sets/</c>
/// with <c>CAP_UPDATE_PR_MEDIA=1</c>, otherwise in a temp directory.
/// </summary>
[Trait("Category", "UiScreenshots")]
[Trait("Category", "Slow")]
[Collection("LocalizationSingleton")]
public class WorkspaceToolSetsScreenshotTests
{
    private const string ExampleFile = "Mach-Zehnder Interferometer.lun";
    private const double Scale = 2.0;
    private const double BaseDpi = 96;

    [AvaloniaFact]
    public async Task CaptureLeftPanelPerWorkspace()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UI_SHOT_DIR")))
            return;
        var outputDir = ScreenshotArtifacts.ResolvePrMediaDirectory("workspace-tool-sets");
        Directory.CreateDirectory(outputDir);

        var vm = ShowcaseCircuit.CreateStagedViewModel();
        var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), ExampleFile);
        (await vm.FileOperations.OpenDesignAsCopyAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        vm.FileOperations.SetActiveProcess(ActiveProcessSelection.Playground(), markDirty: false);
        var window = ShowcaseCircuit.BootMainWindow(vm);
        try
        {
            vm.Home.IsHomeVisible = false;
            foreach (var workspace in new[] { Workspace.Layout, Workspace.Optics, Workspace.Logic, Workspace.System })
            {
                vm.WorkspaceSwitch.Current = workspace;
                Dispatcher.UIThread.RunJobs();
                CaptureLeftPanel(window, Path.Combine(outputDir, $"left-panel-{workspace.ToString().ToLowerInvariant()}.png"));
            }
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void CaptureLeftPanel(Window window, string path)
    {
        ScreenshotArtifacts.SettleAnimations();
        Dispatcher.UIThread.RunJobs();
        var panel = window.FindControl<Border>("LeftPanelBorder").ShouldNotBeNull();
        var size = new PixelSize((int)(panel.Bounds.Width * Scale), (int)(panel.Bounds.Height * Scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(BaseDpi * Scale, BaseDpi * Scale));
        bitmap.Render(panel);
        ScreenshotArtifacts.SavePng(bitmap, path);
    }
}

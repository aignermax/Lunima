using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Export.PythonEnvironmentManager;
using CAP_Core.Export.PythonEnvironmentManager;
using UnitTests.Export.PythonEnvironmentManager;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class VisualLearningJourneyTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task Novice_BuildsCircuitByFollowingActualTargets(string language)
    {
        await using var setup = new PendingPythonSetup();
        using var host = new UiFlowTestHost(pythonStartup: setup.Startup, preferences: setup.Preferences);
        var vm = host.Vm;
        var window = host.Window;
        window.Width = 1280;
        window.Height = 800;
        LocalizationService.Instance.SetLanguage(language);
        setup.Start();
        await vm.Home.LearnTutorialCommand.ExecuteAsync(null);
        Pump();
        Assert.Equal(0, vm.Tutorial.CurrentStepIndex);
        Assert.NotNull(vm.Tutorial.CurrentTargetItem);
        var canvas = UiInput.Descendants<DesignCanvas>(window).First();
        var overlay = UiInput.Descendants<TourAnchorOverlay>(window).Single(o => o.Card is CAP.Avalonia.Views.Panels.FirstStepsTutorialPanel);

        async Task RefreshTarget()
        {
            var name = overlay.TargetName;
            overlay.TargetName = null;
            overlay.TargetName = name;
            Pump();
            var pointer = overlay.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "GuidePointer");
            for (var attempt = 0; attempt < 30 && !pointer.IsVisible; attempt++)
            {
                await Task.Delay(20);
                Pump();
            }
            Assert.True(pointer.IsVisible, $"No demonstrated target for stage {vm.Tutorial.CurrentStepIndex}");
        }

        Point WorldPoint(Point world) => canvas.TranslatePoint(new Point(
            world.X * canvas.Zoom + vm.Canvas.PanX, world.Y * canvas.Zoom + vm.Canvas.PanY), window)!.Value;

        async Task SelectIndicatedPart()
        {
            var item = vm.Tutorial.CurrentTargetItem;
            Assert.True(item != null, $"Stage {vm.Tutorial.CurrentStepIndex}; parts: {string.Join(",", vm.Canvas.Components.Select(c => c.Component.HumanReadableName + ":" + c.TemplatePdkSource))}; library: {string.Join(",", vm.LeftPanel.FilteredTemplates.Select(t => t.Name + ":" + t.PdkSource))}");
            host.LibraryListBox.ScrollIntoView(item);
            Pump();
            await RefreshTarget();
            var row = host.LibraryListBox.ContainerFromItem(item);
            Assert.NotNull(row);
            var detectorTitle = row.GetVisualDescendants().OfType<TextBlock>()
                .SingleOrDefault(text => text.Text == "Photodetector");
            if (item is CAP.Avalonia.ViewModels.Library.ComponentTemplate template && template.Name == "Photodetector")
            {
                Assert.NotNull(detectorTitle);
                Assert.Single(detectorTitle.TextLayout.TextLines);
            }
            UiInput.Click(window, row, .35);
            Pump();
        }

        await RefreshTarget();
        var realizedRows = host.LibraryListBox.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(row => row.IsEffectivelyVisible && row.Bounds.Height > 0).ToArray();
        for (var first = 0; first < realizedRows.Length; first++)
            for (var second = first + 1; second < realizedRows.Length; second++)
                Assert.False(realizedRows[first].Bounds.Intersects(realizedRows[second].Bounds),
                    $"Library rows overlap: index {host.LibraryListBox.IndexFromContainer(realizedRows[first])} {realizedRows[first].Bounds} / index {host.LibraryListBox.IndexFromContainer(realizedRows[second])} {realizedRows[second].Bounds}; realized {realizedRows.Length} / items {host.LibraryListBox.ItemCount}");
        Save(window, language, "select-source");
        await SelectIndicatedPart();
        Assert.Equal(1, vm.Tutorial.CurrentStepIndex);
        Assert.NotNull(vm.Tutorial.CurrentCanvasPoint);
        await RefreshTarget();
        Save(window, language, "place-source");
        UiInput.ClickAt(window, WorldPoint(vm.Tutorial.CurrentCanvasPoint!.Value));
        Pump();
        Assert.Equal(2, vm.Tutorial.CurrentStepIndex);
        await SelectIndicatedPart();
        Assert.Equal(3, vm.Tutorial.CurrentStepIndex);
        UiInput.ClickAt(window, WorldPoint(vm.Tutorial.CurrentCanvasPoint!.Value));
        Pump();
        Assert.Equal(4, vm.Tutorial.CurrentStepIndex);
        // Repeated placement remains armed while the pointer is still on the canvas.
        Assert.True(canvas.InteractionState.ShowPlacementPreview);
        UiInput.Click(window, UiInput.Named<Button>(window, "ConnectModeButton")!);
        Pump();
        Assert.Equal(5, vm.Tutorial.CurrentStepIndex);
        Assert.False(canvas.InteractionState.ShowPlacementPreview);
        Assert.Null(canvas.InteractionState.PlacementPreviewTemplate);
        await RefreshTarget();
        Save(window, language, "connect-pins");
        var start = vm.Tutorial.CurrentTargetPin!.GetAbsolutePosition();
        var end = vm.Tutorial.ConnectionTargetPin!.GetAbsolutePosition();
        UiInput.DragMouse(window, WorldPoint(new Point(start.x, start.y)), WorldPoint(new Point(end.x, end.y)));
        for (var i = 0; i < 100 && vm.Tutorial.CurrentStepIndex != 6; i++)
        {
            await Task.Delay(10);
            Pump();
        }
        Assert.Equal(6, vm.Tutorial.CurrentStepIndex);
        Assert.Single(vm.Canvas.Connections);
        await RefreshTarget();
        Save(window, language, "open-optics");
        UiInput.Click(window, UiInput.Named<Button>(window, "OpticsWorkspaceButton")!);
        Pump();
        Assert.Equal(7, vm.Tutorial.CurrentStepIndex);
        await RefreshTarget();
        Save(window, language, "simulate");
        UiInput.Click(window, UiInput.Named<Button>(window, "RunSimulationButton")!);
        Assert.NotNull(vm.RunWorkspaceCommand.ExecutionTask);
        await vm.RunWorkspaceCommand.ExecutionTask;
        Pump();
        Assert.True(vm.Tutorial.IsCompleted);
        Assert.False(vm.Tutorial.IsActive);
        Assert.True(vm.ShowLearningSuccess);
        var flow = vm.Canvas.PowerFlowVisualizer.GetFlowForConnection(Assert.Single(vm.Canvas.Connections).Connection.Id);
        Assert.NotNull(flow);
        Assert.True(double.IsFinite(flow.OutputPower) && flow.OutputPower > 0, vm.StatusText);
        Assert.False(setup.Pending!.IsCompleted, "The whole circuit journey must work while Python setup is pending.");
        Assert.True(UiInput.Named<StackPanel>(window, "PythonStartupProgress")!.IsEffectivelyVisible);
    }

    private sealed class PendingPythonSetup : IAsyncDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "lunima-learning-python-" + Guid.NewGuid().ToString("N"));
        public UserPreferencesService Preferences { get; }
        public PythonStartupViewModel Startup { get; }
        public Task? Pending { get; private set; }

        public PendingPythonSetup()
        {
            Directory.CreateDirectory(_folder);
            Preferences = new UserPreferencesService(Path.Combine(_folder, "prefs.json"));
            var registry = new PythonEnvironmentRegistry(Path.Combine(_folder, "envs.json"));
            Startup = new PythonStartupViewModel(registry, Preferences, new FakePythonProvisioner(), _folder);
        }

        public void Start() => Pending = Startup.StartOnceAsync();

        public async ValueTask DisposeAsync()
        {
            Startup.Cancel();
            if (Pending != null) await Pending;
            Directory.Delete(_folder, true);
        }
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Window window, string language, string step)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "visual-learning");
        Directory.CreateDirectory(directory);
        Pump();
        using var frame = window.CaptureRenderedFrame();
        var path = Path.Combine(directory, $"{language}-{step}.png");
        if (frame != null)
            frame.Save(path);
        else
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
            bitmap.Render(window);
            bitmap.Save(path);
        }
        File.WriteAllText(Path.ChangeExtension(path, ".capture.txt"), frame != null ? "compositor" : "bitmap fallback");
    }
}

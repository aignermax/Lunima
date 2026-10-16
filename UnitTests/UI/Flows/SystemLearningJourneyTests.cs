using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views.Panels;
using Moq;
using UnitTests.Integration;
using UnitTests.UI.Showcase;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class SystemLearningJourneyTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task ActualButtons_MoveCheckAlignAndCreateDie(string language)
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var window = host.Window;
        window.Width = 1280;
        window.Height = 800;
        LocalizationService.Instance.SetLanguage(language);
        await TwoChipletsExampleLoader.LoadAsync(vm.Canvas, ConnectChipletsTourViewModel.ExampleFileName);
        vm.WorkspaceSwitch.Current = Workspace.Layout;
        var parts = vm.Canvas.Components;
        ShowcaseCircuit.SetView(window, vm, (parts.Min(c => c.X), parts.Min(c => c.Y),
            parts.Max(c => c.X + c.Width) - parts.Min(c => c.X), parts.Max(c => c.Y + c.Height) - parts.Min(c => c.Y)));
        vm.Canvas.ShowPowerFlow = false;
        UiInput.RunJobs();
        var tour = vm.ConnectChipletsTour;
        tour.Start();
        await Task.Delay(180); // Allow the overlay bounds poll and initial card layout to settle.
        UiInput.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        UiInput.RunJobs();
        var panel = UiInput.Descendants<ConnectChipletsTourPanel>(window).Single();
        var overlay = UiInput.Descendants<TourAnchorOverlay>(window).Single(o => ReferenceEquals(o.Card, panel));
        Assert.True(overlay.Demonstrate);
        Assert.NotNull(overlay.TargetCanvasPoint);
        Click("ChipletTourNextButton");
        Assert.True(tour.CurrentTargetName == "SystemWorkspaceButton", $"Stage {tour.CurrentStepIndex}; workspace {vm.WorkspaceSwitch.Current}; power {vm.Canvas.ShowPowerFlow}");
        Click("SystemWorkspaceButton");
        Assert.Equal("RunSimulationButton", tour.CurrentTargetName);
        Click("RunSimulationButton");
        await WaitFor(() => tour.CurrentStepIndex == 2);
        Assert.NotNull(overlay.TargetCanvasDestinationPoint);
        Assert.False(panel.FindControl<Button>("ChipletTourNextButton")!.IsVisible);
        await Task.Delay(180);
        UiInput.RunJobs();
        var showTarget = overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GuideShowTarget");
        if (showTarget.IsVisible) { UiInput.Click(window, showTarget); UiInput.RunJobs(); }
        Assert.False(showTarget.IsVisible);
        Assert.True(overlay.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Line>().Single(c => c.Name == "GuideTrail").IsVisible);
        Assert.True(overlay.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "GuidePointer").IsVisible);
        Save(window, language, "move");
        if (language == "en")
        {
            var canvas = UiInput.Descendants<DesignCanvas>(window).First();
            Point Screen(Point world) => canvas.TranslatePoint(new Point(world.X * canvas.Zoom + vm.Canvas.PanX,
                world.Y * canvas.Zoom + vm.Canvas.PanY), window)!.Value;
            var from = Screen(tour.CurrentCanvasPoint!.Value);
            var to = Screen(tour.CurrentCanvasDestinationPoint!.Value);
            window.MouseMove(from);
            window.MouseDown(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
            window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            window.MouseUp(to, MouseButton.Left);
            UiInput.RunJobs();
        }
        else Click("MoveChipletForMeButton");
        await WaitFor(() => tour.CurrentStepIndex == 3);
        await vm.Canvas.RecalculateRoutesAsync();
        Click("DesignChecksRunButton");
        await WaitFor(() => tour.CurrentStepIndex == 4);
        Assert.True(vm.RightPanel.DesignValidation.IsCurrentIssueAlignable);
        Save(window, language, "align");
        Click("AlignChipletButton");
        await WaitFor(() => tour.CurrentStepIndex == 5);
        if (tour.CurrentTargetName == "ChipletsRailButton") Click("ChipletsRailButton");
        Assert.Equal("NewDieButton", tour.CurrentTargetName);
        Save(window, language, "new-die");
        Click("NewDieButton");
        var newDie = UiInput.Descendants<Button>(window).Single(b => b.Name == "NewDieButton");
        var flyout = Assert.IsType<Flyout>(newDie.Flyout);
        Assert.True(flyout.IsOpen);
        Assert.Equal(5, tour.CurrentStepIndex);
        var content = Assert.IsAssignableFrom<Control>(flyout.Content);
        var create = content.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CreateDieButton");
        create.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        UiInput.RunJobs();
        Assert.Equal(6, tour.CurrentStepIndex);
        Assert.False(flyout.IsOpen);
        Click("ChipletTourNextButton");
        Assert.True(tour.IsCompleted);
        Assert.False(tour.IsActive);

        void Click(string name)
        {
            var button = UiInput.Descendants<Button>(window).Single(b => b.Name == name && b.IsEffectivelyVisible);
            Assert.True(button.IsEnabled);
            UiInput.Click(window, button);
            UiInput.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task CancelExampleOpen_PreservesUnsavedDesign()
    {
        using var host = new UiFlowTestHost();
        var part = host.Vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide());
        host.Vm.WorkspaceSwitch.Current = Workspace.Optics;
        var prompt = new Mock<IMessageBoxService>();
        prompt.Setup(p => p.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(SavePromptResult.Cancel);
        host.Vm.FileOperations.MessageBoxService = prompt.Object;
        await host.Vm.Home.ConnectChipletsTourCommand.ExecuteAsync(null);
        prompt.Verify(p => p.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        Assert.Contains(part, host.Vm.Canvas.Components);
        Assert.True(host.Vm.FileOperations.HasUnsavedChanges);
        Assert.Equal(Workspace.Optics, host.Vm.WorkspaceSwitch.Current);
        Assert.False(host.Vm.ConnectChipletsTour.IsActive);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) { await Task.Delay(10); UiInput.RunJobs(); }
        Assert.True(condition());
    }

    private static void Save(Window window, string language, string stage)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "system-learning");
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(directory, $"{language}-{stage}.png"));
    }
}

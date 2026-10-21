using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CAP.Avalonia.Controls;
using CAP.Avalonia.Controls.TourAnchor;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.Views.Panels;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using CAP_Core.Analysis.LogicAnalysis.GateView;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
public class FirstGateVisualJourneyTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task Learner_SelectsGroupsSimulatesAndBuildsGate(string language)
    {
        using var host = new UiFlowTestHost();
        var vm = host.Vm;
        var window = host.Window;
        window.Width = 1600;
        window.Height = 1000;
        LocalizationService.Instance.SetLanguage(language);
        await vm.Home.FirstGateTourCommand.ExecuteAsync(null);
        await vm.FileOperations.PostLoadRouting;
        UiInput.RunJobs();
        var tour = vm.FirstGateTour;
        var canvas = UiInput.Descendants<DesignCanvas>(window).First();
        var overlay = UiInput.Descendants<TourAnchorOverlay>(window).Single(o => o.Card is FirstGateTourPanel);
        Assert.True(overlay.Demonstrate);
        await Task.Delay(180);
        UiInput.RunJobs();
        Assert.Equal("FirstGateStartButton", tour.CurrentTargetName);
        Assert.True(overlay.FindControl<Control>("GuidePointer")!.IsVisible);
        Save("start");
        UiInput.Click(window, UiInput.Descendants<Button>(window).Single(b => b.Name == "FirstGateStartButton"));
        Assert.Equal(1, tour.CurrentStepIndex);

        Point Screen(Point world) => canvas.TranslatePoint(new Point(
            world.X * canvas.Zoom + vm.Canvas.PanX, world.Y * canvas.Zoom + vm.Canvas.PanY), window)!.Value;

        async Task Settle()
        {
            await Task.Delay(180);
            window.UpdateLayout();
            UiInput.RunJobs();
        }

        async Task ClickTarget()
        {
            await Settle();
            Assert.False(string.IsNullOrEmpty(tour.CurrentTargetName), $"No target at stage {tour.CurrentStepIndex}; reveal={tour.ShowRevealGate}");
            var target = UiInput.Descendants<Button>(window).FirstOrDefault(b => b.Name == tour.CurrentTargetName && b.IsEffectivelyVisible);
            Assert.True(target != null, $"Missing {tour.CurrentTargetName} at stage {tour.CurrentStepIndex}");
            var clicked = false;
            target!.Click += (_, _) => clicked = true;
            UiInput.Click(window, target);
            Assert.True(clicked, $"Actual click did not reach {target.Name}");
            await Settle();
        }

        if (tour.CurrentTargetName != "DesignCanvasControl") await ClickTarget();
        await Settle();
        var reveal = overlay.FindControl<Button>("GuideShowTarget")!;
        if (reveal.IsVisible) { UiInput.Click(window, reveal); await Settle(); }
        Assert.NotNull(tour.CurrentCanvasPoint);
        Assert.NotNull(tour.CurrentCanvasDestinationPoint);
        Assert.True(overlay.FindControl<Control>("GuidePointer")!.IsVisible, "The actual marquee start must be demonstrated.");
        Assert.True(overlay.FindControl<Control>("GuideTrail")!.IsVisible, "The actual marquee drag must be demonstrated.");
        var visibleCanvas = new Rect(canvas.TranslatePoint(default, window)!.Value, canvas.Bounds.Size);
        Assert.True(visibleCanvas.Contains(Screen(tour.CurrentCanvasPoint!.Value)));
        Assert.True(visibleCanvas.Contains(Screen(tour.CurrentCanvasDestinationPoint!.Value)));
        Save("select-parts");
        UiInput.DragMouse(window, Screen(tour.CurrentCanvasPoint!.Value), Screen(tour.CurrentCanvasDestinationPoint!.Value));
        Assert.Equal("CreateGroupButton", tour.CurrentTargetName);
        await ClickTarget();
        Assert.Equal(2, tour.CurrentStepIndex);
        if (tour.ShowRevealGate)
        {
            await Settle();
            UiInput.Click(window, UiInput.Descendants<Button>(window).Single(b => b.Name == "FirstGateRevealButton"));
            await Settle();
        }
        else
        {
            if (tour.CurrentTargetName == "LogicWorkspaceButton") await ClickTarget();
            await ClickTarget();
        }
        Assert.True(tour.CurrentStepIndex == 3, $"Step={tour.CurrentStepIndex}, target={tour.CurrentTargetName}, active={vm.RightPanel.MakeGate.IsActive}, canMake={vm.RightPanel.MakeGate.CanMakeGate}, selection={vm.Canvas.Selection.SelectedComponents.Count}, reveal={tour.ShowRevealGate}");
        // Closing the editor and then losing selection must each have a real recovery action.
        UiInput.Click(window, UiInput.Descendants<Button>(window).Single(b =>
            ReferenceEquals(b.Command, vm.RightPanel.MakeGate.CloseCommand) && b.IsEffectivelyVisible));
        await Settle();
        Assert.Equal(2, tour.CurrentStepIndex);
        await ClickTarget();
        Assert.True(tour.CurrentStepIndex == 3, $"Reopen step={tour.CurrentStepIndex}, target={tour.CurrentTargetName}, active={vm.RightPanel.MakeGate.IsActive}, canMake={vm.RightPanel.MakeGate.CanMakeGate}, selection={vm.Canvas.Selection.SelectedComponents.Count}, reveal={tour.ShowRevealGate}");
        UiInput.ClickAt(window, canvas.TranslatePoint(new Point(canvas.Bounds.Width - 12, 12), window)!.Value);
        await Settle();
        Assert.Empty(vm.Canvas.Selection.SelectedComponents);
        Assert.Equal(2, tour.CurrentStepIndex);
        Assert.True(tour.ShowRevealGate);
        Save("recover-selection");
        UiInput.Click(window, UiInput.Descendants<Button>(window).Single(b => b.Name == "FirstGateRevealButton"));
        await Settle();
        Assert.Equal(3, tour.CurrentStepIndex);
        await ClickTarget();
        if (vm.RightPanel.MakeGate.SimulateCommand.ExecutionTask is { } simulation) await simulation;
        await Settle();
        Assert.Equal(4, tour.CurrentStepIndex);
        var functions = UiInput.Descendants<ComboBox>(window).Single(c => c.Name == "MakeGateFunctionPicker");
        UiInput.Click(window, functions);
        UiInput.PressKey(window, Key.Home);
        var nandIndex = vm.RightPanel.MakeGate.Functions.ToList().FindIndex(f => f.Function == GateFunction.Nand);
        for (var i = 0; i < nandIndex; i++) UiInput.PressKey(window, Key.Down);
        UiInput.PressKey(window, Key.Enter);
        await Settle();
        Assert.Equal(5, tour.CurrentStepIndex);
        Save("save-nand");
        await ClickTarget();
        Assert.Equal(6, tour.CurrentStepIndex);
        await vm.GateView.PendingRebuild;
        Assert.Equal("NAND", Assert.Single(vm.GateView.Graph.Nodes).DisplayName);
        UiInput.PressKey(window, Key.Z, RawInputModifiers.Control);
        await Settle();
        Assert.Equal(5, tour.CurrentStepIndex);
        await vm.GateView.PendingRebuild;
        Assert.Empty(vm.GateView.Graph.Nodes);
        await ClickTarget();
        Assert.Equal(6, tour.CurrentStepIndex);
        await vm.GateView.PendingRebuild;
        Assert.Equal("NAND", Assert.Single(vm.GateView.Graph.Nodes).DisplayName);
        if (tour.CurrentTargetName == "MakeGateOpenLogicButton") await ClickTarget();
        await ClickTarget();
        if (vm.RightPanel.Logic.BuildNetworkCommand.ExecutionTask is { } build) await build;
        Assert.True(tour.IsCompleted, vm.RightPanel.Logic.StatusText);
        Assert.True(vm.RightPanel.Logic.HasNetwork);
        await vm.GateView.PendingRebuild;
        var drawnGate = Assert.Single(vm.GateView.Graph.Nodes);
        Assert.Equal("NAND", drawnGate.DisplayName);
        Assert.Equal(GateSymbolKind.Nand, drawnGate.Kind);
        Assert.False(vm.GateView.IsStale);
        await Settle();
        Save("built");

        void Save(string step)
        {
            var folder = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "first-gate-journey");
            Directory.CreateDirectory(folder);
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(folder, language + "-" + step + ".png"));
        }
    }
}

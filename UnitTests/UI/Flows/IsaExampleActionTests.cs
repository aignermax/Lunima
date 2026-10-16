using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.Views;
using Moq;
using Shouldly;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
[Trait("Category", "Slow")]
public class IsaExampleActionTests
{
    [AvaloniaFact]
    public async Task RealOpenPipeline_DiscardLoadsCopy_BuildIsExplicit()
    {
        using var host = new UiFlowTestHost();
        var part = host.Vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide());
        host.Vm.FileOperations.HasUnsavedChanges = true;
        var prompt = new Mock<IMessageBoxService>();
        prompt.Setup(p => p.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(SavePromptResult.DontSave);
        host.Vm.FileOperations.MessageBoxService = prompt.Object;
        var adder = new ExampleDesignsService().GetExamples().Single(e => Path.GetFileName(e.FilePath) == "Logic Gate 4-Bit Adder.lun");
        var vm = new IsaPlaygroundViewModel { FindExamples = () => new[] { adder } };
        IsaPlaygroundNavigation.Attach(vm, host.Vm);
        await vm.OpenSuitableExampleCommand.ExecuteAsync(null);
        prompt.Verify(p => p.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        host.Vm.Canvas.Components.ShouldNotContain(part);
        host.Vm.FileOperations.CurrentFilePath.ShouldBeNull();
        host.Vm.FileOperations.HasUnsavedChanges.ShouldBeTrue();
        host.Vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Logic);
        vm.HasOpenedSuitableExample.ShouldBeTrue();
        host.Vm.RightPanel.Logic.HasNetwork.ShouldBeFalse();
        await vm.BuildExampleNetworkCommand.ExecuteAsync(null);
        host.Vm.RightPanel.Logic.HasNetwork.ShouldBeTrue();
        vm.HasOpenedSuitableExample.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task RealOpenPipeline_UnsavedCancelPreservesDesignAndWorkspace()
    {
        using var host = new UiFlowTestHost();
        var part = host.Vm.Canvas.AddComponent(TestComponentFactory.CreateStraightWaveGuide());
        host.Vm.WorkspaceSwitch.Current = Workspace.Optics;
        host.Vm.FileOperations.HasUnsavedChanges = true;
        var prompt = new Mock<IMessageBoxService>();
        prompt.Setup(p => p.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(SavePromptResult.Cancel);
        host.Vm.FileOperations.MessageBoxService = prompt.Object;
        var vm = new IsaPlaygroundViewModel();
        IsaPlaygroundNavigation.Attach(vm, host.Vm);
        await vm.OpenSuitableExampleCommand.ExecuteAsync(null);
        prompt.Verify(p => p.ShowSavePromptAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        host.Vm.Canvas.Components.ShouldContain(part);
        host.Vm.WorkspaceSwitch.Current.ShouldBe(Workspace.Optics);
        vm.HasOpenedSuitableExample.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task RealButtons_OfferBuildOnlyAfterOpen(string language)
    {
        using var host = new UiFlowTestHost();
        LocalizationService.Instance.SetLanguage(language);
        var vm = new IsaPlaygroundViewModel();
        int opens = 0, builds = 0;
        vm.OpenExampleRequested = _ => { opens++; return Task.FromResult(true); };
        vm.BuildLogicNetworkRequested = () => { builds++; return Task.FromResult(false); };
        var window = new IsaPlaygroundWindow { DataContext = vm };
        window.Show();
        try
        {
            UiInput.RunJobs();
            var open = window.FindControl<Button>("OpenSuitableIsaExampleButton")!;
            open.IsEffectivelyVisible.ShouldBeTrue();
            Capture(window, language + "-open");
            Activate(open);
            await vm.OpenSuitableExampleCommand.ExecutionTask!;
            UiInput.RunJobs();
            opens.ShouldBe(1);
            builds.ShouldBe(0);
            var build = window.FindControl<Button>("BuildIsaExampleNetworkButton")!;
            build.IsEffectivelyVisible.ShouldBeTrue();
            open.IsEffectivelyVisible.ShouldBeFalse();
            Capture(window, language + "-build");
            Activate(build);
            await vm.BuildExampleNetworkCommand.ExecutionTask!;
            builds.ShouldBe(1);
        }
        finally { window.Close(); }
    }

    private static void Activate(Button button) => button.RaiseEvent(new KeyEventArgs
        { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

    private static void Capture(IsaPlaygroundWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable("UI_SHOT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        ScreenshotArtifacts.SettleAnimations();
        using var frame = window.CaptureRenderedFrame();
        ScreenshotArtifacts.SavePng(frame!, Path.Combine(directory, "isa-example-" + name + ".png"));
    }

    [AvaloniaFact]
    public void UnbuiltNetwork_OffersExampleActionBesideHint()
    {
        using var host = new UiFlowTestHost();
        var window = new IsaPlaygroundWindow { DataContext = new IsaPlaygroundViewModel() };
        window.Show();
        try
        {
            UiInput.RunJobs();
            var button = window.FindControl<Button>("OpenSuitableIsaExampleButton");
            button.ShouldNotBeNull();
            button.IsEffectivelyVisible.ShouldBeTrue();
        }
        finally { window.Close(); }
    }
}

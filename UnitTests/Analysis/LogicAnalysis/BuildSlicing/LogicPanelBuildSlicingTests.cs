using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Regression;
using Xunit;
using static UnitTests.Analysis.LogicAnalysis.BuildSlicing.CaptureSlicingFixture;

namespace UnitTests.Analysis.LogicAnalysis.BuildSlicing;

/// <summary>
/// The Logic panel's build hands the UI thread back between capture slices. Whatever the user does
/// in such a frame — edit the design, reconfigure the panel, press Cancel, close the shell — the
/// build ends without an exception and publishes nothing (the existing
/// <see cref="CAP.Avalonia.ViewModels.Analysis.AnalysisSweepOperation"/> semantics); an
/// uninterrupted build still publishes once its last slice resumes.
/// </summary>
[Collection("LocalizationSingleton")]
public class LogicPanelBuildSlicingTests
{
    [AvaloniaFact]
    public async Task Build_RunsQueuedWorkBetweenSlices_ThenPublishes()
    {
        await ContinueInsideDispatcherJobAsync();
        await using var panel = new SlicedPanel();
        var slicesBeforeQueuedWork = -1;
        Dispatcher.UIThread.Post(() => slicesBeforeQueuedWork = panel.Assembler.CompletedSlices);

        await panel.BuildAsync();

        slicesBeforeQueuedWork.ShouldBe(0, "work queued before the build runs at its first slice boundary");
        panel.Assembler.CompletedSlices.ShouldBe(SlicingAssembler.SliceCount);
        panel.Model.HasNetwork.ShouldBeTrue(panel.Model.StatusText);
        panel.Provider.Network.ShouldNotBeNull();
        panel.Model.IsProcessing.ShouldBeFalse();
    }

    [AvaloniaTheory]
    [InlineData("edit")]
    [InlineData("component")]
    [InlineData("replace")]
    [InlineData("configure")]
    public async Task DesignChangeBetweenSlices_EndsBuildWithoutPublishing(string change)
    {
        await ContinueInsideDispatcherJobAsync();
        await using var panel = new SlicedPanel();
        Dispatcher.UIThread.Post(() => Apply(panel, change));

        await Should.NotThrowAsync(panel.BuildAsync);

        panel.Assembler.CompletedSlices.ShouldBe(0, "the capture must not resume over a changed design");
        panel.Model.HasNetwork.ShouldBeFalse();
        panel.Provider.Network.ShouldBeNull();
        panel.Model.IsProcessing.ShouldBeFalse();
        panel.Model.StatusText.ShouldBe(LocalizationService.Instance.Translate("Analysis.LogicPanel.Running"));
    }

    [AvaloniaFact]
    public async Task CancelBetweenSlices_ReportsCancelledAndPublishesNothing()
    {
        await ContinueInsideDispatcherJobAsync();
        await using var panel = new SlicedPanel();
        Dispatcher.UIThread.Post(() => panel.Model.CancelCommand.Execute(null));

        await Should.NotThrowAsync(panel.BuildAsync);

        panel.Model.HasNetwork.ShouldBeFalse();
        panel.Provider.Network.ShouldBeNull();
        panel.Model.StatusText.ShouldBe(LocalizationService.Instance.Translate("Analysis.LogicPanel.Cancelled"));
    }

    [AvaloniaFact]
    public async Task ShellStopBetweenSlices_DrainsTheBuildWithoutPublishing()
    {
        await ContinueInsideDispatcherJobAsync();
        await using var panel = new SlicedPanel();
        Task? stopping = null;
        Dispatcher.UIThread.Post(() => stopping = panel.Model.StopAsync());

        await Should.NotThrowAsync(panel.BuildAsync);
        await stopping.ShouldNotBeNull().WaitAsync(TimeSpan.FromSeconds(5));

        panel.Assembler.CompletedSlices.ShouldBe(0);
        panel.Model.HasNetwork.ShouldBeFalse();
        panel.Provider.Network.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task RunInLogic_CanvasEditDuringTheRealCapture_PublishesNothingAndTheNextRunBuilds()
    {
        await ContinueInsideDispatcherJobAsync();
        LocalizationService.Instance.SetLanguage(SupportedLanguage.English.Code);
        var provider = new BuiltLogicNetworkProvider();
        await using var vm = MainViewModelTestHelper.CreateMainViewModel(logicNetworkProvider: provider);
        var path = Path.Combine(GoldenManifest.ExamplesDirectory(GoldenManifest.LocateRepositoryRoot()),
            "Logic Gate 4-Bit Adder.lun");
        (await vm.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue();
        await vm.FileOperations.PostLoadRouting;
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        Dispatcher.UIThread.Post(() => vm.Canvas.Components.Add(
            new ComponentViewModel(TestComponentFactory.CreateStraightWaveGuide())));

        await Should.NotThrowAsync(() => vm.RunWorkspaceCommand.ExecuteAsync(null));

        vm.RightPanel.Logic.HasNetwork.ShouldBeFalse("a build that saw the canvas change must not publish");
        provider.Network.ShouldBeNull();
        await vm.RunWorkspaceCommand.ExecuteAsync(null);
        vm.RightPanel.Logic.HasNetwork.ShouldBeTrue(vm.RightPanel.Logic.StatusText);
        provider.Network.ShouldNotBeNull().Gates.Count.ShouldBe(344);
    }

    private static void Apply(SlicedPanel panel, string change)
    {
        switch (change)
        {
            case "edit": panel.Canvas.Document.Invalidate(); break;
            case "component": panel.Canvas.Components.Add(new ComponentViewModel(TestComponentFactory.CreateStraightWaveGuide())); break;
            case "replace": panel.Canvas.Document.ReplaceDocument(); break;
            default: panel.Model.Configure(panel.Canvas); break;
        }
    }
}

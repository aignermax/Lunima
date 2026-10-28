using CAP.Avalonia.ViewModels.Analysis;
using CAP.Avalonia.ViewModels.Analysis.AnalysisOutput;
using CAP.Avalonia.ViewModels.Analysis.CircuitOptimization;
using CAP.Avalonia.ViewModels.Analysis.EyeDiagram;
using CAP.Avalonia.ViewModels.Analysis.MonteCarloAnalysis;
using CAP.Avalonia.ViewModels.Analysis.WavelengthSpectrum;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>Workspace → dock tabs / Run label mapping, the "show all tabs" override and persistence.</summary>
public class WorkspaceViewModelTests
{
    private static AnalysisDockViewModel MakeDock() =>
        new(new TimeDomainViewModel(), new EyeDiagramViewModel(),
            new WavelengthSpectrumViewModel(new CAP_Core.Analysis.MeasuredSpectrum.MeasuredSpectrumFileSource()), new AnalysisOutputPanelViewModel(),
            new MonteCarloViewModel(), new CircuitOptimizationViewModel(new CAP.Avalonia.Commands.CommandManager()));

    private static IEnumerable<AnalysisDockTab> ShownTabs(AnalysisDockViewModel dock) =>
        Enum.GetValues<AnalysisDockTab>().Where(dock.Tabs.IsShown);

    [Fact]
    public void StartsInLayout_ShowingChecks()
    {
        var dock = MakeDock();
        var vm = new WorkspaceViewModel(dock);

        vm.Current.ShouldBe(Workspace.Layout);
        ShownTabs(dock).ShouldBe([AnalysisDockTab.Checks]);
        dock.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Checks);
    }

    [Theory]
    [InlineData(Workspace.Layout, new[] { AnalysisDockTab.Checks })]
    [InlineData(Workspace.Optics, new[]
    {
        AnalysisDockTab.Transient, AnalysisDockTab.Sweep, AnalysisDockTab.EyeBer,
        AnalysisDockTab.Spectrum, AnalysisDockTab.MonteCarlo, AnalysisDockTab.Optimization,
    })]
    [InlineData(Workspace.Logic, new[] { AnalysisDockTab.Logic })]
    [InlineData(Workspace.System, new[]
    {
        AnalysisDockTab.Checks, AnalysisDockTab.Logic, AnalysisDockTab.Spectrum,
    })]
    public void Select_ShowsOnlyTheWorkspaceTabs(Workspace workspace, AnalysisDockTab[] expected)
    {
        var dock = MakeDock();
        var vm = new WorkspaceViewModel(dock);

        vm.SelectCommand.Execute(workspace);

        ShownTabs(dock).ShouldBe(expected, ignoreOrder: true);
        dock.SelectedTabIndex.ShouldBe((int)expected[0], "the dock opens on the workspace's first tab");
    }

    [Theory]
    [InlineData(Workspace.Layout, "Workspace.RunLayout")]
    [InlineData(Workspace.Optics, "Workspace.RunOptics")]
    [InlineData(Workspace.Logic, "Workspace.RunLogic")]
    [InlineData(Workspace.System, "Workspace.RunSystem")]
    public void RunLabel_NamesTheWorkspaceAction(Workspace workspace, string labelKey)
    {
        var vm = new WorkspaceViewModel(MakeDock());

        vm.Current = workspace;

        vm.Profile.RunLabelKey.ShouldBe(labelKey);
        vm.RunLabel.ShouldNotBeNullOrWhiteSpace();
        vm.RunLabel.ShouldNotBe(labelKey, "the label must resolve from the string table");
    }

    [Fact]
    public void CwTransientChoice_AndOverlayToggle_FollowTheWorkspace()
    {
        var vm = new WorkspaceViewModel(MakeDock());

        vm.IsOptics.ShouldBeFalse();
        vm.IsOpticalWorkspace.ShouldBeFalse();
        vm.Current = Workspace.Optics;
        vm.IsOptics.ShouldBeTrue();
        vm.IsOpticalWorkspace.ShouldBeTrue();
        vm.Current = Workspace.System;
        vm.IsOptics.ShouldBeFalse("the CW/Transient sub-choice belongs to Optics only");
        vm.IsOpticalWorkspace.ShouldBeTrue("System runs the CW simulation too");
    }

    [Theory]
    [InlineData(Workspace.Optics, true)]
    [InlineData(Workspace.System, true)]
    [InlineData(Workspace.Layout, false)]
    [InlineData(Workspace.Logic, false)]
    public void RunIsAToggle_OnlyWhereItSimulatesLight(Workspace workspace, bool toggles)
    {
        var vm = new WorkspaceViewModel(MakeDock()) { Current = workspace };
        string offLabel = vm.RunLabel;

        vm.IsSimulationOn = true;

        vm.IsRunActive.ShouldBe(toggles);
        if (toggles)
        {
            vm.RunLabel.ShouldNotBe(offLabel, "the pressed Run names its state, e.g. \"Simulation on\"");
            vm.RunTip.ShouldContain("(L, P)");
        }
        else
        {
            vm.RunLabel.ShouldBe(offLabel, "Run checks / Run logic never become a toggle");
        }
    }

    [Fact]
    public void RunActive_RaisesLabelChanges_WhenTheSimulationTurnsOff()
    {
        var vm = new WorkspaceViewModel(MakeDock()) { Current = Workspace.Optics, IsSimulationOn = true };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsSimulationOn = false;

        vm.IsRunActive.ShouldBeFalse();
        changed.ShouldContain(nameof(WorkspaceViewModel.IsRunActive));
        changed.ShouldContain(nameof(WorkspaceViewModel.RunLabel));
        changed.ShouldContain(nameof(WorkspaceViewModel.RunTip));
    }

    [Fact]
    public void ShowAllTabs_ShowsEveryTab_AndKeepsSelection()
    {
        var dock = MakeDock();
        var vm = new WorkspaceViewModel(dock) { Current = Workspace.Logic };

        dock.ShowAllTabs = true;
        ShownTabs(dock).Count().ShouldBe(Enum.GetValues<AnalysisDockTab>().Length);

        vm.Current = Workspace.Layout;
        dock.SelectedTabIndex.ShouldBe((int)AnalysisDockTab.Logic,
            "with all tabs shown, switching workspace must not yank the user off their tab");
    }

    [Fact]
    public void SelectedTab_StaysVisible_WhenOpenedOutsideTheWorkspace()
    {
        var dock = MakeDock();
        _ = new WorkspaceViewModel(dock) { Current = Workspace.Layout };

        dock.OpenLogic();

        dock.Tabs.Logic.ShouldBeTrue("a programmatic jump (tour, menu) must never land on a hidden tab");
        dock.Tabs.Spectrum.ShouldBeFalse();
    }

    [Fact]
    public void CaptureAndRestore_RoundTripsTheStoredWorkspace()
    {
        var saved = new WorkspaceViewModel(MakeDock()) { Current = Workspace.System };
        var stored = saved.CaptureForSave();

        var loaded = new WorkspaceViewModel(MakeDock());
        loaded.RestoreAfterLoad(stored, [], []);

        loaded.Current.ShouldBe(Workspace.System);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NotAWorkspace")]
    [InlineData("42")]
    public void Restore_WithMissingOrInvalidValue_FallsBackToContentDefault(string? stored)
    {
        var vm = new WorkspaceViewModel(MakeDock()) { Current = Workspace.Logic };
        var source = UnitTests.TestComponentFactory.CreateBasicComponent();
        source.IsUserMarkedLightSource = true;

        vm.RestoreAfterLoad(stored, [source], Array.Empty<WaveguideConnection>());

        vm.Current.ShouldBe(Workspace.Optics);
    }

    [Fact]
    public void ResetForNewDesign_GoesToLayout()
    {
        var vm = new WorkspaceViewModel(MakeDock()) { Current = Workspace.Optics };

        vm.ResetForNewDesign();

        vm.Current.ShouldBe(Workspace.Layout);
    }
}

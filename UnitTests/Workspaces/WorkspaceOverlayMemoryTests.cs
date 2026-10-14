using CAP.Avalonia.ViewModels.Canvas.ViewSettings;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP.Avalonia.ViewModels.Workspaces.Presentation;
using Shouldly;
using Xunit;

namespace UnitTests.Workspaces;

/// <summary>
/// The data side of the workspace presentation: each profile's overlay defaults, emphasis and
/// tool placement, and how the overlay memory resolves defaults against the user's own toggles.
/// </summary>
public class WorkspaceOverlayMemoryTests
{
    private static readonly WorkspaceOverlayState AllOff = new(PowerFlow: false, DesignCheckMarkers: false);
    private static readonly WorkspaceOverlayState AllOn = new(PowerFlow: true, DesignCheckMarkers: true);

    [Fact]
    public void Layout_EmphasisesGeometry_ChecksOn_LightOff_EveryToolPrimary()
    {
        var layout = WorkspaceProfile.For(Workspace.Layout).Presentation;

        layout.Emphasis.ShouldBe(CanvasEmphasis.Geometry);
        layout.PowerFlow.ShouldBe(OverlayDefault.Off);
        layout.DesignCheckMarkers.ShouldBe(OverlayDefault.On);
        layout.OverflowTools.ShouldBeEmpty();
    }

    [Fact]
    public void Optics_EmphasisesLight_LightWhenSimulated_CutInOverflow()
    {
        var optics = WorkspaceProfile.For(Workspace.Optics).Presentation;

        optics.Emphasis.ShouldBe(CanvasEmphasis.Light);
        optics.PowerFlow.ShouldBe(OverlayDefault.OnWhenAvailable);
        optics.DesignCheckMarkers.ShouldBe(OverlayDefault.Off);
        optics.IsPrimary(WorkspaceTool.Probe).ShouldBeTrue("the probe is the Optics instrument");
        optics.IsPrimary(WorkspaceTool.Cut).ShouldBeFalse();
    }

    [Theory]
    [InlineData(Workspace.Logic)]
    [InlineData(Workspace.System)]
    public void LogicAndSystem_KeepTheOverlays(Workspace workspace)
    {
        var presentation = WorkspaceProfile.For(workspace).Presentation;
        var memory = new WorkspaceOverlayMemory();
        memory.Remember(workspace, AllOff);

        memory.Resolve(workspace, presentation, AllOn, hasSimulationResult: true).ShouldBe(AllOn);
    }

    [Fact]
    public void FirstVisit_UsesTheDefaults()
    {
        var memory = new WorkspaceOverlayMemory();

        memory.Resolve(Workspace.Layout, WorkspacePresentation.Layout, AllOn, hasSimulationResult: true)
            .ShouldBe(new WorkspaceOverlayState(PowerFlow: false, DesignCheckMarkers: true));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Optics_FirstVisit_ShowsLightOnlyWhenAResultExists(bool hasResult, bool expectedPowerFlow)
    {
        var memory = new WorkspaceOverlayMemory();

        var state = memory.Resolve(Workspace.Optics, WorkspacePresentation.Optics, AllOff, hasResult);

        state.PowerFlow.ShouldBe(expectedPowerFlow);
        state.DesignCheckMarkers.ShouldBeFalse();
    }

    [Fact]
    public void ReturningVisit_RestoresWhatTheUserLeft_OverTheDefaults()
    {
        var memory = new WorkspaceOverlayMemory();
        var userChoice = new WorkspaceOverlayState(PowerFlow: true, DesignCheckMarkers: false);
        memory.Remember(Workspace.Layout, userChoice);

        memory.HasRemembered(Workspace.Layout).ShouldBeTrue();
        memory.HasRemembered(Workspace.Optics).ShouldBeFalse();
        memory.Resolve(Workspace.Layout, WorkspacePresentation.Layout, AllOff, hasSimulationResult: false)
            .ShouldBe(userChoice);
    }
}

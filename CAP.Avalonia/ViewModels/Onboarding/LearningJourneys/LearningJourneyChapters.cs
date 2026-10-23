using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Onboarding.FirstStepsTutorial;
using CAP.Avalonia.ViewModels.Panels;
using CAP.Avalonia.ViewModels.Shell;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;

namespace CAP.Avalonia.ViewModels.Onboarding.LearningJourneys;

/// <summary>Owns the chapter engines and their attachment to the editor's actual panels.</summary>
public sealed class LearningJourneyChapters(
    TutorialViewModel tutorial,
    WatchComputeTourViewModel watch,
    FirstGateTourViewModel firstGate,
    RunProgramTourViewModel program,
    ConnectChipletsTourViewModel chiplets,
    StoreNumberTourViewModel memory)
{
    private Action<ComponentGroup>? _revealGate;
    public TutorialViewModel Tutorial { get; } = tutorial;
    public WatchComputeTourViewModel Watch { get; } = watch;
    public FirstGateTourViewModel FirstGate { get; } = firstGate;
    public RunProgramTourViewModel Program { get; } = program;
    public ConnectChipletsTourViewModel Chiplets { get; } = chiplets;
    public StoreNumberTourViewModel Memory { get; } = memory;

    public static LearningJourneyChapters Create(DesignCanvasViewModel canvas, RightPanelViewModel right,
        BottomPanelViewModel bottom, CommandManager commands, IsaPlaygroundViewModel? isa = null) => new(
            new TutorialViewModel(canvas), new WatchComputeTourViewModel(right.Logic, bottom.Analysis),
            new FirstGateTourViewModel(canvas, right.MakeGate, right.Logic, commands),
            new RunProgramTourViewModel(right.Logic, bottom.Analysis, isa ?? new IsaPlaygroundViewModel()),
            new ConnectChipletsTourViewModel(canvas, right.DesignValidation, bottom.Analysis, commands),
            new StoreNumberTourViewModel(right.Logic, bottom.Analysis));

    public void Attach(CanvasInteractionViewModel interaction, LeftPanelViewModel library,
        WorkspaceViewModel workspace, LeftRailViewModel rail, BottomPanelViewModel bottom,
        Action<ComponentGroup> revealGate)
    {
        Tutorial.AttachInteraction(interaction, library.FilteredTemplates);
        Tutorial.AttachWorkspace(workspace);
        Watch.AttachWorkspace(workspace);
        Chiplets.AttachVisualContext(workspace, rail);
        Memory.AttachWorkspace(workspace);
        FirstGate.AttachUi(workspace, bottom.Analysis, interaction);
        _revealGate = revealGate;
        FirstGate.RevealGateRequested = revealGate;
    }

    public void Stop()
    {
        Tutorial.Skip();
        FirstGate.Skip();
        Watch.Skip();
        Program.Skip();
        Chiplets.Skip();
        Memory.Skip();
    }

    public void Detach()
    {
        Stop();
        if (FirstGate.RevealGateRequested == _revealGate) FirstGate.RevealGateRequested = null;
        _revealGate = null;
    }
}

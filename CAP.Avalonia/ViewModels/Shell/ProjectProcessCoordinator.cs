using CAP.Avalonia.ViewModels.Onboarding.LearningJourneys;
using CAP.Avalonia.ViewModels.Panels;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.ViewModels.Shell;

/// <summary>Sequences new-project confirmation, process choice and optional learning guidance.</summary>
internal sealed class ProjectProcessCoordinator(FileOperationsViewModel files,
    ProcessRoutingContext process, LearningGuidanceController guidance)
{
    public Func<IReadOnlyList<ProcessGroup>, Task<ActiveProcessSelection?>>? ShowPicker { get; set; }

    public async Task NewProjectAsync()
    {
        guidance.ChoosingNewProjectProcess = true;
        try
        {
            // Cancellation must be explicit: an already-empty canvas is not proof that New succeeded.
            if (!await files.TryNewProjectAsync()) return;
            await PickAsync();
        }
        finally { guidance.ChoosingNewProjectProcess = false; }
        guidance.AfterNewProject();
    }

    public Task PromptInitiallyAsync() =>
        guidance.ChoosingNewProjectProcess || guidance.IsPreparing || files.ActiveProcess != null
            ? Task.CompletedTask : PickAsync();

    private async Task PickAsync()
    {
        if (ShowPicker == null) return;
        var selected = await ShowPicker(process.BuildCatalog());
        // Choosing the baseline of a fresh design must not create a spurious save prompt.
        files.SetActiveProcess(selected ?? ActiveProcessSelection.Playground(), markDirty: false);
    }
}

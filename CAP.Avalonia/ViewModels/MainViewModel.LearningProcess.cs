using CAP.Avalonia.Services.Localization;
using CAP_Core.Components.Process;

namespace CAP.Avalonia.ViewModels;

public partial class MainViewModel
{
    private const string LearningPdkName = "Demo PDK";
    private bool _preparingLearningProject;
    private bool _choosingNewProjectProcess;

    private bool HasLearningProcess => FileOperations.ActiveProcess is { IsPlayground: false } process
        && process.MemberPdkNames.Contains(LearningPdkName)
        && Canvas.Surface.Dies.All(die => die.ProcessBinding?.MemberPdkNames.Contains(LearningPdkName) == true);

    private bool IsFreshLearningCanvas => Canvas.Components.Count == 0 && Canvas.Surface.Dies.Count == 0
        && FileOperations.CurrentFilePath == null && !FileOperations.HasUnsavedChanges;

    private bool PrepareFreshLearningProcess()
    {
        if (HasLearningProcess) return true;
        if (!IsFreshLearningCanvas) return false;
        var group = BuildProcessCatalog().FirstOrDefault(g => g.MemberPdkNames.Contains(LearningPdkName));
        if (group == null) return false;
        FileOperations.SetActiveProcess(ActiveProcessSelection.ForGroup(group), markDirty: false);
        return true;
    }

    /// <summary>Starts the optical chapter in its supported process without silently replacing existing work.</summary>
    private async Task StartVisualTutorialAsync()
    {
        if (_preparingLearningProject) return;
        var navigation = _learningNavigationVersion;
        _preparingLearningProject = true;
        try
        {
            if (!PrepareFreshLearningProcess())
            {
                var dialogs = FileOperations.MessageBoxService;
                if (dialogs == null) return;
                var loc = LocalizationService.Instance;
                var choice = await dialogs.ShowChoicePromptAsync(loc.Translate("Tutorial.Process.NewProjectBody"),
                    loc.Translate("Tutorial.Process.Title"),
                    [loc.Translate("Tutorial.Process.Create"), loc.Translate("Tutorial.Process.Keep")]);
                if (choice != 0 || navigation != _learningNavigationVersion) return;
                if (!await FileOperations.TryNewProjectAsync(() => navigation == _learningNavigationVersion)) return;
                if (!PrepareFreshLearningProcess()) return;
            }
            if (navigation != _learningNavigationVersion) return;
            _guidancePreferences?.SetGuidedLearningEnabled(true);
            StartVisualLearning();
        }
        finally { _preparingLearningProject = false; }
    }
}

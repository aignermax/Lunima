using CAP.Avalonia.ViewModels.Panels.PdkTrash;
using CAP_DataAccess.Components.AddCustomComponent;

namespace CAP.Avalonia.ViewModels.Panels;

public partial class LeftPanelViewModel
{
    private PdkTrashViewModel? _pdkTrash;

    public PdkTrashViewModel PdkTrash => _pdkTrash ??= CreatePdkTrash();

    private PdkTrashViewModel CreatePdkTrash()
    {
        var library = _userPdkLibrary ?? throw new InvalidOperationException(
            "A user-PDK library is required to open the PDK trash.");
        var trashService = library.CreateTrashService();
        var vm = new PdkTrashViewModel(trashService, _errorConsole)
        {
            OnRestored = ApplyRestore,
        };
        return vm;
    }

    private void ApplyRestore(PdkTrashRestoreResult result)
    {
        if (result.Kind == PdkTrashKind.DeletedPdk)
        {
            RegisterCreatedPdk(result.RestoredPdkPath);
            return;
        }

        // The restored file in user-pdks is the truth about a fork's existence, so a restore
        // under a bundled PDK's name may shadow the built-in entry again.
        foreach (var component in result.RestoredComponents)
            RegisterSavedCustomComponent(component, result.PdkName, result.RestoredPdkPath, savedViaBundledFork: true);
    }
}

using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP.Avalonia.ViewModels.Workspaces;

namespace CAP.Avalonia.Services;

/// <summary>Connects the ISA tool's explicit next steps to normal design opening and Logic analysis.</summary>
public static class IsaPlaygroundNavigation
{
    /// <summary>Attaches the owning editor; canceling its save prompt never navigates or builds.</summary>
    public static void Attach(IsaPlaygroundViewModel playground, MainViewModel main)
    {
        playground.OpenExampleRequested = async path =>
        {
            if (!await main.FileOperations.OpenDesignAsCopyAsync(path)) return false;
            main.WorkspaceSwitch.Current = Workspace.Logic;
            main.BottomPanel.Analysis.OpenLogic();
            return true;
        };
        playground.BuildLogicNetworkRequested = async () =>
        {
            main.WorkspaceSwitch.Current = Workspace.Logic;
            main.BottomPanel.Analysis.OpenLogic();
            await main.RightPanel.Logic.BuildNetworkCommand.ExecuteAsync(null);
            return main.RightPanel.Logic.HasNetwork;
        };
    }
}

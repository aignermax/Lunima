using Avalonia.Headless.XUnit;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Analysis.LogicAnalysis.GateBuilding;
using UnitTests.Analysis.LogicAnalysis.MakeGate;
using UnitTests.Helpers;

namespace UnitTests.Logic.GateView;

[Collection("LocalizationSingleton")]
public class GateDefinitionGraphRefreshTests
{
    [AvaloniaFact]
    public async Task SavingGateDefinition_RefreshesGraph_AfterSaveUndoRedoAndRename()
    {
        var (canvas, group) = await GateKitFixture.GroupedKitAsync();
        var vm = MainViewModelTestHelper.CreateMainViewModel(canvas: canvas);
        vm.WorkspaceSwitch.Current = Workspace.Logic;
        await vm.GateView.PendingRebuild;
        Assert.Empty(vm.GateView.Graph.Nodes);
        canvas.Selection.SelectSingle(group);
        var editor = vm.RightPanel.MakeGate;
        editor.ConfigureForSelection(group, canvas);
        editor.OpenCommand.Execute(null);
        await editor.SimulateCommand.ExecuteAsync(null);
        editor.SelectedFunction = editor.Functions.Single(f => f.Function == GateFunction.Nand);
        Assert.True(editor.MatchesFunction);

        editor.SaveCommand.Execute(null);
        await vm.GateView.PendingRebuild;
        Assert.Equal("NAND", Assert.Single(vm.GateView.Graph.Nodes).DisplayName);
        Assert.False(vm.GateView.IsStale);

        vm.CommandManager.Undo();
        await vm.GateView.PendingRebuild;
        Assert.Empty(vm.GateView.Graph.Nodes);
        vm.CommandManager.Redo();
        await vm.GateView.PendingRebuild;
        Assert.Equal("NAND", Assert.Single(vm.GateView.Graph.Nodes).DisplayName);

        editor.GateName = "MyNand";
        editor.SaveCommand.Execute(null);
        await vm.GateView.PendingRebuild;
        Assert.Equal("MyNand", Assert.Single(vm.GateView.Graph.Nodes).DisplayName);
        vm.CommandManager.Undo();
        await vm.GateView.PendingRebuild;
        Assert.Equal("NAND", Assert.Single(vm.GateView.Graph.Nodes).DisplayName);
    }
}

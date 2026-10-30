using CAP.Avalonia.Commands;
using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels;
using CAP.Avalonia.ViewModels.Canvas;
using CAP.Avalonia.ViewModels.Workspaces;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.Helpers;
using UnitTests.Integration;

namespace UnitTests.UI;

/// <summary>Owns one loaded benchmark document and prepares a real, repeatable command state.</summary>
internal sealed class ResponsivenessFixture(MainViewModel vm, string preferencesPath) : IAsyncDisposable
{
    public MainViewModel Vm { get; } = vm;
    private ComponentViewModel? _historyTarget;
    private double _historyStartX;
    public string Shape => string.Join("|", Vm.Canvas.Components.Count, Vm.Canvas.Connections.Count,
        Vm.Canvas.Components.Count(c => c.Component is ComponentGroup), Vm.WorkspaceSwitch.Current,
        Vm.CommandManager.UndoCount, Vm.CommandManager.RedoCount, Vm.Canvas.Selection.SelectedComponents.Count);

    public static async Task<ResponsivenessFixture> LoadAsync(string example, string commandName)
    {
        var preferences = Path.Combine(Path.GetTempPath(), $"lunima-responsiveness-{Guid.NewGuid():N}.json");
        var fixture = new ResponsivenessFixture(MainViewModelTestHelper.CreateMainViewModel(
            preferencesService: new UserPreferencesService(preferences)), preferences);
        try
        {
            var path = Path.Combine(ExampleDesignFilesTests.ExamplesDirectory(), example);
            (await fixture.Vm.FileOperations.LoadDesignFromPathAsync(path)).ShouldBeTrue(
                $"'{example}' must load through the real load path");
            fixture.Vm.Canvas.Components.ShouldNotBeEmpty();
            fixture.Vm.Canvas.Connections.ShouldNotBeEmpty();
            await fixture.WaitForRoutingAsync();
            fixture.Vm.Home.OnProjectOpened();
            fixture.Vm.Canvas.ShowPowerFlow = false;
            fixture.Vm.CommandManager.ClearHistory();
            await fixture.PrepareAsync(commandName);
            await fixture.WaitForRoutingAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    private async Task PrepareAsync(string commandName)
    {
        if (commandName is "UndoCommand" or "RedoCommand")
        {
            _historyTarget = Vm.Canvas.Components.First(c => !c.Component.IsLocked);
            _historyStartX = _historyTarget.X;
            Vm.CommandManager.ExecuteCommand(new MoveComponentCommand(Vm.Canvas, _historyTarget,
                _historyTarget.X, _historyTarget.Y, _historyTarget.X + 1, _historyTarget.Y));
            _historyTarget.X.ShouldBe(_historyStartX + 1);
            Vm.CommandManager.UndoCount.ShouldBe(1);
            if (commandName == "RedoCommand")
            {
                Vm.UndoCommand.CanExecute(null).ShouldBeTrue();
                await Vm.UndoCommand.ExecuteAsync(null);
                _historyTarget.X.ShouldBe(_historyStartX);
                Vm.CommandManager.RedoCount.ShouldBe(1);
            }
        }
        if (commandName == "MakeGateCommand")
        {
            var group = Vm.Canvas.Components.First(c => c.Component is ComponentGroup { IsDie: false });
            Vm.Canvas.Selection.SelectSingle(group);
            Vm.Canvas.SelectedComponent = group;
            Vm.CanvasInteraction.SelectedComponent = group;
            Vm.CanvasInteraction.SelectionHasDie.ShouldBeFalse();
        }
        if (commandName is "SwitchToLayoutCommand" or "ShowInLayoutCommand")
        {
            Vm.WorkspaceSwitch.Current = Workspace.Logic;
            Vm.CanvasInteraction.SelectedComponent = Vm.Canvas.Components[0];
        }
        if (commandName is "RunWorkspaceCommand" or "RunWorkspaceFromShortcutCommand")
            Vm.WorkspaceSwitch.Current = Workspace.Logic;
        if (commandName.StartsWith("Set", StringComparison.Ordinal) && commandName.EndsWith("ModeCommand", StringComparison.Ordinal))
        {
            if (commandName == "SetSelectModeCommand") Vm.SetConnectModeCommand.Execute(null);
            else Vm.SetSelectModeCommand.Execute(null);
        }
        if (commandName == "ResetZoomCommand") Vm.ViewportControl.ZoomLevel = 1.5;
        if (commandName == "ResetPanCommand") { Vm.Canvas.PanX = 25; Vm.Canvas.PanY = 35; }
        if (commandName == "SelectAllCommand") Vm.Canvas.Selection.ClearSelection();
    }

    public void AssertHistoryEffect(string commandName)
    {
        if (commandName == "UndoCommand")
        {
            _historyTarget!.X.ShouldBe(_historyStartX);
            Vm.CommandManager.UndoCount.ShouldBe(0);
            Vm.CommandManager.RedoCount.ShouldBe(1);
        }
        if (commandName == "RedoCommand")
        {
            _historyTarget!.X.ShouldBe(_historyStartX + 1);
            Vm.CommandManager.UndoCount.ShouldBe(1);
            Vm.CommandManager.RedoCount.ShouldBe(0);
        }
    }

    private async Task WaitForRoutingAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (Vm.Canvas.Routing.IsRouting)
            await Task.Delay(10, deadline.Token);
    }

    public async ValueTask DisposeAsync()
    {
        Vm.Canvas.Routing.CancelRouting();
        try { await WaitForRoutingAsync(); }
        finally
        {
            try { await Vm.DisposeAsync(); }
            finally { if (File.Exists(preferencesPath)) File.Delete(preferencesPath); }
        }
    }
}

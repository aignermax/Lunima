using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using Xunit;

namespace UnitTests.UI.Flows;

/// <summary>Exercises button activation, including the flyout Click handler, without bypassing it via the command.</summary>
[Collection("LocalizationSingleton")]
[Trait("Category", "Slow")]
public class NewDieButtonTests
{
    [AvaloniaFact]
    public void CreateButton_CreatesDieBeforeClosingFlyout()
    {
        using var host = new UiFlowTestHost();
        host.Vm.WorkspaceSwitch.Current = Workspace.System;
        UiInput.RunJobs();
        var open = UiInput.Descendants<Button>(host.Window).Single(b => b.Name == "NewDieButton");
        UiInput.Click(host.Window, open);
        var flyout = (Flyout)open.Flyout!;
        flyout.IsOpen.ShouldBeTrue();
        var content = (Control)flyout.Content!;
        var create = content.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CreateDieButton");
        create.IsEnabled.ShouldBeTrue();
        create.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        UiInput.RunJobs();

        host.Vm.Canvas.Dies.RootDies.Count.ShouldBe(2, "activating Create must execute before its bindings detach");
        flyout.IsOpen.ShouldBeFalse();
        host.Vm.CommandManager.Undo();
        host.Vm.Canvas.Dies.RootDies.ShouldBeEmpty();
    }
}

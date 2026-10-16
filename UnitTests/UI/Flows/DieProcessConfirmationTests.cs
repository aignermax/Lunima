using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Dies;
using CAP.Avalonia.ViewModels.Workspaces;
using Shouldly;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.UI.Flows;

[Collection("LocalizationSingleton")]
[Trait("Category", "Slow")]
public class DieProcessConfirmationTests
{
    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void ConfirmationButtons_CancelThenConfirm(string language)
    {
        using var host = new UiFlowTestHost();
        LocalizationService.Instance.SetLanguage(language);
        host.Vm.WorkspaceSwitch.Current = Workspace.System;
        var oldProcess = Process("SiN", "SiN", "sin");
        var die = Die("Die 1", 0, 0, 1000, 1000, oldProcess, Part("SiN coupler", 200, 200));
        var vm = host.Vm.Canvas.AddComponent(die);
        host.Vm.Canvas.Selection.SelectSingle(vm);
        host.Vm.Canvas.SelectedComponent = vm;
        var props = host.Vm.DieProperties;
        props.ResolvePdkSource = _ => "sin";
        props.ResolveAllowedPdkNames = _ => new[] { "inp" };
        props.Show(die);
        var target = new DieProcessOption("InP", Process("InP", "InP", "inp"));
        props.SelectedProcess = target;
        UiInput.RunJobs();
        var cancel = UiInput.Descendants<Button>(host.Window).Single(b => b.Name == "CancelDieProcessButton");
        var confirm = UiInput.Descendants<Button>(host.Window).Single(b => b.Name == "ConfirmDieProcessButton");
        cancel.IsEffectivelyVisible.ShouldBeTrue();
        confirm.IsEffectivelyVisible.ShouldBeTrue();
        die.ProcessBinding.ShouldBe(oldProcess);
        if (Environment.GetEnvironmentVariable("UI_SHOT_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            ScreenshotArtifacts.SettleAnimations();
            using var frame = host.Window.CaptureRenderedFrame();
            ScreenshotArtifacts.SavePng(frame!, Path.Combine(directory, "die-process-" + language + ".png"));
        }
        Activate(cancel);
        props.HasProcessPreview.ShouldBeFalse();
        props.SelectedProcess!.Selection.ShouldBe(oldProcess);
        die.ChildComponents.Count.ShouldBe(1);
        props.SelectedProcess = target;
        UiInput.RunJobs();
        Activate(confirm);
        die.ChildComponents.ShouldBeEmpty();
        die.ProcessBinding!.DisplayName.ShouldBe("InP");
        host.Vm.CommandManager.Undo();
        die.ChildComponents.Count.ShouldBe(1);
        die.ProcessBinding.ShouldBe(oldProcess);
    }

    private static void Activate(Button button)
    {
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        UiInput.RunJobs();
    }
}

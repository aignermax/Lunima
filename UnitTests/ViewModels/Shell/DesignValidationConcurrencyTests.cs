using Avalonia.Headless.XUnit;
using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels;
using CAP_Core.Components.Core;
using Shouldly;
using UnitTests.UI;
using Xunit;

namespace UnitTests.ViewModels.Shell;

/// <summary>
/// The design checks run on a worker while the UI thread stays free, so the user can keep
/// editing the canvas — and can close the design — before the run finishes. On the largest
/// shipped logic design: edits made while the checks run must not break the run, and
/// retiring the shell mid-run must cancel it without publishing findings.
/// </summary>
[Collection("LocalizationSingleton")]
public class DesignValidationConcurrencyTests
{
    private const string ExampleFileName = "Logic Gate 4-Bit Adder.lun";
    private const string CommandName = "RunDesignChecksCommand";
    private const int EditedGateCount = 3;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    [AvaloniaFact]
    public async Task CanvasEdits_WhileChecksRun_DoNotThrowAndTheRunCompletes()
    {
        await using var loaded = await ResponsivenessFixture.LoadAsync(ExampleFileName, CommandName);
        var vm = loaded.Vm;
        var gates = vm.Canvas.Components
            .Where(c => c.Component is ComponentGroup { IsDie: false } && !c.Component.IsLocked)
            .Take(EditedGateCount)
            .ToList();
        gates.Count.ShouldBe(EditedGateCount);

        var run = vm.RunDesignChecksCommand.ExecuteAsync(null);
        vm.IsRunningDesignChecks.ShouldBeTrue("the checks must still be running while the user edits");
        EditCanvas(vm, gates);

        await Should.NotThrowAsync(() => run.WaitAsync(Deadline));
        vm.IsRunningDesignChecks.ShouldBeFalse();
        vm.RightPanel.DesignValidation.AllFindings.ShouldNotBeEmpty("the run must finish and report its findings");
    }

    [AvaloniaFact]
    public async Task StoppingTheShell_WhileChecksRun_CancelsWithoutPublishingFindings()
    {
        await using var loaded = await ResponsivenessFixture.LoadAsync(ExampleFileName, CommandName);
        var vm = loaded.Vm;

        var run = vm.RunDesignChecksCommand.ExecuteAsync(null);
        vm.IsRunningDesignChecks.ShouldBeTrue();
        await vm.StopAsync().WaitAsync(Deadline);

        await Should.NotThrowAsync(() => run.WaitAsync(Deadline));
        vm.IsRunningDesignChecks.ShouldBeFalse();
        vm.RightPanel.DesignValidation.AllFindings.ShouldBeEmpty("a cancelled run must not commit its findings");
        vm.Canvas.ViewSettings.DesignCheckFindings.ShouldBeEmpty("a cancelled run must not mark the canvas");

        await vm.RunDesignChecksCommand.ExecuteAsync(null).WaitAsync(Deadline);
        vm.RightPanel.DesignValidation.AllFindings.ShouldBeEmpty("a retired shell admits no new run");
    }

    /// <summary>Moves, ungroups and deletes gates through undoable commands, then undoes once.</summary>
    private static void EditCanvas(MainViewModel vm, IReadOnlyList<CAP.Avalonia.ViewModels.Canvas.ComponentViewModel> gates)
    {
        var moved = gates[0];
        vm.CommandManager.ExecuteCommand(new MoveComponentCommand(vm.Canvas, moved, moved.X, moved.Y, moved.X + 1, moved.Y));
        vm.CommandManager.ExecuteCommand(new UngroupCommand(vm.Canvas, (ComponentGroup)gates[1].Component));
        vm.CommandManager.ExecuteCommand(new DeleteComponentCommand(vm.Canvas, gates[2]));
        vm.CommandManager.Undo();
    }
}

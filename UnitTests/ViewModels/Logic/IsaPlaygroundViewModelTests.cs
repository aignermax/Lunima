using System.Globalization;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Acceptance for the ISA playground ViewModel (issue #1194): the window must open
/// demo-ready (first shipped sample pre-assembled), step the golden model to the
/// documented end state, surface assembler errors with their line number, and let
/// Reset restore the power-on state. The default constructor discovers
/// <c>examples/isa/</c> by walking up from the test binaries to the repo root.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundViewModelTests
{
    private const int StepBudget = 500;

    private static string HaltedText => LocalizationService.Instance.Translate("IsaPlayground.StatusHalted");

    [Fact]
    public void Ctor_LoadsAllShippedSamples_AndPreassemblesCountTo5()
    {
        var vm = new IsaPlaygroundViewModel();

        vm.Samples.Count.ShouldBe(4);
        vm.Samples[0].FileName.ShouldBe("count-to-5.asm");
        vm.Samples[3].FileName.ShouldBe("mask-and-invert.asm");
        vm.SelectedSample.ShouldBe(vm.Samples[0]);
        vm.ProgramText.ShouldContain("LOAD 1");
        vm.IsAssembled.ShouldBeTrue();
        vm.ErrorText.ShouldBeEmpty();
        vm.TraceLines.ShouldNotBeEmpty();
    }

    [Fact]
    public void StepUntilHalt_CountTo5_EndsWithAccumulator5()
    {
        var vm = new IsaPlaygroundViewModel();

        for (int i = 0; i < StepBudget && vm.MachineStatusText != HaltedText; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.MachineStatusText.ShouldBe(HaltedText, "count-to-5 must halt within the step budget");
        vm.Accumulator.ShouldBe(5);
    }

    [Fact]
    public void Assemble_UnknownMnemonic_ShowsErrorAndDisablesStepAndReset()
    {
        var vm = new IsaPlaygroundViewModel();

        vm.ProgramText = "LOAD 1\nFROB 2";
        vm.AssembleCommand.Execute(null);

        vm.ErrorText.ShouldContain("2");
        vm.IsAssembled.ShouldBeFalse();
        vm.StepCommand.CanExecute(null).ShouldBeFalse();
        vm.ResetCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Reset_AfterSteps_RestoresPowerOnState()
    {
        var vm = new IsaPlaygroundViewModel();
        vm.StepCommand.Execute(null);
        vm.StepCommand.Execute(null);
        vm.ProgramCounter.ShouldBeGreaterThan(0);

        vm.ResetCommand.Execute(null);

        vm.ProgramCounter.ShouldBe(0);
        vm.Accumulator.ShouldBe(0);
    }

    [Fact]
    public void SelectingOtherSample_LoadsItsSourceAndReassembles()
    {
        var vm = new IsaPlaygroundViewModel();

        vm.SelectedSample = vm.Samples[1];

        vm.SelectedSample.FileName.ShouldBe("add-two-numbers.asm");
        vm.ProgramText.ShouldContain("LOAD 7");
        vm.IsAssembled.ShouldBeTrue();
        vm.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public void EditingProgramText_MarksAssembledStateStale()
    {
        var vm = new IsaPlaygroundViewModel();
        vm.IsAssembled.ShouldBeTrue();

        vm.ProgramText += "\n";

        vm.IsAssembled.ShouldBeFalse();
        vm.StepCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Run_CountTo5_TicksUntilHalt_EndsHaltedWithAccumulator5()
    {
        var vm = new IsaPlaygroundViewModel();
        vm.ToggleRunCommand.Execute(null);
        vm.IsRunning.ShouldBeTrue();

        int ticks = 0;
        while (vm.IsRunning && ticks < StepBudget)
        {
            vm.AdvanceRunTick();
            ticks++;
        }

        vm.IsRunning.ShouldBeFalse("count-to-5 must halt within the step budget");
        vm.Accumulator.ShouldBe(5);
        var expected = string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusHaltedAfterSteps"),
            ticks);
        vm.MachineStatusText.ShouldBe(expected);
    }

    [Fact]
    public void RunCommand_ReturnsImmediately_NoStepsWithoutTicks()
    {
        var vm = new IsaPlaygroundViewModel();

        vm.ToggleRunCommand.Execute(null);

        vm.IsRunning.ShouldBeTrue();
        vm.ProgramCounter.ShouldBe(0, "the run loop must not step synchronously on the UI thread");
        vm.Accumulator.ShouldBe(0);
    }

    [Fact]
    public void Stop_MidRun_LeavesPcAndReenablesStep()
    {
        var vm = new IsaPlaygroundViewModel();
        vm.ToggleRunCommand.Execute(null);
        vm.AdvanceRunTick();
        vm.AdvanceRunTick();
        int pc = vm.ProgramCounter;
        pc.ShouldBeGreaterThan(0);

        vm.ToggleRunCommand.Execute(null); // Stop

        vm.IsRunning.ShouldBeFalse();
        vm.ProgramCounter.ShouldBe(pc);
        vm.StepCommand.CanExecute(null).ShouldBeTrue();
        vm.AdvanceRunTick();
        vm.ProgramCounter.ShouldBe(pc, "ticks after Stop must be no-ops");
    }

    [Fact]
    public void Run_EndlessLoop_StepCapStopsWithMessage()
    {
        var vm = new IsaPlaygroundViewModel();
        vm.ProgramText = "loop: JMP loop";
        vm.AssembleCommand.Execute(null);
        vm.ToggleRunCommand.Execute(null);

        int guard = IsaPlaygroundViewModel.MaxRunSteps + 10;
        while (vm.IsRunning && guard-- > 0)
        {
            vm.AdvanceRunTick();
        }

        vm.IsRunning.ShouldBeFalse("the step cap must stop an endless loop");
        var expected = string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusStepCapReached"),
            IsaPlaygroundViewModel.MaxRunSteps);
        vm.MachineStatusText.ShouldBe(expected);
    }

    [Fact]
    public void WhileRunning_EditorReadOnly_AndAssembleStepDisabled()
    {
        var vm = new IsaPlaygroundViewModel();

        vm.ToggleRunCommand.Execute(null);

        vm.IsEditorReadOnly.ShouldBeTrue();
        vm.RunStopText.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.StopButton"));
        vm.StepCommand.CanExecute(null).ShouldBeFalse();
        vm.AssembleCommand.CanExecute(null).ShouldBeFalse();
    }
}

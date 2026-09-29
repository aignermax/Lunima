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
    public void Ctor_LoadsBothSamples_AndPreassemblesCountTo5()
    {
        var vm = new IsaPlaygroundViewModel();

        vm.Samples.Count.ShouldBe(2);
        vm.Samples[0].FileName.ShouldBe("count-to-5.asm");
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
}

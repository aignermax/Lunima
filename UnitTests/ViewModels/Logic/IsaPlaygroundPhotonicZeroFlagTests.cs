using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Rung-5 acceptance (issue #1322): on the shipped Zero Detect 4-bit network the ISA
/// playground's photonic toggle moves the machine's one branch decision onto light —
/// every <c>JZ</c> asks a <see cref="PhotonicZeroFlag"/> whether the accumulator is
/// zero. The network (inputs A0–A3, tap Z) must not be misdetected as an ADD/AND/NOT
/// ALU, the toggle label and header name the zero flag, count-to-5 runs to HALT with
/// the identical trace the golden model produces, and the flag is consulted exactly
/// once per executed <c>JZ</c>. The Logic Unit 4-bit regression pins that a network
/// without the Z tap does not offer the zero flag.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundPhotonicZeroFlagTests
    : IClassFixture<LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture>,
      IClassFixture<LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture>
{
    private readonly LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture _fixture;
    private readonly LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture _logicUnitFixture;

    /// <summary>Attaches the shared zero-detect and logic-unit fixtures (each assembles its network once).</summary>
    public IsaPlaygroundPhotonicZeroFlagTests(
        LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture fixture,
        LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture logicUnitFixture)
    {
        _fixture = fixture;
        _logicUnitFixture = logicUnitFixture;
    }

    [Fact]
    public void ZeroDetectNetwork_IsNotMisdetectedAsAnAlu_AndOffersTheZeroFlagToggle()
    {
        var vm = CreateVm();

        vm.IsPhotonicZeroFlagAvailable.ShouldBeTrue(
            "the Zero Detect chip exposes A0–A3 in and the Z tap");
        vm.IsPhotonicAddAvailable.ShouldBeFalse("no adder signals (B0–B3, Cin, S0–S3) on the Zero Detect chip");
        vm.IsPhotonicAndAvailable.ShouldBeFalse("no AND signals (B0–B3, Y0–Y3) on the Zero Detect chip");
        vm.IsPhotonicNotAvailable.ShouldBeFalse("no NOT signals (Y0–Y3 / N0–N3) on the Zero Detect chip");
        vm.IsPhotonicToggleEnabled.ShouldBeTrue();
        vm.PhotonicToggleLabel.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.PhotonicZeroFlagToggle"),
            "the toggle must name the photonic zero flag");
        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.Title"),
            "with the toggle off the header still names the golden model");
    }

    [Fact]
    public void ToggleOn_HeaderNamesTheZeroFlag_AndTheMachineHoldsThePhotonicFlag()
    {
        var vm = CreateVm();
        vm.UsePhotonicAdder = true;

        vm.HeaderTitle.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicZeroFlag"),
            "the header must name the photonic zero flag (JZ)");
        vm.ZeroFlag.ShouldNotBeNull("JZ must branch on the photonic zero-detect network");
        vm.PhotonicAlu.ShouldBeNull("the Zero Detect chip cannot compute ADD photonically");
        vm.PhotonicAndAlu.ShouldBeNull("the Zero Detect chip cannot compute AND photonically");
        vm.PhotonicNotAlu.ShouldBeNull("the Zero Detect chip cannot compute NOT photonically");
    }

    [Fact]
    public void CountTo5_OnPhotonicZeroFlag_HaltsWithTheGoldenTrace_ConsultedOncePerJz()
    {
        var vm = CreateVm();
        vm.UsePhotonicAdder = true;
        vm.IsAssembled.ShouldBeTrue(vm.ErrorText);
        vm.ProgramText.ShouldContain("count-to-5");

        var photonicTrace = new List<string>();
        var jzCount = 0;
        var program = new IsaAssembler().Assemble(vm.ProgramText);
        while (vm.MachineStatusText != LocalizationService.Instance.Translate("IsaPlayground.StatusHalted")
               && photonicTrace.Count < IsaPlaygroundViewModel.MaxRunSteps)
        {
            if (IsaInstruction.Decode(program[vm.ProgramCounter], out _)?.Opcode == IsaOpcode.Jz)
            {
                jzCount++;
            }

            vm.StepCommand.Execute(null);
            photonicTrace.Add($"PC={vm.ProgramCounter} ACC={vm.Accumulator} RAM=[{vm.RamText}]");
        }

        vm.Accumulator.ShouldBe(5, "count-to-5 halts with ACC = 5");
        vm.ErrorText.ShouldBeEmpty();
        photonicTrace.ShouldBe(GoldenModelTrace(vm.ProgramText),
            "deciding JZ on light must reproduce the golden model cycle by cycle");
        jzCount.ShouldBeGreaterThan(0, "count-to-5's loop exits on the JZ zero check");
        vm.ZeroFlag!.ConsultationCount.ShouldBe(jzCount,
            "the photonic flag must be consulted exactly once per executed JZ");
    }

    [Fact]
    public void LogicUnitNetwork_DoesNotOfferTheZeroFlag_BehaviourUnchanged()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_logicUnitFixture.Network);
        var vm = new IsaPlaygroundViewModel(provider);

        vm.IsPhotonicZeroFlagAvailable.ShouldBeFalse("the Logic Unit chip has no Z tap");
        vm.IsPhotonicAndAvailable.ShouldBeTrue("the Logic Unit regression: AND still offered");
        vm.IsPhotonicNotAvailable.ShouldBeTrue("the Logic Unit regression: NOT still offered");
        vm.PhotonicToggleLabel.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.PhotonicAndNotToggle"),
            "the toggle label is unchanged without the zero flag");

        vm.UsePhotonicAdder = true;

        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicAndNot"));
        vm.ZeroFlag.ShouldBeNull("JZ stays on the golden zero flag");
    }

    [Fact]
    public void ClearingZeroDetectNetwork_WhileToggleOn_DisablesToggle()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.IsPhotonicZeroFlagAvailable.ShouldBeTrue();

        provider.Clear();

        vm.IsAnyPhotonicAvailable.ShouldBeFalse();
        vm.IsPhotonicToggleEnabled.ShouldBeFalse();
        vm.UsePhotonicAdder.ShouldBeFalse("losing the zero-detect network falls back to the golden flag");
        vm.ZeroFlag.ShouldBeNull();
    }

    /// <summary>A playground with the fixture's zero-detect network published (toggle off).</summary>
    private IsaPlaygroundViewModel CreateVm()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        return new IsaPlaygroundViewModel(provider);
    }

    /// <summary>The golden model's per-step trace of the same program, formatted as the VM renders it.</summary>
    private static List<string> GoldenModelTrace(string source)
    {
        var emulator = new IsaEmulator(new IsaAssembler().Assemble(source));
        var trace = new List<string>();
        while (!emulator.IsHalted && trace.Count < IsaPlaygroundViewModel.MaxRunSteps)
        {
            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join("  ", emulator.Ram)}]");
        }

        emulator.IsHalted.ShouldBeTrue("the golden model must halt within the step cap");
        return trace;
    }
}

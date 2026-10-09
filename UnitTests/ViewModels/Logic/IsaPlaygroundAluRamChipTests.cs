using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Issue #1468 acceptance: on the shipped <c>Logic Gate ALU + RAM.lun</c> chip the ISA
/// playground must put the ALU <b>and</b> the data RAM on light together. The data
/// memory answers to the <c>RAM.</c>-prefixed signal map
/// (<see cref="IsaDataMemorySignalMap.WithPrefix"/>) because the plain names belong to
/// the adder. Multiply-3x4 must halt with ACC = 12 and a per-step trace identical to
/// the golden run, with ALU and RAM both on light in the unit-chip row, and toggle
/// label and header naming both units. The plain RAM 4x4 example keeps resolving
/// through the default map (covered by <see cref="IsaPlaygroundPhotonicDataMemoryTests"/>).
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundAluRamChipTests : IClassFixture<AluRamChipExampleTests.AluRamFixture>
{
    /// <summary>STORE 1, STORE 3 plus one STORE 0 per loop round (4 rounds) in multiply-3x4.</summary>
    private const int ExpectedMultiplyStoreCount = 6;

    /// <summary>Generous bound far above the expected 33 steps; guards against an endless loop.</summary>
    private const int StepBudget = 500;

    private const string MultiplySampleFileName = "multiply-3x4.asm";

    private readonly AluRamChipExampleTests.AluRamFixture _fixture;

    /// <summary>Attaches the shared combined-chip fixture (loads and assembles once).</summary>
    public IsaPlaygroundAluRamChipTests(AluRamChipExampleTests.AluRamFixture fixture) => _fixture = fixture;

    [Fact]
    public void AluRamChip_OffersAddAndDataMemoryTogether_AndNamesBothInLabelAndHeader()
    {
        var vm = CreateVm();

        vm.IsPhotonicAddAvailable.ShouldBeTrue(
            "the adder keeps its plain persisted signal names (A0–A3, B0–B3, Cin → S0–S3, Cout)");
        vm.IsPhotonicDataMemoryAvailable.ShouldBeTrue(
            "the RAM answers to the 'RAM.'-prefixed map on this chip");
        vm.PhotonicToggleLabel.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.PhotonicAdderDataMemoryToggle"),
            "the toggle must name both units: ADD on light and the data RAM on light");
        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.Title"),
            "with the toggle off the header still names the golden model");

        vm.UsePhotonicAdder = true;

        vm.PhotonicAlu.ShouldNotBeNull("the ALU must run on light");
        vm.DataMemory.ShouldNotBeNull("the data RAM must live on the photonic registers");
        vm.IsPhotonicDataMemoryActive.ShouldBeTrue();
        vm.UnitChips[0].IsOnLight.ShouldBeTrue("the ALU chip must show on light");
        vm.UnitChips[2].IsOnLight.ShouldBeTrue("the RAM chip must show on light");
        vm.HeaderTitle.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicAdderDataMemory"),
            "the header must name the adder and the data RAM together");
    }

    [Fact]
    public void MultiplySample_OnPhotonicAluAndMemory_ProducesTheGoldenTrace_AndHaltsWithTwelve()
    {
        var vm = CreateVm();
        vm.UsePhotonicAdder = true;
        vm.SelectedSample = vm.Samples.Single(s => s.FileName == MultiplySampleFileName);
        vm.IsAssembled.ShouldBeTrue(vm.ErrorText);
        vm.PhotonicAlu.ShouldNotBeNull();
        vm.DataMemory.ShouldNotBeNull();

        var photonicTrace = new List<string>();
        while (vm.MachineStatusText != LocalizationService.Instance.Translate("IsaPlayground.StatusHalted")
               && photonicTrace.Count < StepBudget)
        {
            vm.StepCommand.Execute(null);
            photonicTrace.Add($"PC={vm.ProgramCounter} ACC={vm.Accumulator} RAM=[{vm.RamText}]");
        }

        vm.Accumulator.ShouldBe(12, "multiply-3x4 halts with ACC = 12 with both units on light");
        vm.ErrorText.ShouldBeEmpty();
        photonicTrace.ShouldBe(GoldenModelTrace(vm.ProgramText),
            "multiply-3x4 with ALU and RAM on light must reproduce the golden model cycle by cycle");
        vm.DataMemory!.WriteCount.ShouldBe(ExpectedMultiplyStoreCount, "one write per executed STORE");
    }

    /// <summary>A playground with the fixture's combined ALU + RAM network published (toggle off).</summary>
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
        while (!emulator.IsHalted && trace.Count < StepBudget)
        {
            emulator.Step();
            trace.Add($"PC={emulator.ProgramCounter} ACC={emulator.Accumulator} " +
                $"RAM=[{string.Join("  ", emulator.Ram)}]");
        }

        emulator.IsHalted.ShouldBeTrue("the golden model must halt within the step budget");
        return trace;
    }
}

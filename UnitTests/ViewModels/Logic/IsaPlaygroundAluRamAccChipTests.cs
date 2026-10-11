using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Issue #1479 acceptance: on the shipped <c>Logic Gate ALU + RAM + ACC.lun</c> chip
/// the ISA playground must put the ALU, the data RAM <b>and</b> the accumulator on
/// light together. The accumulator answers to the default
/// <see cref="IsaAccumulatorSignalMap"/> (<c>ACC.D0</c>–<c>ACC.D3</c>,
/// <c>ACC.LOAD</c> → <c>ACC.Q0</c>–<c>ACC.Q3</c>), so it never collides with the
/// adder's plain names or the RAM's <c>RAM.</c> prefix. Multiply-3x4 must halt with
/// ACC = 12 and a per-step trace identical to the golden run, with ALU, RAM and ACC
/// all on light in the unit-chip row, toggle label and header naming all three units,
/// and a photonic accumulator write count proving the register really was clocked on
/// light. On the plain ALU + RAM chip (no register) the ACC unit chip stays grey.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundAluRamAccChipTests : IClassFixture<AluRamAccChipExampleTests.AluRamAccFixture>
{
    /// <summary>Generous bound far above the expected 33 steps; guards against an endless loop.</summary>
    private const int StepBudget = 500;

    private const string MultiplySampleFileName = "multiply-3x4.asm";

    private readonly AluRamAccChipExampleTests.AluRamAccFixture _fixture;

    /// <summary>Attaches the shared combined-chip fixture (loads and assembles once).</summary>
    public IsaPlaygroundAluRamAccChipTests(AluRamAccChipExampleTests.AluRamAccFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void AluRamAccChip_OffersAllThreeUnits_AndNamesThemInLabelAndHeader()
    {
        var vm = CreateVm();

        vm.IsPhotonicAddAvailable.ShouldBeTrue(
            "the adder keeps its plain persisted signal names (A0–A3, B0–B3, Cin → S0–S3, Cout)");
        vm.IsPhotonicDataMemoryAvailable.ShouldBeTrue(
            "the RAM answers to the 'RAM.'-prefixed map on this chip");
        vm.IsPhotonicAccumulatorAvailable.ShouldBeTrue(
            "the register exposes the accumulator signals ACC.D0–ACC.D3, ACC.LOAD → ACC.Q0–ACC.Q3");
        vm.PhotonicToggleLabel.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.PhotonicAdderDataMemoryAccumulatorToggle"),
            "the toggle must name all three units: ADD, the data RAM and the accumulator on light");
        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.Title"),
            "with the toggle off the header still names the golden model");

        vm.UsePhotonicAdder = true;

        vm.PhotonicAlu.ShouldNotBeNull("the ALU must run on light");
        vm.DataMemory.ShouldNotBeNull("the data RAM must live on the photonic registers");
        vm.PhotonicAccumulator.ShouldNotBeNull("the accumulator must be clocked on the photonic register");
        vm.UnitChips[0].IsOnLight.ShouldBeTrue("the ALU chip must show on light");
        vm.UnitChips[2].IsOnLight.ShouldBeTrue("the RAM chip must show on light");
        vm.UnitChips[3].IsOnLight.ShouldBeTrue("the ACC chip must show on light");
        vm.UnitChips[4].IsOnLight.ShouldBeFalse("the PC stays electronic for now");
        vm.HeaderTitle.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicAdderDataMemoryAccumulator"),
            "the header must name the adder, the data RAM and the accumulator together");
    }

    [Fact]
    public void MultiplySample_OnThreePhotonicUnits_ProducesTheGoldenTrace_AndHaltsWithTwelve()
    {
        var vm = CreateVm();
        vm.UsePhotonicAdder = true;
        vm.SelectedSample = vm.Samples.Single(s => s.FileName == MultiplySampleFileName);
        vm.IsAssembled.ShouldBeTrue(vm.ErrorText);
        vm.PhotonicAlu.ShouldNotBeNull();
        vm.DataMemory.ShouldNotBeNull();
        vm.PhotonicAccumulator.ShouldNotBeNull();

        var photonicTrace = new List<string>();
        while (vm.MachineStatusText != LocalizationService.Instance.Translate("IsaPlayground.StatusHalted")
               && photonicTrace.Count < StepBudget)
        {
            vm.StepCommand.Execute(null);
            photonicTrace.Add($"PC={vm.ProgramCounter} ACC={vm.Accumulator} RAM=[{vm.RamText}]");
        }

        vm.Accumulator.ShouldBe(12, "multiply-3x4 halts with ACC = 12 with all three units on light");
        vm.ErrorText.ShouldBeEmpty();
        photonicTrace.ShouldBe(GoldenModelTrace(vm.ProgramText),
            "multiply-3x4 with ALU, RAM and ACC on light must reproduce the golden model cycle by cycle");
        vm.PhotonicAccumulator!.WriteCount.ShouldBeGreaterThan(0,
            "every instruction writes the ACC — the accumulator really was clocked on light");
    }

    /// <summary>A playground with the fixture's combined ALU + RAM + ACC network published (toggle off).</summary>
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

/// <summary>
/// The counterpart of <see cref="IsaPlaygroundAluRamAccChipTests"/> on the plain
/// ALU + RAM chip (no register block): the playground must not offer the accumulator
/// there and the ACC unit chip stays grey — every other example behaves
/// byte-identically to before issue #1479.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundAluRamChipAccStaysGreyTests : IClassFixture<AluRamChipExampleTests.AluRamFixture>
{
    private readonly AluRamChipExampleTests.AluRamFixture _fixture;

    /// <summary>Attaches the shared ALU + RAM fixture (loads and assembles once).</summary>
    public IsaPlaygroundAluRamChipAccStaysGreyTests(AluRamChipExampleTests.AluRamFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void AluRamChip_KeepsTheAccChipGrey()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider);

        vm.IsPhotonicAccumulatorAvailable.ShouldBeFalse(
            "the ALU + RAM chip has no ACC.D0–ACC.D3 register, so the accumulator stays electronic");

        vm.UsePhotonicAdder = true;

        vm.PhotonicAccumulator.ShouldBeNull("no photonic accumulator is constructed on this chip");
        vm.UnitChips[0].IsOnLight.ShouldBeTrue("the ALU chip still shows on light");
        vm.UnitChips[2].IsOnLight.ShouldBeTrue("the RAM chip still shows on light");
        vm.UnitChips[3].IsOnLight.ShouldBeFalse("the ACC chip stays grey on the ALU + RAM chip");
    }
}

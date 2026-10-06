using System.Diagnostics;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Rung-5 acceptance (issue #1446): with the shipped RAM 4x4 network built, the ISA
/// playground's photonic toggle moves the machine's data RAM onto light through
/// <see cref="PhotonicDataMemory"/> — the step from photonic calculator to photonic
/// computer. Multiply-3x4 must step to HALT with the identical PC/ACC/RAM trace the
/// golden model produces, with exactly one write per executed STORE, and the RAM
/// readout must carry the "on light" chip while the memory is photonic. With only
/// the 4-bit adder built the RAM stays golden and the chip is hidden. A budget test
/// pins one UI step far under 100 ms, so stepping stays synchronous.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundPhotonicDataMemoryTests
    : IClassFixture<LogicGateRam4x4ExampleTests.Ram4x4Fixture>,
      IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    /// <summary>STORE 1, STORE 3 plus one STORE 0 per loop round (4 rounds) in multiply-3x4.</summary>
    private const int ExpectedMultiplyStoreCount = 6;

    /// <summary>Generous bound far above the expected 33 steps; guards against an endless loop.</summary>
    private const int StepBudget = 500;

    private const string MultiplySampleFileName = "multiply-3x4.asm";

    /// <summary>Generous bound on the 100 ms UI budget for one photonic step (actual: microseconds).</summary>
    private static readonly TimeSpan UiBudget = TimeSpan.FromMilliseconds(100);

    private readonly LogicGateRam4x4ExampleTests.Ram4x4Fixture _ramFixture;
    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _adderFixture;

    /// <summary>Attaches the shared RAM 4x4 and 4-bit-adder fixtures (each assembles its network once).</summary>
    public IsaPlaygroundPhotonicDataMemoryTests(
        LogicGateRam4x4ExampleTests.Ram4x4Fixture ramFixture,
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture adderFixture)
    {
        _ramFixture = ramFixture;
        _adderFixture = adderFixture;
    }

    [Fact]
    public void RamNetwork_OffersTheDataMemoryToggle_AndNamesItInLabelAndHeader()
    {
        var vm = CreateRamVm();

        vm.IsPhotonicDataMemoryAvailable.ShouldBeTrue(
            "the RAM 4x4 chip exposes A0/A1, LOAD, D0–D3 in and Q0–Q3 out");
        vm.IsPhotonicAddAvailable.ShouldBeFalse("no adder signals on the RAM chip");
        vm.IsPhotonicZeroFlagAvailable.ShouldBeFalse("no Z tap on the RAM chip");
        vm.IsPhotonicToggleEnabled.ShouldBeTrue();
        vm.PhotonicToggleLabel.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.PhotonicDataMemoryToggle"),
            "the toggle must name the photonic data RAM");
        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.Title"),
            "with the toggle off the header still names the golden model");
        vm.IsPhotonicDataMemoryActive.ShouldBeFalse("the chip marker only shows in photonic mode");

        vm.UsePhotonicAdder = true;

        vm.HeaderTitle.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicDataMemory"),
            "the header must name the photonic data RAM");
        vm.DataMemory.ShouldNotBeNull("the RAM must live on the photonic registers");
        vm.IsPhotonicDataMemoryActive.ShouldBeTrue("the RAM readout must carry the 'on light' chip");
    }

    [Fact]
    public void MultiplySample_OnPhotonicMemory_ProducesTheGoldenTrace_AndWritesOncePerStore()
    {
        var photonic = CreateRamVm();
        photonic.UsePhotonicAdder = true;
        photonic.SelectedSample = photonic.Samples.Single(s => s.FileName == MultiplySampleFileName);
        photonic.IsAssembled.ShouldBeTrue(photonic.ErrorText);
        photonic.DataMemory.ShouldNotBeNull(
            "selecting the sample re-creates the machine with the photonic data memory");

        var photonicTrace = new List<string>();
        while (photonic.MachineStatusText != LocalizationService.Instance.Translate("IsaPlayground.StatusHalted")
               && photonicTrace.Count < StepBudget)
        {
            photonic.StepCommand.Execute(null);
            photonicTrace.Add($"PC={photonic.ProgramCounter} ACC={photonic.Accumulator} RAM=[{photonic.RamText}]");
        }

        photonic.Accumulator.ShouldBe(12, "multiply-3x4 halts with ACC = 12");
        photonic.ErrorText.ShouldBeEmpty();
        photonicTrace.ShouldBe(GoldenModelTrace(photonic.ProgramText),
            "multiply-3x4 with its RAM on light must reproduce the golden model cycle by cycle");
        photonic.DataMemory!.WriteCount.ShouldBe(ExpectedMultiplyStoreCount,
            "one write per executed STORE");
    }

    [Fact]
    public void AdderNetwork_Only_KeepsTheRamGolden_AndHidesTheChip()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_adderFixture.Network);
        var vm = new IsaPlaygroundViewModel(provider);

        vm.IsPhotonicDataMemoryAvailable.ShouldBeFalse("the 4-bit adder exposes no RAM signals");
        vm.IsPhotonicAddAvailable.ShouldBeTrue("the adder regression: ADD still offered");

        vm.UsePhotonicAdder = true;

        vm.DataMemory.ShouldBeNull("the RAM stays on the golden model");
        vm.IsPhotonicDataMemoryActive.ShouldBeFalse("the 'on light' chip stays hidden");
        vm.PhotonicAlu.ShouldNotBeNull("the adder still runs ADD photonically");
    }

    [Fact]
    public void Step_OnPhotonicMemory_StaysFarUnderUiBudget()
    {
        var vm = CreateRamVm();
        vm.UsePhotonicAdder = true;
        vm.SelectedSample = vm.Samples.Single(s => s.FileName == MultiplySampleFileName);
        vm.DataMemory.ShouldNotBeNull();

        // Warm-up: the first steps pay JIT and network prime-up; the UI budget
        // applies to the steady-state step, so measure after a full first run.
        RunToHalt(vm);
        vm.ResetCommand.Execute(null);

        var worst = TimeSpan.Zero;
        while (vm.MachineStatusText != LocalizationService.Instance.Translate("IsaPlayground.StatusHalted"))
        {
            var watch = Stopwatch.StartNew();
            vm.StepCommand.Execute(null);
            watch.Stop();
            if (watch.Elapsed > worst)
            {
                worst = watch.Elapsed;
            }
        }

        worst.ShouldBeLessThan(UiBudget,
            "one playground step with photonic data memory must stay far under the 100 ms UI budget");
    }

    /// <summary>A playground with the fixture's RAM 4x4 network published (toggle off).</summary>
    private IsaPlaygroundViewModel CreateRamVm()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_ramFixture.Network);
        return new IsaPlaygroundViewModel(provider);
    }

    private static void RunToHalt(IsaPlaygroundViewModel vm)
    {
        var steps = 0;
        while (vm.MachineStatusText != LocalizationService.Instance.Translate("IsaPlayground.StatusHalted")
               && steps < StepBudget)
        {
            vm.StepCommand.Execute(null);
            steps++;
        }
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

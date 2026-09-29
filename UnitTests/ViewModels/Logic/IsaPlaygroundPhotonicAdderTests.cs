using System.Diagnostics;
using System.Globalization;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Analysis.LogicAnalysis;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Analysis.LogicAnalysis;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Rung-5 slice-3 acceptance (issue #1215): the ISA playground's "Compute ADD on the
/// photonic chip" toggle swaps the emulator's ALU to <see cref="PhotonicAdderAlu"/>
/// wrapping the network the Logic tab published through
/// <see cref="BuiltLogicNetworkProvider"/>. With the shipped 4-bit adder published,
/// count-to-5 must reach the same end state as on the golden model and the status
/// must name the photonic adder; with no (or a non-adder) network the toggle stays
/// disabled and the golden ALU keeps running. Also pins the 100 ms UI budget:
/// a photonic ADD is a truth-table walk, far below it, so stepping stays synchronous.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundPhotonicAdderTests : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>
{
    private const int StepBudget = 500;

    /// <summary>Generous bound on the 100 ms UI budget for one photonic ADD (actual: microseconds).</summary>
    private static readonly TimeSpan UiBudget = TimeSpan.FromMilliseconds(100);

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _fixture;

    /// <summary>Attaches the shared 4-bit-adder fixture (assembles the network once).</summary>
    public IsaPlaygroundPhotonicAdderTests(LogicGateFourBitAdderExampleTests.FourBitAdderFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void PhotonicToggle_CountTo5RunToHalt_MatchesGoldenEndState_AndNamesPhotonicAdder()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var photonic = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        var golden = new IsaPlaygroundViewModel();

        RunToHalt(photonic);
        RunToHalt(golden);

        photonic.Accumulator.ShouldBe(5);
        photonic.ProgramCounter.ShouldBe(golden.ProgramCounter);
        photonic.RamText.ShouldBe(golden.RamText);
        photonic.MachineStatusText.ShouldBe(golden.MachineStatusText);

        var expectedStatus = string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicAdd"),
            _fixture.Network.Gates.Count);
        photonic.PhotonicStatusText.ShouldBe(expectedStatus,
            "the last executed ADD ran photonically, so the status must name the photonic adder");
    }

    [Fact]
    public void ProviderEmpty_ToggleDisabled_AndGoldenAluUsed()
    {
        var vm = new IsaPlaygroundViewModel();

        vm.IsPhotonicAddAvailable.ShouldBeFalse();
        vm.IsPhotonicToggleEnabled.ShouldBeFalse();

        vm.UsePhotonicAdder = true;
        vm.UsePhotonicAdder.ShouldBeFalse("the toggle must not stick without an adder network");

        RunToHalt(vm);
        vm.Accumulator.ShouldBe(5);
        vm.PhotonicStatusText.ShouldBeEmpty("no photonic ADD ran, so no photonic status line");
    }

    [Fact]
    public void NonAdderNetwork_ToggleDisabled()
    {
        var provider = new BuiltLogicNetworkProvider();
        var vm = new IsaPlaygroundViewModel(provider);

        provider.Publish(BuildSingleNandNetwork());

        vm.IsPhotonicAddAvailable.ShouldBeFalse("a half-adder-style network lacks the adder signals");
        vm.IsPhotonicToggleEnabled.ShouldBeFalse();
    }

    [Fact]
    public void TogglingPhotonic_ResetsMachineToPowerOnState()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider);
        for (var i = 0; i < 4; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.ProgramCounter.ShouldBeGreaterThan(0);

        vm.UsePhotonicAdder = true;

        vm.ProgramCounter.ShouldBe(0);
        vm.Accumulator.ShouldBe(0);
        vm.PhotonicStatusText.ShouldBeEmpty();
    }

    [Fact]
    public void ClearingProvider_WhileToggleOn_FallsBackToGoldenAlu()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.IsPhotonicAddAvailable.ShouldBeTrue();

        provider.Clear();

        vm.IsPhotonicAddAvailable.ShouldBeFalse();
        vm.UsePhotonicAdder.ShouldBeFalse("losing the adder network must fall back to the golden ALU");
        RunToHalt(vm);
        vm.Accumulator.ShouldBe(5);
    }

    [Fact]
    public void PhotonicAdd_OnShippedAdder_StaysFarUnderUiBudget()
    {
        var alu = new PhotonicAdderAlu(_fixture.Network);

        // Warm-up: the first call pays JIT and dictionary prime-up; the UI budget
        // applies to the steady-state ADD a Step executes, so measure after it.
        alu.Add(3, 4);

        var worst = TimeSpan.Zero;
        for (var a = 0; a <= IsaMachine.MaxDataValue; a++)
        for (var b = 0; b <= IsaMachine.MaxDataValue; b++)
        {
            var watch = Stopwatch.StartNew();
            alu.Add(a, b);
            watch.Stop();
            if (watch.Elapsed > worst)
            {
                worst = watch.Elapsed;
            }
        }

        worst.ShouldBeLessThan(UiBudget,
            "one photonic ADD must stay far under the 100 ms UI budget, so a step never blocks the UI thread");
    }

    /// <summary>Steps the playground's machine to HALT within the shared step budget.</summary>
    private static void RunToHalt(IsaPlaygroundViewModel vm)
    {
        var haltedText = LocalizationService.Instance.Translate("IsaPlayground.StatusHalted");
        for (var i = 0; i < StepBudget && vm.MachineStatusText != haltedText; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.MachineStatusText.ShouldBe(haltedText, "count-to-5 must halt within the step budget");
    }

    /// <summary>One NAND gate (inputs A, B; output Y): a network the photonic ALU must reject.</summary>
    private static LogicNetworkEvaluator BuildSingleNandNetwork() =>
        new(
            inputPinNames: new List<string> { "A", "B" },
            gates: new Dictionary<string, LogicGateModel> { ["nand"] = PinnedGateTables.NandGate() },
            inputWiring: new Dictionary<LogicPinRef, LogicNetDriver>
            {
                [new LogicPinRef("nand", "A")] = new LogicNetDriver.NetworkInput("A"),
                [new LogicPinRef("nand", "B")] = new LogicNetDriver.NetworkInput("B"),
            },
            outputTaps: new Dictionary<string, LogicPinRef> { ["Y"] = new LogicPinRef("nand", "Y") });
}

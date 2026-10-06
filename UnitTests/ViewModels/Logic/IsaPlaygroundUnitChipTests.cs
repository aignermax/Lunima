using CAP.Avalonia.Services;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Acceptance for the "what runs on light" row (issue #1456): one chip per machine
/// unit (ALU, Z, RAM, ACC, PC) must report green exactly while that unit is
/// computed by the built photonic network — the 4-bit adder lights only ALU, the
/// RAM 4x4 lights only RAM, the Zero Detect lights only Z, ACC and PC are always
/// electronic — and with the toggle off (or no network) every chip is grey.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundUnitChipTests
    : IClassFixture<LogicGateFourBitAdderExampleTests.FourBitAdderFixture>,
      IClassFixture<LogicGateRam4x4ExampleTests.Ram4x4Fixture>,
      IClassFixture<LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture>
{
    private const int AluIndex = 0;
    private const int ZeroFlagIndex = 1;
    private const int RamIndex = 2;
    private const int AccIndex = 3;
    private const int PcIndex = 4;

    private readonly LogicGateFourBitAdderExampleTests.FourBitAdderFixture _adderFixture;
    private readonly LogicGateRam4x4ExampleTests.Ram4x4Fixture _ramFixture;
    private readonly LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture _zeroFixture;

    /// <summary>Attaches the shared network fixtures (each assembles its example once).</summary>
    public IsaPlaygroundUnitChipTests(
        LogicGateFourBitAdderExampleTests.FourBitAdderFixture adderFixture,
        LogicGateRam4x4ExampleTests.Ram4x4Fixture ramFixture,
        LogicGateZeroDetect4BitExampleTests.ZeroDetectFixture zeroFixture)
    {
        _adderFixture = adderFixture;
        _ramFixture = ramFixture;
        _zeroFixture = zeroFixture;
    }

    [Fact]
    public void Chips_AreNamedAluZRamAccPc_InMachineOrder()
    {
        var vm = new IsaPlaygroundViewModel(new BuiltLogicNetworkProvider());

        vm.UnitChips.Select(chip => chip.UnitName)
            .ShouldBe(new[] { "ALU", "Z", "RAM", "ACC", "PC" });
    }

    [Fact]
    public void NoNetwork_ToggleOff_AllChipsElectronic()
    {
        var vm = new IsaPlaygroundViewModel(new BuiltLogicNetworkProvider());

        vm.IsPhotonicToggleEnabled.ShouldBeFalse("no accepted network, no toggle");
        AssertChips(vm, alu: false, z: false, ram: false, acc: false, pc: false);
    }

    [Fact]
    public void AdderNetwork_ToggleOn_LightsOnlyAlu()
    {
        var vm = CreateVm(_adderFixture.Network);
        vm.UsePhotonicAdder = true;

        vm.PhotonicAlu.ShouldNotBeNull("the adder network must drive the ADD path");
        AssertChips(vm, alu: true, z: false, ram: false, acc: false, pc: false);
    }

    [Fact]
    public void RamNetwork_ToggleOn_LightsOnlyRam()
    {
        var vm = CreateVm(_ramFixture.Network);
        vm.UsePhotonicAdder = true;

        vm.DataMemory.ShouldNotBeNull("the RAM 4x4 network must hold the data memory");
        AssertChips(vm, alu: false, z: false, ram: true, acc: false, pc: false);
    }

    [Fact]
    public void ZeroDetectNetwork_ToggleOn_LightsOnlyZ()
    {
        var vm = CreateVm(_zeroFixture.Network);
        vm.UsePhotonicAdder = true;

        vm.ZeroFlag.ShouldNotBeNull("the Zero Detect network must answer JZ");
        AssertChips(vm, alu: false, z: true, ram: false, acc: false, pc: false);
    }

    [Fact]
    public void ToggleOff_AfterPhotonicRun_AllChipsElectronicAgain()
    {
        var vm = CreateVm(_ramFixture.Network);
        vm.UsePhotonicAdder = true;
        AssertChips(vm, alu: false, z: false, ram: true, acc: false, pc: false);

        vm.UsePhotonicAdder = false;

        AssertChips(vm, alu: false, z: false, ram: false, acc: false, pc: false);
    }

    /// <summary>A playground with <paramref name="network"/> published (toggle still off).</summary>
    private static IsaPlaygroundViewModel CreateVm(CAP_Core.Analysis.LogicAnalysis.LogicNetworkEvaluator network)
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(network);
        return new IsaPlaygroundViewModel(provider);
    }

    private static void AssertChips(
        IsaPlaygroundViewModel vm, bool alu, bool z, bool ram, bool acc, bool pc)
    {
        vm.UnitChips[AluIndex].IsOnLight.ShouldBe(alu, "ALU chip");
        vm.UnitChips[ZeroFlagIndex].IsOnLight.ShouldBe(z, "Z chip");
        vm.UnitChips[RamIndex].IsOnLight.ShouldBe(ram, "RAM chip");
        vm.UnitChips[AccIndex].IsOnLight.ShouldBe(acc, "ACC chip — always electronic for now");
        vm.UnitChips[PcIndex].IsOnLight.ShouldBe(pc, "PC chip — always electronic for now");
    }
}

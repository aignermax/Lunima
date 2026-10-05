using System.Globalization;
using CAP.Avalonia.Services;
using CAP.Avalonia.Services.Localization;
using CAP.Avalonia.ViewModels.Logic.IsaPlayground;
using CAP_Core.Logic.Isa;
using Shouldly;
using UnitTests.Integration;
using Xunit;

namespace UnitTests.ViewModels.Logic;

/// <summary>
/// Rung-5 acceptance (issue #1295): on the shipped Logic Unit 4-bit chip the ISA
/// playground's photonic toggle runs AND and NOT both on light — AND keeps the
/// default Y0–Y3 taps, NOT reads its own N0–N3 taps through
/// <see cref="IsaAluSignalMap.CombinedLogicUnitNot"/>. The toggle label and the
/// header name both operations, every one of the 16 NOT inputs yields
/// ~A &amp; 0xF and every one of the 256 (A, B) pairs yields A &amp; B, and the
/// per-step status line names the photonic operation that just ran. The #1284
/// regression pins stay in <see cref="IsaPlaygroundPhotonicAndTests"/> (AND-only
/// chip keeps NOT golden) and <see cref="IsaPlaygroundPhotonicNotTests"/>
/// (NOT-only chip keeps the default map).
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundLogicUnitTests
    : IClassFixture<LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture>
{
    private readonly LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture _fixture;

    /// <summary>Attaches the shared logic-unit fixture (assembles the network once).</summary>
    public IsaPlaygroundLogicUnitTests(LogicGateLogicUnit4BitExampleTests.LogicUnit4BitFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void LogicUnitNetwork_EnablesToggle_WithCombinedLabelAndCombinedHeader()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider);

        vm.IsPhotonicAndAvailable.ShouldBeTrue("the Logic Unit chip exposes A0–A3, B0–B3 and Y0–Y3");
        vm.IsPhotonicNotAvailable.ShouldBeTrue(
            "the Logic Unit chip exposes the combined-map NOT taps N0–N3");
        vm.IsPhotonicAddAvailable.ShouldBeFalse("the Logic Unit chip has no adder signals");
        vm.IsPhotonicToggleEnabled.ShouldBeTrue();
        vm.PhotonicToggleLabel.ShouldBe(
            LocalizationService.Instance.Translate("IsaPlayground.PhotonicAndNotToggle"),
            "the toggle must name both operations that run on light");

        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.Title"));

        vm.UsePhotonicAdder = true;

        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicAndNot"),
            "the header must name both photonic operations");
        vm.PhotonicAndAlu.ShouldNotBeNull("the machine must wrap the AND network photonically");
        vm.PhotonicNotAlu.ShouldNotBeNull("NOT must run photonically through the combined map");
        vm.PhotonicAlu.ShouldBeNull("the Logic Unit chip cannot compute ADD photonically");
    }

    [Fact]
    public void PhotonicNot_OnLogicUnit_YieldsTheComplementForAll16Inputs()
    {
        var vm = CreatePhotonicVm();

        for (var a = 0; a < 16; a++)
        {
            vm.PhotonicNotAlu!.Not(a).ShouldBe(~a & 0xF, $"NOT {a} on the N0–N3 taps");
        }
    }

    [Fact]
    public void PhotonicAnd_OnLogicUnit_YieldsTheAndForAll256OperandPairs()
    {
        var vm = CreatePhotonicVm();

        for (var a = 0; a < 16; a++)
        for (var b = 0; b < 16; b++)
        {
            vm.PhotonicAndAlu!.And(a, b).ShouldBe(a & b, $"{a} AND {b} on the Y0–Y3 taps");
        }
    }

    [Fact]
    public void MaskAndInvertProgram_StepsReportBothPhotonicOperations_AndEndsWithAcc7()
    {
        var vm = CreatePhotonicVm();
        vm.ProgramText = "LOAD 10\nSTORE 0\nLOAD 12\nAND 0\nNOT\nSTORE 1\nHALT";
        vm.AssembleCommand.Execute(null);

        for (var i = 0; i < 4; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.PhotonicStatusText.ShouldBe(string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicAnd"),
            "1100", "1010", "1000", _fixture.Network.Gates.Count));

        vm.StepCommand.Execute(null);

        vm.PhotonicStatusText.ShouldBe(string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicNot"),
            "1000", "0111", _fixture.Network.Gates.Count));

        vm.StepCommand.Execute(null);
        vm.StepCommand.Execute(null);

        vm.Accumulator.ShouldBe(7, "~(1100 & 1010) & 0xF = 0111");
        vm.MachineStatusText.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.StatusHalted"));
    }

    [Fact]
    public void ClearingLogicUnitNetwork_WhileToggleOn_DisablesToggle()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.IsAnyPhotonicAvailable.ShouldBeTrue();

        provider.Clear();

        vm.IsAnyPhotonicAvailable.ShouldBeFalse();
        vm.IsPhotonicToggleEnabled.ShouldBeFalse();
        vm.UsePhotonicAdder.ShouldBeFalse("losing the accepted network must fall back to the golden ALU");
    }

    /// <summary>A playground on the fixture's network with the photonic toggle already on.</summary>
    private IsaPlaygroundViewModel CreatePhotonicVm()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.PhotonicAndAlu.ShouldNotBeNull("the fixture network must run AND photonically");
        vm.PhotonicNotAlu.ShouldNotBeNull("the fixture network must run NOT photonically");
        return vm;
    }
}

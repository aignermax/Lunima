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
/// Rung-5 acceptance (issue #1284): the ISA playground's photonic toggle also works
/// when the network the Logic tab built is the shipped 4-bit AND chip. With that
/// network published through <see cref="BuiltLogicNetworkProvider"/> the toggle is
/// enabled, its label and the header name AND (never "photonic adder" or "photonic
/// NOT"), and <c>LOAD 12 / STORE 1 / LOAD 10 / AND 1 / STORE 0 / HALT</c> ends with
/// ACC = 8, RAM[0] = 8 and a status line that names the photonic AND with both
/// operands and the result in binary plus the gate count. Regression pin: NOT on
/// that network must fall back to the golden model — the AND chip exposes A0–A3 in
/// / Y0–Y3 out, so <see cref="PhotonicNotAlu.Accepts"/> matches it, but its Y taps
/// carry A &amp; B with B tied to 0, not ~A.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundPhotonicAndTests : IClassFixture<LogicGateAnd4BitExampleTests.And4BitFixture>
{
    private readonly LogicGateAnd4BitExampleTests.And4BitFixture _fixture;

    /// <summary>Attaches the shared AND-4-bit fixture (assembles the network once).</summary>
    public IsaPlaygroundPhotonicAndTests(LogicGateAnd4BitExampleTests.And4BitFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void AndNetwork_EnablesToggle_WithAndLabelAndAndHeader()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider);

        vm.IsPhotonicAndAvailable.ShouldBeTrue(
            "the shipped AND 4-bit network exposes A0–A3, B0–B3 and Y0–Y3");
        vm.IsPhotonicNotAvailable.ShouldBeFalse(
            "NOT must never be offered on the AND chip — its Y taps carry A & 0 there, not ~A");
        vm.IsPhotonicToggleEnabled.ShouldBeTrue();
        vm.PhotonicToggleLabel.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.PhotonicAndToggle"),
            "the toggle must name the operation that will run on light");

        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.Title"));

        vm.UsePhotonicAdder = true;

        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicAnd"),
            "the header must name the photonic AND, never the adder or NOT");
        vm.PhotonicAndAlu.ShouldNotBeNull("the machine must wrap the AND network photonically");
        vm.PhotonicAlu.ShouldBeNull("an AND-only network cannot compute ADD photonically");
        vm.PhotonicNotAlu.ShouldBeNull("NOT must stay golden on the AND chip");
    }

    [Fact]
    public void AndProgram_OnPhotonicToggle_EndsWithAcc8_AndNamesThePhotonicAnd()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.ProgramText = "LOAD 12\nSTORE 1\nLOAD 10\nAND 1\nSTORE 0\nHALT";
        vm.AssembleCommand.Execute(null);

        for (var i = 0; i < 6; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.Accumulator.ShouldBe(8, "1100 & 1010 = 1000");
        vm.RamText.ShouldStartWith("8");
        vm.PhotonicStatusText.ShouldBe(string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicAnd"),
            "1010", "1100", "1000", _fixture.Network.Gates.Count));
    }

    [Fact]
    public void NotProgram_OnAndNetwork_FallsBackToGoldenNot()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.ProgramText = "LOAD 5\nNOT\nHALT";
        vm.AssembleCommand.Execute(null);

        vm.StepCommand.Execute(null);
        vm.StepCommand.Execute(null);

        vm.Accumulator.ShouldBe(10, "~5 & 0xF = 10 — the golden NOT, not the AND chip's A & 0 = 0");
        vm.PhotonicNotAlu.ShouldBeNull();
        vm.PhotonicStatusText.ShouldBeEmpty("a golden NOT leaves no photonic status line");
    }

    [Fact]
    public void NoNetwork_ToggleDisabled_AndHintNamesAllThreeExamples()
    {
        var vm = new IsaPlaygroundViewModel();

        vm.IsAnyPhotonicAvailable.ShouldBeFalse();
        vm.IsPhotonicToggleEnabled.ShouldBeFalse();

        var hint = LocalizationService.Instance.Translate("IsaPlayground.PhotonicAdderHint");
        hint.ShouldContain("adder", Case.Insensitive,
            customMessage: "the hint names the 4-bit adder example");
        hint.ShouldContain("NOT 4-bit",
            customMessage: "the hint names the NOT 4-bit example");
        hint.ShouldContain("AND 4-bit",
            customMessage: "the hint names the AND 4-bit example");
    }

    [Fact]
    public void ClearingAndNetwork_WhileToggleOn_DisablesToggle()
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
}

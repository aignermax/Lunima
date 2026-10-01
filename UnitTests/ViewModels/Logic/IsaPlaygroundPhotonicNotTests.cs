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
/// Rung-5 acceptance (issue #1275): the ISA playground's photonic toggle also works
/// when the network the Logic tab built is the shipped 4-bit NOT chip. With that
/// network published through <see cref="BuiltLogicNetworkProvider"/> the toggle is
/// enabled, its label and the header name NOT (never "photonic adder"), and
/// <c>LOAD 5 / NOT / STORE 0 / HALT</c> ends with ACC = 10 and a status line that
/// names the photonic NOT with operand and result in binary plus the gate count.
/// ADDs on that network keep running on the golden model (the composite ALU's
/// fallback), and the adder path stays untouched.
/// </summary>
[Collection("LocalizationSingleton")]
public class IsaPlaygroundPhotonicNotTests : IClassFixture<LogicGateNot4BitExampleTests.Not4BitFixture>
{
    private readonly LogicGateNot4BitExampleTests.Not4BitFixture _fixture;

    /// <summary>Attaches the shared NOT-4-bit fixture (assembles the network once).</summary>
    public IsaPlaygroundPhotonicNotTests(LogicGateNot4BitExampleTests.Not4BitFixture fixture) =>
        _fixture = fixture;

    [Fact]
    public void NotNetwork_EnablesToggle_WithNotLabelAndNotHeader()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider);

        vm.IsPhotonicNotAvailable.ShouldBeTrue("the shipped NOT 4-bit network exposes A0–A3 and Y0–Y3");
        vm.IsPhotonicToggleEnabled.ShouldBeTrue();
        vm.PhotonicToggleLabel.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.PhotonicNotToggle"),
            "the toggle must name the operation that will run on light");

        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.Title"));

        vm.UsePhotonicAdder = true;

        vm.HeaderTitle.ShouldBe(LocalizationService.Instance.Translate("IsaPlayground.TitlePhotonicNot"),
            "the header must never claim 'photonic adder' while NOT runs on light");
        vm.PhotonicNotAlu.ShouldNotBeNull("the machine must wrap the NOT network photonically");
        vm.PhotonicAlu.ShouldBeNull("a NOT-only network cannot compute ADD photonically");
    }

    [Fact]
    public void NotProgram_OnPhotonicToggle_EndsWithAcc10_AndNamesThePhotonicNot()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.ProgramText = "LOAD 5\nNOT\nSTORE 0\nHALT";
        vm.AssembleCommand.Execute(null);

        for (var i = 0; i < 4; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.Accumulator.ShouldBe(10, "~5 & 0xF = 10");
        vm.RamText.ShouldStartWith("10");
        vm.PhotonicStatusText.ShouldBe(string.Format(
            CultureInfo.InvariantCulture,
            LocalizationService.Instance.Translate("IsaPlayground.StatusPhotonicNot"),
            "0101", "1010", _fixture.Network.Gates.Count));
    }

    [Fact]
    public void AddInstruction_OnNotNetwork_FallsBackToGoldenAlu()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.ProgramText = "LOAD 5\nSTORE 0\nLOAD 3\nADD 0\nHALT";
        vm.AssembleCommand.Execute(null);

        for (var i = 0; i < 4; i++)
        {
            vm.StepCommand.Execute(null);
        }

        vm.Accumulator.ShouldBe(8, "ADD still computes correctly on the golden fallback");
        vm.PhotonicStatusText.ShouldBeEmpty("a golden ADD leaves no photonic status line");
    }

    [Fact]
    public void TogglingOff_OnNotNetwork_FallsBackToGoldenNot()
    {
        var provider = new BuiltLogicNetworkProvider();
        provider.Publish(_fixture.Network);
        var vm = new IsaPlaygroundViewModel(provider) { UsePhotonicAdder = true };
        vm.ProgramText = "LOAD 5\nNOT\nHALT";
        vm.AssembleCommand.Execute(null);

        vm.UsePhotonicAdder = false;
        vm.PhotonicNotAlu.ShouldBeNull();

        vm.StepCommand.Execute(null);
        vm.StepCommand.Execute(null);

        vm.Accumulator.ShouldBe(10);
        vm.PhotonicStatusText.ShouldBeEmpty("a golden NOT leaves no photonic status line");
    }

    [Fact]
    public void ClearingNotNetwork_WhileToggleOn_DisablesToggle()
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

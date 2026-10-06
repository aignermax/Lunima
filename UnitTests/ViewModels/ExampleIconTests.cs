using CAP.Avalonia.Services;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

public class ExampleIconTests
{
    [Theory]
    [InlineData("EBeam Add-Drop Ring", "⭕")]
    [InlineData("Mach-Zehnder Interferometer", "〰️")]
    [InlineData("Logic Gate NOT-NAND", "⚡")]
    [InlineData("Logic Gate AND 4-bit", "⚡")]
    [InlineData("Logic Gate Half Adder", "🔢")]
    [InlineData("Logic Gate RAM 4x4", "🧮")]
    [InlineData("Two Chiplets - Edge-Coupler Link", "🔗")]
    [InlineData("Something Else", "🧪")]
    public void For_PicksTheFamilyIcon(string name, string icon) => ExampleIcon.For(name).ShouldBe(icon);

    [Fact]
    public void For_MatchesWholeWordsOnly()
    {
        ExampleIcon.For("Ordinary Waveguide").ShouldBe("🧪", "'OR' inside 'Ordinary' is not a logic gate");
    }

    [Theory]
    [InlineData("Logic Gate RAM 4x4", "RAM 4x4")]
    [InlineData("EBeam Add-Drop Ring", "EBeam Add-Drop Ring")]
    public void ShortName_DropsTheLogicGatePrefix(string name, string expected) => ExampleIcon.ShortName(name).ShouldBe(expected);
}

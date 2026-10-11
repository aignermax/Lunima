using CAP.Avalonia.Services;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

public class ExampleIconTests
{
    [Theory]
    [InlineData("EBeam Add-Drop Ring", "Icon.CircleDot")]
    [InlineData("Mach-Zehnder Interferometer", "Icon.Waves")]
    [InlineData("Logic Gate NOT-NAND", "Icon.Zap")]
    [InlineData("Logic Gate AND 4-bit", "Icon.Zap")]
    [InlineData("Logic Gate Half Adder", "Icon.Calculator")]
    [InlineData("Logic Gate RAM 4x4", "Icon.MemoryStick")]
    [InlineData("Two Chiplets - Edge-Coupler Link", "Icon.Link")]
    [InlineData("Something Else", "Icon.FlaskConical")]
    public void For_PicksTheFamilyIcon(string name, string icon) => ExampleIcon.For(name).ShouldBe(icon);

    [Fact]
    public void For_MatchesWholeWordsOnly()
    {
        ExampleIcon.For("Ordinary Waveguide").ShouldBe("Icon.FlaskConical", "'OR' inside 'Ordinary' is not a logic gate");
    }

    [Theory]
    [InlineData("Logic Gate RAM 4x4", "RAM 4x4")]
    [InlineData("EBeam Add-Drop Ring", "EBeam Add-Drop Ring")]
    public void ShortName_DropsTheLogicGatePrefix(string name, string expected) => ExampleIcon.ShortName(name).ShouldBe(expected);
}

using CAP_Core.Update;
using Shouldly;

namespace UnitTests.Update;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V2.0.0", 2, 0, 0)]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1.2.3-beta", 1, 2, 3)]
    [InlineData("1.2.3-rc1", 1, 2, 3)]
    public void Parse_ValidVersionStrings_ReturnsExpectedComponents(
        string input, int major, int minor, int patch)
    {
        var version = SemanticVersion.Parse(input);

        version.Major.ShouldBe(major);
        version.Minor.ShouldBe(minor);
        version.Patch.ShouldBe(patch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1.x.3")]
    public void Parse_InvalidStrings_ThrowsFormatException(string input)
    {
        Should.Throw<FormatException>(() => SemanticVersion.Parse(input));
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("v2.0.0", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("abc", false)]
    public void TryParse_ReturnsExpectedResult(string? input, bool expectedSuccess)
    {
        var success = SemanticVersion.TryParse(input, out var result);

        success.ShouldBe(expectedSuccess);
        if (expectedSuccess)
            result.ShouldNotBeNull();
        else
            result.ShouldBeNull();
    }

    [Fact]
    public void CompareTo_SameVersion_ReturnsZero()
    {
        var a = new SemanticVersion(1, 2, 3);
        var b = new SemanticVersion(1, 2, 3);

        a.CompareTo(b).ShouldBe(0);
        a.Equals(b).ShouldBeTrue();
        (a == b).ShouldBeFalse(); // reference equality
        a.Equals((object)b).ShouldBeTrue();
    }

    [Fact]
    public void GreaterThan_NewerVersion_ReturnsTrue()
    {
        var current = new SemanticVersion(1, 0, 0);
        var newer = new SemanticVersion(1, 1, 0);

        (newer > current).ShouldBeTrue();
        (current > newer).ShouldBeFalse();
    }

    [Theory]
    [InlineData(2, 0, 0, 1, 9, 9)]   // major wins
    [InlineData(1, 2, 0, 1, 1, 99)]  // minor wins
    [InlineData(1, 1, 1, 1, 1, 0)]   // patch wins
    public void CompareVersions_MajorMinorPatchPrecedence(
        int aMaj, int aMin, int aPatch, int bMaj, int bMin, int bPatch)
    {
        var a = new SemanticVersion(aMaj, aMin, aPatch);
        var b = new SemanticVersion(bMaj, bMin, bPatch);

        (a > b).ShouldBeTrue();
        (b < a).ShouldBeTrue();
        (a >= b).ShouldBeTrue();
        (b <= a).ShouldBeTrue();
    }

    [Fact]
    public void ToString_ReturnsFormattedVersion()
    {
        var v = new SemanticVersion(1, 2, 3);
        v.ToString().ShouldBe("1.2.3");
    }

    [Fact]
    public void GetHashCode_EqualVersions_SameHash()
    {
        var a = new SemanticVersion(1, 2, 3);
        var b = new SemanticVersion(1, 2, 3);

        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void CompareTo_Null_ReturnsPositive()
    {
        var v = new SemanticVersion(1, 0, 0);
        v.CompareTo(null).ShouldBeGreaterThan(0);
    }
}

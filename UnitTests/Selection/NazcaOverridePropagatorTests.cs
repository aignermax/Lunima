using CAP.Avalonia.Selection;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.Selection;

/// <summary>Tests for <see cref="NazcaOverridePropagator"/>.</summary>
public class NazcaOverridePropagatorTests
{
    [Fact]
    public void Propagate_CopiesOverrideUnderNewId_AsIndependentDeepCopy()
    {
        var overrides = new Dictionary<string, NazcaCodeOverride>
        {
            ["A"] = new NazcaCodeOverride
            {
                RawCode = "import nazca",
                OverridePins = new() { new OverridePinData { Name = "a0" } },
            },
        };
        var map = new Dictionary<string, string> { ["A"] = "A_1" };

        var count = NazcaOverridePropagator.Propagate(map, overrides);

        count.ShouldBe(1);
        overrides.ShouldContainKey("A_1");
        overrides["A_1"].RawCode.ShouldBe("import nazca");
        overrides["A_1"].ShouldNotBeSameAs(overrides["A"]);
        overrides["A_1"].OverridePins.ShouldNotBeSameAs(overrides["A"].OverridePins);

        overrides["A_1"].RawCode = "edited";
        overrides["A"].RawCode.ShouldBe("import nazca", "Editing the copy must not change the source override.");
    }

    [Fact]
    public void Propagate_NoOverrideForSourceId_ReturnsZeroAndAddsNothing()
    {
        var overrides = new Dictionary<string, NazcaCodeOverride>();
        var map = new Dictionary<string, string> { ["A"] = "A_1" };

        NazcaOverridePropagator.Propagate(map, overrides).ShouldBe(0);
        overrides.ShouldNotContainKey("A_1");
    }

    [Fact]
    public void Propagate_DoesNotOverwriteExistingTargetOverride()
    {
        var existing = new NazcaCodeOverride { RawCode = "keep" };
        var overrides = new Dictionary<string, NazcaCodeOverride>
        {
            ["A"] = new NazcaCodeOverride { RawCode = "src" },
            ["A_1"] = existing,
        };
        var map = new Dictionary<string, string> { ["A"] = "A_1" };

        NazcaOverridePropagator.Propagate(map, overrides).ShouldBe(0);
        overrides["A_1"].ShouldBeSameAs(existing);
    }

    [Fact]
    public void Propagate_IdentityMapping_IsSkipped()
    {
        var overrides = new Dictionary<string, NazcaCodeOverride> { ["A"] = new NazcaCodeOverride { RawCode = "x" } };
        var map = new Dictionary<string, string> { ["A"] = "A" };

        NazcaOverridePropagator.Propagate(map, overrides).ShouldBe(0);
    }

    [Fact]
    public void Propagate_MultipleMappings_CopiesOnlyThoseWithSourceOverrides()
    {
        var overrides = new Dictionary<string, NazcaCodeOverride>
        {
            ["A"] = new NazcaCodeOverride { RawCode = "a" },
            ["C"] = new NazcaCodeOverride { RawCode = "c" },
        };
        var map = new Dictionary<string, string> { ["A"] = "A_1", ["B"] = "B_1", ["C"] = "C_1" };

        NazcaOverridePropagator.Propagate(map, overrides).ShouldBe(2);
        overrides.ShouldContainKey("A_1");
        overrides.ShouldContainKey("C_1");
        overrides.ShouldNotContainKey("B_1");
    }
}

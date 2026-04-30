using System.Numerics;
using CAP_DataAccess.Import;
using Shouldly;
using Xunit;

namespace UnitTests.Import;

/// <summary>
/// Unit tests for the pure port-name reconciliation helpers
/// (<see cref="PortNameMapping"/>). Drives the import flow's decision
/// "do we need a mapping dialog?" and the subsequent relabel.
/// </summary>
public class PortNameMappingTests
{
    [Fact]
    public void NamesAlignWithComponent_AllImportedNamesPresent_True()
    {
        var imported = new[] { "in", "out1", "out2" };
        var component = new[] { "in", "out1", "out2", "extra" }; // extras allowed

        PortNameMapping.NamesAlignWithComponent(imported, component).ShouldBeTrue();
    }

    [Fact]
    public void NamesAlignWithComponent_GenericLumericalNames_False()
    {
        var imported = new[] { "port 1", "port 2", "port 3" };
        var component = new[] { "in", "out1", "out2" };

        PortNameMapping.NamesAlignWithComponent(imported, component).ShouldBeFalse();
    }

    [Fact]
    public void NamesAlignWithComponent_CaseInsensitive_True()
    {
        // Lumerical occasionally capitalises; we don't want to spuriously
        // trigger the mapping dialog over a casing difference.
        var imported = new[] { "IN", "OUT1" };
        var component = new[] { "in", "out1" };

        PortNameMapping.NamesAlignWithComponent(imported, component).ShouldBeTrue();
    }

    [Fact]
    public void BuildDefaultMapping_KeepsMatchingNames_PositionalForOthers()
    {
        // Mixed case: one name already matches a component pin, the other
        // doesn't — the matching one stays put, the rest fall back positionally.
        var imported = new[] { "in", "port 2", "port 3" };
        var component = new[] { "in", "out1", "out2" };

        var mapping = PortNameMapping.BuildDefaultMapping(imported, component);

        mapping["in"].ShouldBe("in");           // matched
        mapping["port 2"].ShouldBe("out1");     // positional (index 1)
        mapping["port 3"].ShouldBe("out2");     // positional (index 2)
    }

    [Fact]
    public void BuildDefaultMapping_PortCountMismatch_Throws()
    {
        // A positional fallback would silently drop or fabricate a port
        // — fail loud instead so the import flow can surface a clear error.
        var imported = new[] { "port 1", "port 2" };
        var component = new[] { "in", "out1", "out2" };

        Should.Throw<ArgumentException>(() =>
            PortNameMapping.BuildDefaultMapping(imported, component));
    }

    [Fact]
    public void Remap_RewritesPortNames_KeepsDataAndOrderingIntact()
    {
        var imported = new ImportedSParameters
        {
            PortCount = 3,
            PortNames = new List<string> { "port 1", "port 2", "port 3" },
            SMatricesByWavelengthNm =
            {
                [1550] = new Complex[3, 3]
                {
                    { new(0.1, 0), new(0.7, 0), new(0.7, 0) },
                    { new(0.7, 0), new(0.0, 0), new(0.0, 0) },
                    { new(0.7, 0), new(0.0, 0), new(0.0, 0) }
                }
            }
        };

        var mapping = new Dictionary<string, string>
        {
            ["port 1"] = "in",
            ["port 2"] = "out1",
            ["port 3"] = "out2"
        };

        var result = PortNameMapping.Remap(imported, mapping);

        result.PortNames.ShouldBe(new[] { "in", "out1", "out2" });
        result.PortCount.ShouldBe(3);
        // The matrix data points to the same array — relabelling alone is
        // enough; we don't permute rows/columns because the applicator keys
        // on names, not positions.
        result.SMatricesByWavelengthNm[1550].ShouldBeSameAs(imported.SMatricesByWavelengthNm[1550]);
    }

    [Fact]
    public void Remap_MissingTargetForOneName_Throws()
    {
        // Any imported name without a mapping entry is a contract violation:
        // we'd otherwise drop it on the floor and shift the matrix indexing
        // by one, producing physically wrong S-matrices in subtle silence.
        var imported = new ImportedSParameters
        {
            PortCount = 2,
            PortNames = new List<string> { "port 1", "port 2" },
        };
        var mapping = new Dictionary<string, string> { ["port 1"] = "in" };

        Should.Throw<ArgumentException>(() => PortNameMapping.Remap(imported, mapping));
    }
}

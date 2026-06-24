using CAP_Core.Components.Process;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Verifies the single-PDK-per-design enforcement policy (issue #570).
/// </summary>
public class SinglePdkPolicyTests
{
    // ── DetermineActivePdk ────────────────────────────────────────────────────

    [Fact]
    public void DetermineActivePdk_EmptyList_ReturnsNull()
    {
        SinglePdkPolicy.DetermineActivePdk(Array.Empty<string?>()).ShouldBeNull();
    }

    [Fact]
    public void DetermineActivePdk_AllNullOrEmpty_ReturnsNull()
    {
        SinglePdkPolicy.DetermineActivePdk(new string?[] { null, "", "  " }).ShouldBeNull();
    }

    [Fact]
    public void DetermineActivePdk_SinglePdk_ReturnsThatPdk()
    {
        var result = SinglePdkPolicy.DetermineActivePdk(new[] { "AMF-Si", "AMF-Si", null });
        result.ShouldBe("AMF-Si");
    }

    [Fact]
    public void DetermineActivePdk_DominantPdk_ReturnsMostFrequent()
    {
        var sources = new[] { "HHI-InP", "AMF-Si", "HHI-InP", "HHI-InP", "AMF-Si" };
        SinglePdkPolicy.DetermineActivePdk(sources).ShouldBe("HHI-InP");
    }

    // ── FindConflictingPdks ───────────────────────────────────────────────────

    [Fact]
    public void FindConflictingPdks_UniformDesign_ReturnsEmpty()
    {
        var conflicts = SinglePdkPolicy.FindConflictingPdks(
            new[] { "AMF-Si", "AMF-Si", null }, "AMF-Si");
        conflicts.ShouldBeEmpty();
    }

    [Fact]
    public void FindConflictingPdks_MixedDesign_ReturnsForeignPdks()
    {
        var sources = new string?[] { "HHI-InP", "AMF-Si", "AMF-Si", null };
        var conflicts = SinglePdkPolicy.FindConflictingPdks(sources, "AMF-Si");
        conflicts.Count.ShouldBe(1);
        conflicts[0].ShouldBe("HHI-InP");
    }

    [Fact]
    public void FindConflictingPdks_IsCaseInsensitive()
    {
        var sources = new string?[] { "amf-si", "AMF-Si" };
        SinglePdkPolicy.FindConflictingPdks(sources, "AMF-Si").ShouldBeEmpty();
    }

    // ── CheckPlacement ────────────────────────────────────────────────────────

    [Fact]
    public void CheckPlacement_NoActivePdk_AllowsAnyComponent()
    {
        var (ok, reason) = SinglePdkPolicy.CheckPlacement(null, "AMF-Si");
        ok.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact]
    public void CheckPlacement_BuiltInComponent_AlwaysAllowed()
    {
        var (ok, _) = SinglePdkPolicy.CheckPlacement("AMF-Si", null);
        ok.ShouldBeTrue();

        (ok, _) = SinglePdkPolicy.CheckPlacement("AMF-Si", "");
        ok.ShouldBeTrue();
    }

    [Fact]
    public void CheckPlacement_SamePdk_IsAllowed()
    {
        var (ok, reason) = SinglePdkPolicy.CheckPlacement("AMF-Si", "AMF-Si");
        ok.ShouldBeTrue();
        reason.ShouldBeNull();
    }

    [Fact]
    public void CheckPlacement_DifferentPdk_IsBlocked()
    {
        var (ok, reason) = SinglePdkPolicy.CheckPlacement("AMF-Si", "HHI-InP");
        ok.ShouldBeFalse();
        reason.ShouldNotBeNull();
        reason!.ShouldContain("HHI-InP");
        reason.ShouldContain("AMF-Si");
    }

    [Fact]
    public void CheckPlacement_IsCaseInsensitive()
    {
        var (ok, _) = SinglePdkPolicy.CheckPlacement("amf-si", "AMF-Si");
        ok.ShouldBeTrue();
    }

    [Fact]
    public void CheckPlacement_BothNull_IsAllowed()
    {
        var (ok, _) = SinglePdkPolicy.CheckPlacement(null, null);
        ok.ShouldBeTrue();
    }
}

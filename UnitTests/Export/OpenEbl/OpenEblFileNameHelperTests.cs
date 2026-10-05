using CAP.Avalonia.Services.OpenEblCheck;
using Shouldly;
using Xunit;

namespace UnitTests.Export.OpenEbl;

/// <summary>
/// Unit tests for <see cref="OpenEblFileNameHelper"/>: the proposed openEBL file name must
/// carry the course-category prefix and be safe on every filesystem (umlauts folded,
/// spaces/slashes replaced, locale-independent).
/// </summary>
public class OpenEblFileNameHelperTests
{
    [Theory]
    [InlineData(OpenEblCourseCategory.EBeam, "EBeam_alice_MZI.gds")]
    [InlineData(OpenEblCourseCategory.Elec413, "ELEC413_alice_MZI.gds")]
    [InlineData(OpenEblCourseCategory.SiepicPassives, "SiEPIC_Passives_alice_MZI.gds")]
    [InlineData(OpenEblCourseCategory.OpenEbl, "openEBL_alice_MZI.gds")]
    public void ProposeFileName_SupportsAllCoursePrefixes(
        OpenEblCourseCategory category, string expected)
    {
        OpenEblFileNameHelper.ProposeFileName("alice", "MZI", category).ShouldBe(expected);
    }

    [Fact]
    public void ProposeFileName_DefaultsToEBeamPrefix()
    {
        OpenEblFileNameHelper.ProposeFileName("alice", "MZI").ShouldBe("EBeam_alice_MZI.gds");
    }

    [Fact]
    public void ProposeFileName_UmlautsAreFoldedToBaseLetters()
    {
        OpenEblFileNameHelper.ProposeFileName("Jürgen Müller", "Größe MZI")
            .ShouldBe("EBeam_Jurgen_Muller_Grosse_MZI.gds");
    }

    [Fact]
    public void ProposeFileName_SpacesAndSlashesBecomeUnderscores()
    {
        OpenEblFileNameHelper.ProposeFileName("jonas", "designs/v2\\final draft")
            .ShouldBe("EBeam_jonas_designs_v2_final_draft.gds");
    }

    [Fact]
    public void ProposeFileName_UnsafeCharactersBecomeUnderscores()
    {
        OpenEblFileNameHelper.ProposeFileName("a<b>:c", "d|e?f*g")
            .ShouldBe("EBeam_a_b__c_d_e_f_g.gds");
    }

    [Fact]
    public void ProposeFileName_EmptyPartsFallBackToPlaceholders()
    {
        OpenEblFileNameHelper.ProposeFileName("", "  ")
            .ShouldBe("EBeam_anonymous_design.gds");
    }
}

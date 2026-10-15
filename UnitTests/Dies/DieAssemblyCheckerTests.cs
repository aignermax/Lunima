using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.Dies.DieTestFixtures;

namespace UnitTests.Dies;

/// <summary>Design checks for die assemblies (overlap, parts off their die, facets off the edge).</summary>
public class DieAssemblyCheckerTests
{
    private readonly DieAssemblyChecker _checker = new();

    [Fact]
    public void SingleDieDesign_HasNoDieFindings()
    {
        _checker.Check(new[] { Part("p", -100, -100) }).ShouldBeEmpty();
    }

    [Fact]
    public void CleanAssembly_HasNoFindings()
    {
        var a = Die("A", 0, 0, 1000, 500, null, Part("p", 100, 100), EdgeCoupler("ec", 0, 200));
        var b = Die("B", 1002, 0, 1000, 500);

        _checker.Check(new Component[] { a, b }).ShouldBeEmpty();
    }

    [Fact]
    public void OverlappingDies_AreReported()
    {
        var a = Die("A", 0, 0, 1000, 500);
        var b = Die("B", 900, 0, 1000, 500);

        var issue = _checker.Check(new Component[] { a, b }).Single();

        issue.Type.ShouldBe(DesignIssueType.DieOverlap);
        issue.Description.ShouldContain("'A'");
        issue.LocalizationKey.ShouldBe("DesignChecks.DieOverlap");
    }

    [Fact]
    public void PartOnTheBareCarrier_IsReported()
    {
        var issues = _checker.Check(new Component[] { Die("A", 0, 0, 500, 500), Part("stray", 2000, 0) });

        issues.Single().Type.ShouldBe(DesignIssueType.ComponentOffDie);
        issues.Single().LocalizationKey.ShouldBe("DesignChecks.ComponentNotOnDie");
    }

    [Fact]
    public void PartStickingOutOfItsDie_IsReported()
    {
        var die = Die("A", 0, 0, 500, 500, null, Part("big", 480, 10, 50, 20));

        var issue = _checker.Check(new Component[] { die }).Single();

        issue.Type.ShouldBe(DesignIssueType.ComponentOffDie);
        issue.LocalizationKey.ShouldBe("DesignChecks.ComponentOutsideDie");
    }

    [Fact]
    public void EdgeCouplerAwayFromTheEdge_IsReported()
    {
        var die = Die("A", 0, 0, 1000, 500, null, EdgeCoupler("ec", 200, 200));

        var issue = _checker.Check(new Component[] { die }).Single();

        issue.Type.ShouldBe(DesignIssueType.EdgeCouplerOffDieEdge);
        issue.Description.ShouldContain("200.0");
    }
}

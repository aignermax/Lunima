using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using CAP_Core.Routing;
using CAP_Core.Routing.WaveguideCompanions;
using Shouldly;
using Xunit;

namespace UnitTests.Analysis;

/// <summary>
/// Keep-out companion bands in the spacing check: keep-out bands of two different wires must
/// not overlap (core gap ≥ sum of both keep-out growths), except around a crossing of the two
/// wires; trench/cladding bands never produce a finding.
/// </summary>
public class WaveguideSpacingKeepOutTests
{
    private const double KeepOut = 3.0;
    private readonly WaveguideSpacingDetector _detector = new();

    [Fact]
    public void ParallelWires_WithOverlappingKeepOutBands_AreFlagged()
    {
        // Centers 5 µm apart → core gap 4.5 µm < 2 × 3 µm keep-out.
        var a = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 0, 100, 0);
        var b = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 5, 100, 5);

        var issues = Detect(a, b, _ => KeepOut);

        var issue = issues.ShouldHaveSingleItem();
        issue.Type.ShouldBe(DesignIssueType.WaveguideSpacingViolation);
        issue.LocalizationKey.ShouldBe("DesignChecks.WaveguideKeepOutOverlap");
        issue.Description.ShouldContain("Keep-out bands overlap");
        issue.Description.ShouldContain("keep-out needs 6.00");
    }

    [Fact]
    public void ParallelWires_WithSeparateKeepOutBands_AreClean()
    {
        // Centers 7 µm apart → core gap 6.5 µm ≥ 6 µm.
        var a = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 0, 100, 0);
        var b = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 7, 100, 7);

        Detect(a, b, _ => KeepOut).ShouldBeEmpty();
    }

    [Fact]
    public void OnlyOneWireWithKeepOut_TheOtherCoreMustStayOutOfIt()
    {
        var a = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 0, 100, 0);
        var near = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 3, 100, 3);
        var far = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 4, 100, 4);

        Detect(a, near, c => c == a ? KeepOut : 0).ShouldHaveSingleItem();
        Detect(a, far, c => c == a ? KeepOut : 0).ShouldBeEmpty();
    }

    [Fact]
    public void WithoutKeepOut_SpacingCheckIsUnchanged()
    {
        var a = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 0, 100, 0);
        var b = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 5, 100, 5);

        Detect(a, b, keepOut: null).ShouldBeEmpty();
        Detect(a, b, _ => 0).ShouldBeEmpty();
    }

    [Fact]
    public void MinSpacingViolation_KeepsTheSpacingMessage()
    {
        // Core gap 1.5 µm violates the 2 µm minimum itself — reported as plain spacing.
        var a = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 0, 100, 0);
        var b = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 2, 100, 2);

        var issue = _detector.DetectViolations(new[] { a, b }, Array.Empty<ComponentGroup>(), 2.0,
            keepOutForConnection: _ => KeepOut).ShouldHaveSingleItem();

        issue.LocalizationKey.ShouldBe("DesignChecks.WaveguideSpacingViolation");
    }

    [Fact]
    public void WiresCrossingEachOther_AreNotFlaggedAroundTheCrossing()
    {
        // b crosses a at (50, 0), then leaves diagonally: its second segment starts 10 µm
        // from a — inside the 2 × 6 µm keep-out, but only because of the crossing.
        var a = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 0, 100, 0);
        var b = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegments(
            new StraightSegment(50, -50, 50, 10, 90),
            new StraightSegment(50, 10, 100, 60, 45));

        Detect(a, b, _ => 6.0).ShouldBeEmpty();
    }

    [Fact]
    public void PerConnectionChecker_UsesTheEndpointProcessKeepOut()
    {
        var a = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 0, 100, 0);
        var b = WaveguideSpacingDetectorTestHelpers.CreateConnectionWithSegment(0, 5, 100, 5);
        var keepOut = new ConnectionDrcRules(Array.Empty<WaveguideMinWidthRule>(), 0)
        {
            Companions = new WaveguideCompanionSpec(new[]
            {
                new WaveguideCompanionLayer("K", 40, 0, WaveguideCompanionRole.KeepOut, KeepOut, null),
            }),
        };
        var trench = new ConnectionDrcRules(Array.Empty<WaveguideMinWidthRule>(), 0)
        {
            Companions = new WaveguideCompanionSpec(new[]
            {
                new WaveguideCompanionLayer("T", 2, 0, WaveguideCompanionRole.Trench, 8.0, null),
            }),
        };

        new PerConnectionDrcChecker().Check(new[] { a, b }, Array.Empty<ComponentGroup>(), 0, null, _ => keepOut)
            .ShouldHaveSingleItem().LocalizationKey.ShouldBe("DesignChecks.WaveguideKeepOutOverlap");
        new PerConnectionDrcChecker().Check(new[] { a, b }, Array.Empty<ComponentGroup>(), 0, null, _ => trench)
            .ShouldBeEmpty();
    }

    private List<DesignIssue> Detect(
        WaveguideConnection a, WaveguideConnection b, Func<WaveguideConnection, double>? keepOut) =>
        _detector.DetectViolations(new[] { a, b }, Array.Empty<ComponentGroup>(), 0, keepOutForConnection: keepOut);
}

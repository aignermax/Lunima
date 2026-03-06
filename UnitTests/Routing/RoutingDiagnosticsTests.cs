using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Routing;

/// <summary>
/// Unit tests for RoutingDiagnostics path validation.
/// </summary>
public class RoutingDiagnosticsTests
{
    private readonly RoutingDiagnostics _diagnostics = new(10.0);

    [Fact]
    public void Validate_EmptyPath_ReturnsError()
    {
        var path = new RoutedPath();
        var report = _diagnostics.Validate(path);

        report.IsValid.ShouldBeFalse();
        report.ErrorCount.ShouldBeGreaterThan(0);
        report.Issues.ShouldContain(i => i.Message.Contains("no segments"));
    }

    [Fact]
    public void Validate_SingleStraightSegment_ReturnsValid()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));

        var report = _diagnostics.Validate(path);

        report.IsValid.ShouldBeTrue();
        report.ErrorCount.ShouldBe(0);
    }

    [Fact]
    public void Validate_ValidBendRadius_ReturnsValid()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 50, 0, 0));
        path.Segments.Add(new BendSegment(50, 10, 10.0, 0, 90));

        var report = _diagnostics.Validate(path);

        report.Issues
            .Where(i => i.Message.Contains("bend radius"))
            .ShouldBeEmpty("Valid bend radius should not produce errors");
    }

    [Fact]
    public void Validate_BendRadiusTooSmall_ReturnsError()
    {
        var path = new RoutedPath();
        // Create a bend with radius 3µm (below minimum 10µm)
        path.Segments.Add(new BendSegment(0, 3, 3.0, 0, 90));

        var report = _diagnostics.Validate(path);

        report.IsValid.ShouldBeFalse();
        report.Issues.ShouldContain(i =>
            i.Severity == RoutingIssueSeverity.Error &&
            i.Message.Contains("bend radius"));
    }

    [Fact]
    public void Validate_SegmentGap_ReturnsError()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 50, 0, 0));
        // Big gap: next segment starts at (60,0) instead of (50,0)
        path.Segments.Add(new StraightSegment(60, 0, 100, 0, 0));

        var report = _diagnostics.Validate(path);

        report.IsValid.ShouldBeFalse();
        report.Issues.ShouldContain(i =>
            i.Severity == RoutingIssueSeverity.Error &&
            i.Message.Contains("gap"));
    }

    [Fact]
    public void Validate_ConnectedSegments_ReturnsValid()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 50, 0, 0));
        path.Segments.Add(new StraightSegment(50, 0, 100, 0, 0));

        var report = _diagnostics.Validate(path);

        report.Issues
            .Where(i => i.Message.Contains("gap"))
            .ShouldBeEmpty("Connected segments should not produce gap errors");
    }

    [Fact]
    public void Validate_ExcessiveSweepAngle_ReturnsWarning()
    {
        var path = new RoutedPath();
        // 150° sweep is over the 135° warning threshold
        path.Segments.Add(new BendSegment(0, 10, 10.0, 0, 150));

        var report = _diagnostics.Validate(path);

        report.Issues.ShouldContain(i =>
            i.Severity == RoutingIssueSeverity.Warning &&
            i.Message.Contains("sweep angle"));
    }

    [Fact]
    public void Validate_BlockedFallbackPath_ReturnsWarning()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        path.IsBlockedFallback = true;

        var report = _diagnostics.Validate(path);

        report.Issues.ShouldContain(i =>
            i.Severity == RoutingIssueSeverity.Warning &&
            i.Message.Contains("fallback"));
    }

    [Fact]
    public void Validate_InvalidGeometry_ReturnsError()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));
        path.IsInvalidGeometry = true;

        var report = _diagnostics.Validate(path);

        report.Issues.ShouldContain(i =>
            i.Severity == RoutingIssueSeverity.Error &&
            i.Message.Contains("invalid geometry"));
    }

    [Fact]
    public void FormatSummary_NoIssues_ReturnsOkMessage()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));

        var report = _diagnostics.Validate(path);

        report.FormatSummary().ShouldContain("OK");
    }

    [Fact]
    public void FormatSummary_WithIssues_IncludesSeverity()
    {
        var path = new RoutedPath();
        path.Segments.Add(new BendSegment(0, 3, 3.0, 0, 90));

        var report = _diagnostics.Validate(path);

        var summary = report.FormatSummary();
        summary.ShouldContain("Error");
    }

    [Fact]
    public void ValidateEndpoints_EndPointGap_ReportsWarning()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 95, 0, 0));

        // End pin at (100,0) but path ends at (95,0) → 5µm gap
        var report = _diagnostics.ValidateEndpoints(path, 0, 0, 100, 0, 0);

        report.Issues.ShouldContain(i =>
            i.Severity == RoutingIssueSeverity.Warning &&
            i.Message.Contains("End point gap"));
    }

    [Fact]
    public void ValidateEndpoints_CorrectEndpoints_NoGapWarnings()
    {
        var path = new RoutedPath();
        path.Segments.Add(new StraightSegment(0, 0, 100, 0, 0));

        var report = _diagnostics.ValidateEndpoints(path, 0, 0, 100, 0, 0);

        report.Issues
            .Where(i => i.Message.Contains("gap"))
            .ShouldBeEmpty();
    }
}

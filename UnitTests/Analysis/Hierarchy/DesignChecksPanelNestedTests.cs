using CAP.Avalonia.ViewModels.Diagnostics;
using CAP_Core.Analysis;
using CAP_Core.Components.Connections;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;
using static UnitTests.Analysis.Hierarchy.NestedDesignBuilder;
using Component = CAP_Core.Components.Core.Component;

namespace UnitTests.Analysis.Hierarchy;

/// <summary>
/// The Design Checks panel on nested groups: findings deep inside groups are listed with
/// their group path and "next issue" lands on them in absolute canvas coordinates;
/// unconnected pins are informational hints, hidden by default behind the
/// "Show unconnected pins" toggle and never counted as issues.
/// </summary>
public class DesignChecksPanelNestedTests
{
    /// <summary>OUTER ⊃ { A, INNER ⊃ { B, C } }; frozen B.out → C.in inside INNER.</summary>
    private static (ComponentGroup Outer, ComponentGroup Inner) Build()
    {
        var b = Leaf("B", 200, 0);
        var c = Leaf("C", 400, 0);
        var inner = Group("INNER", b, c);
        Frozen(inner, Pin(b, "out"), Pin(c, "in"), (300, 10), (400, 10));
        return (Group("OUTER", Leaf("A", 0, 0), inner), inner);
    }

    private static (DesignValidationViewModel Panel, List<(double X, double Y)> Visited) Run(ComponentGroup top)
    {
        var panel = new DesignValidationViewModel();
        var visited = new List<(double X, double Y)>();
        panel.NavigateToPosition = (x, y) => visited.Add((x, y));
        panel.RunValidation(Array.Empty<WaveguideConnection>(), new[] { top }, new Component[] { top });
        return (panel, visited);
    }

    [Fact]
    public void NestedBlockedFrozenPath_IsListedWithItsGroupPath_AndZoomLandsOnIt()
    {
        var (outer, inner) = Build();
        outer.MoveGroup(1000, 500);
        inner.InternalPaths[0].Path.IsBlockedFallback = true;

        var (panel, visited) = Run(outer);

        var blocked = panel.Issues.Single(i => i.Type == DesignIssueType.BlockedPath);
        blocked.Description.ShouldBe("Blocked path in group 'OUTER/INNER': B.out to C.in");
        panel.CurrentIndex.ShouldBe(0, "the blocked path is the only problem, so it is selected first");
        visited[^1].ShouldBe((1350.0, 510.0), "the moved nested frozen path's midpoint, absolute");
    }

    [Fact]
    public void NextIssue_WalksIntoNestedFindings_InAbsoluteCoordinates()
    {
        var (outer, inner) = Build();
        outer.MoveGroup(1000, 500);
        inner.InternalPaths[0].Path.IsInvalidGeometry = true;
        Frozen(inner, null, null, (1320, 400), (1320, 600));

        var (panel, visited) = Run(outer);
        panel.Issues.Count.ShouldBe(2, "invalid geometry + the vertical path crossing B.out → C.in");
        panel.NextIssueCommand.Execute(null);

        visited.ToArray().ShouldBe(new (double X, double Y)[] { (1320.0, 510.0), (1350.0, 510.0) },
            "overlap point first, then the flagged path's midpoint — both absolute");
    }

    [Fact]
    public void UnconnectedPins_AreHiddenHintsByDefault_AndNeverCountAsIssues()
    {
        var (outer, _) = Build();

        var (panel, visited) = Run(outer);

        panel.Issues.ShouldBeEmpty("A.in, A.out, B.in and C.out are only hints");
        panel.HasIssues.ShouldBeFalse();
        panel.HiddenHintCount.ShouldBe(4);
        panel.StatusText.ShouldStartWith("No issues found");
        panel.StatusText.ShouldContain("4");
        visited.ShouldBeEmpty();
    }

    [Fact]
    public void ShowUnconnectedPins_ListsHintsAfterProblems_WithoutCountingThemAsIssues()
    {
        var (outer, inner) = Build();
        inner.InternalPaths[0].Path.IsBlockedFallback = true;
        var (panel, _) = Run(outer);

        panel.ShowUnconnectedPins = true;

        panel.Issues.Count.ShouldBe(5);
        panel.Issues[0].Type.ShouldBe(DesignIssueType.BlockedPath, "problems come before hints");
        panel.Issues.Skip(1).ShouldAllBe(i => i.Severity == DesignIssueSeverity.Hint);
        panel.HiddenHintCount.ShouldBe(0);

        panel.ShowUnconnectedPins = false;
        panel.Issues.Count.ShouldBe(1);
        panel.HiddenHintCount.ShouldBe(4);
    }

    [Theory]
    [InlineData(DesignIssueType.UnconnectedPin, DesignIssueSeverity.Hint)]
    [InlineData(DesignIssueType.BlockedPath, DesignIssueSeverity.Problem)]
    [InlineData(DesignIssueType.ComponentFootprintOverlap, DesignIssueSeverity.Problem)]
    [InlineData(DesignIssueType.WaveguideThroughComponent, DesignIssueSeverity.Problem)]
    public void Severity_OnlyUnconnectedPinsAreHints(DesignIssueType type, DesignIssueSeverity expected)
    {
        DesignIssue.SeverityOf(type).ShouldBe(expected);
    }
}

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
/// Dangling-pin, PDK-compatibility and Design-Checks-panel behaviour on nested groups:
/// a nested pin is connected when a top-level wire or a frozen path at any depth ends on
/// it, a pin exposed up to the top-level group is the group's interface, and the panel
/// navigates to nested findings in absolute canvas coordinates.
/// </summary>
public class NestedGroupPinAndPdkCheckTests
{
    private readonly DesignValidator _validator = new();

    /// <summary>OUTER ⊃ { A, INNER ⊃ { B, C } }; frozen B.out → C.in inside INNER.</summary>
    private static (ComponentGroup Outer, ComponentGroup Inner, Component A, Component B, Component C) Build()
    {
        var b = Leaf("B", 200, 0);
        var c = Leaf("C", 400, 0);
        var inner = Group("INNER", b, c);
        Frozen(inner, Pin(b, "out"), Pin(c, "in"), (300, 10), (400, 10));
        var a = Leaf("A", 0, 0);
        return (Group("OUTER", a, inner), inner, a, b, c);
    }

    private List<string> DanglingPins(IEnumerable<WaveguideConnection> connections, params Component[] topLevel) =>
        _validator.ValidateUnconnectedPins(topLevel, connections)
            .Select(i => (string)i.LocalizationArgs![0]).ToList();

    [Fact]
    public void NestedPins_NeitherWiredNorExposed_AreReported_FrozenPathEndsAreConnected()
    {
        var (outer, _, _, _, _) = Build();

        DanglingPins(Array.Empty<WaveguideConnection>(), outer).ShouldBe(
            new[] { "OUTER/A.in", "OUTER/A.out", "OUTER/INNER/B.in", "OUTER/INNER/C.out" },
            ignoreOrder: true);
    }

    [Fact]
    public void PinExposedUpToTheTopLevelGroup_IsTheGroupInterface_AndNotReported()
    {
        var (outer, inner, a, b, _) = Build();
        var innerPin = Expose(inner, Pin(b, "in"), "bin");
        inner.SyncPhysicalPinsFromExternalPins();
        Expose(outer, inner.PhysicalPins.Single(p => p.Name == innerPin.Name), "top_bin");
        Expose(outer, Pin(a, "in"), "top_ain");
        outer.SyncPhysicalPinsFromExternalPins();

        var dangling = DanglingPins(Array.Empty<WaveguideConnection>(), outer);

        dangling.ShouldNotContain("OUTER/INNER/B.in", "exposed by INNER and, through INNER's pin, by OUTER");
        dangling.ShouldNotContain("OUTER/A.in");
        dangling.ShouldNotContain(p => p.StartsWith("OUTER.") || p.StartsWith("INNER."),
            "a group's synthesized pins mirror leaf pins and are never reported themselves");
    }

    [Fact]
    public void PinExposedOnlyByTheInnerGroup_AndNotWiredInsideTheOuter_IsReported()
    {
        var (outer, inner, _, b, _) = Build();
        Expose(inner, Pin(b, "in"), "bin");

        DanglingPins(Array.Empty<WaveguideConnection>(), outer).ShouldContain("OUTER/INNER/B.in");
    }

    [Fact]
    public void OuterFrozenPathWiringTheInnerGroupsExposedPin_ConnectsTheLeafPin()
    {
        var (outer, inner, a, b, _) = Build();
        Expose(inner, Pin(b, "in"), "bin");
        inner.SyncPhysicalPinsFromExternalPins();
        Frozen(outer, Pin(a, "out"), inner.PhysicalPins.Single(p => p.Name == "bin"), (100, 10), (200, 10));

        var dangling = DanglingPins(Array.Empty<WaveguideConnection>(), outer);

        dangling.ShouldNotContain("OUTER/INNER/B.in");
        dangling.ShouldNotContain("OUTER/A.out");
    }

    [Fact]
    public void TopLevelWireToANestedLeafPin_ConnectsIt()
    {
        var (outer, _, _, _, c) = Build();
        var loose = Leaf("L", 700, 0);
        var wire = Connection(Pin(c, "out"), Pin(loose, "in"), (500, 10), (700, 10));

        DanglingPins(new[] { wire }, outer, loose).ShouldNotContain("OUTER/INNER/C.out");
    }

    [Fact]
    public void NestedComponentFromADivergedPdk_IsReported()
    {
        var (outer, _, _, b, _) = Build();
        var sources = new Dictionary<Component, string?> { [b] = "ForeignPdk" };

        var issue = _validator.ValidateComponentPdkCompatibility(
            new[] { outer }, sources, Array.Empty<string>(), new[] { "ActivePdk" }).Single();

        issue.Type.ShouldBe(DesignIssueType.PdkProcessMismatch);
        issue.Description.ShouldContain("'OUTER/INNER/B'");
        issue.X.ShouldBe(250, 1e-9);
    }

    [Fact]
    public void DesignChecksPanel_NavigatesToNestedFindingsInAbsoluteCoordinates()
    {
        var (outer, inner, _, _, _) = Build();
        outer.MoveGroup(1000, 500);
        inner.InternalPaths[0].Path.IsInvalidGeometry = true;
        var panel = new DesignValidationViewModel();
        var visited = new List<(double X, double Y)>();
        panel.NavigateToPosition = (x, y) => visited.Add((x, y));

        panel.RunValidation(Array.Empty<WaveguideConnection>(), new[] { outer }, new Component[] { outer });

        var geometry = panel.Issues.Single(i => i.Type == DesignIssueType.InvalidGeometry);
        int index = panel.Issues.IndexOf(geometry);
        for (int i = 0; i < index; i++)
            panel.NextIssueCommand.Execute(null);

        visited[^1].ShouldBe((1350.0, 510.0), "the moved nested frozen path's midpoint, absolute");
        panel.Issues.ShouldContain(i => i.Type == DesignIssueType.UnconnectedPin && i.X == 1200 && i.Y == 510,
            "B.in moved with both groups");
    }
}

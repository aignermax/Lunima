using CAP_Core.Analysis;
using CAP_Core.Components.Core;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

public class AdderCachedRoutingTests
{
    [Theory]
    [InlineData("Logic Gate 4-Bit Adder.lun")]
    [InlineData("Logic Gate ALU + RAM.lun")]
    [InlineData("Logic Gate ALU + RAM + ACC.lun")]
    public async Task AdderExamples_OpenWithCleanCachedRoutes_WithoutRoutingSearch(string example)
    {
        var (canvas, fileOps, _) = await MziFringeAnalysis.LoadExample(example);
        await fileOps.PostLoadRouting;

        canvas.Connections.ShouldAllBe(c => c.Connection.RoutedPath != null
            && c.Connection.IsPathValid && !c.Connection.IsBlockedFallback,
            "the two carry-copy inputs must ship connected through real crossings");
        canvas.ConnectionManager.LastOrderingAttemptCount.ShouldBe(0,
            "opening a repaired example must use its cached geometry immediately");
        canvas.ConnectionManager.LastRoutingPassTimings.ShouldBeNull(
            "a clean cached example must not start even an incremental routing pass");
        new ComponentFootprintOverlapChecker().DetectOverlaps(canvas.Components.Select(c => c.Component))
            .ShouldBeEmpty("the shifted carry gates must remain clear of existing crossing bodies");
        // The composed designs also contain a frozen RAM block with separately tracked
        // internal routing findings. Validate the repaired adder and its crossings here.
        var adder = canvas.Components.Select(c => c.Component)
            .Where(c => c is not ComponentGroup group || group.GroupName.StartsWith("T", StringComparison.Ordinal)).ToList();
        new DesignValidator().Validate(canvas.Connections.Select(c => c.Connection),
                adder.OfType<ComponentGroup>(), adder, null, 1550)
            .Where(i => i.Type != DesignIssueType.UnconnectedPin).ShouldBeEmpty();

        var carryGates = canvas.Components.Select(c => c.Component).OfType<ComponentGroup>()
            .Where(g => g.GroupName is "T1H2N5C3" or "T1H2N5C5").ToList();
        carryGates.Count.ShouldBe(2);
        foreach (var gate in carryGates)
        foreach (var pin in gate.ExternalPins)
        {
            var (x, y) = pin.InternalPin.GetAbsolutePosition();
            x.ShouldBe(gate.PhysicalX + pin.RelativeX, 1e-6,
                "saved group origin and translated child geometry must stay in sync");
            y.ShouldBe(gate.PhysicalY + pin.RelativeY, 1e-6);
        }
        var output = carryGates.Single(g => g.GroupName == "T1H2N5C3")
            .ExternalPins.Single(p => p.Name == "Y").InternalPin;
        var crossing = canvas.Connections.Single(c => c.Connection.StartPin == output).Connection.EndPin.ParentComponent;
        (crossing.PhysicalX - output.ParentComponent.PhysicalX - output.ParentComponent.WidthMicrometers)
            .ShouldBeGreaterThanOrEqualTo(1 - 1e-6, "the output body must stay clear of its next crossing");
    }
}

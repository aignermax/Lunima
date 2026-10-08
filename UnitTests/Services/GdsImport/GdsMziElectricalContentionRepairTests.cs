using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Headless pin for the contention rip-up-and-reroute pass's payoff on the MZI
/// electrical fixture: the pass accepts exactly one repair there (the initial pass
/// connects the phase-shifter arm on its own since waveguides rasterize without gaps,
/// so one repair fewer is needed and one blocked wire fewer remains). The nazca-gated
/// <see cref="GdsMziElectricalRoundTripTests"/> assert the downstream geometry; this
/// test asserts the repair outcome itself, so a regression of the pass (e.g. the
/// #1297 budget hardening skipping one attempt too many) fails without nazca. Slow:
/// routing the design takes seconds — it shares the one routed build with the export
/// tests of the same (slow) test process.
/// </summary>
[Trait("Category", "Slow")]
public class GdsMziElectricalContentionRepairTests
{
    [Fact]
    public void MziElectricalFixture_ContentionRepair_AcceptsOneRepair()
    {
        var canvas = GdsMziElectricalFixture.SharedMziCanvas;

        canvas.ConnectionManager.LastContentionRepairAcceptCount.ShouldBe(1,
            "the contention repair pass re-routes one former fallback wire on his MZI");
        canvas.Connections.Count(c => c.Connection.IsBlockedFallback).ShouldBe(2,
            "two wires stay blocked (combiner → cross detector, one bond-pad trace) — one fewer than before");
    }
}

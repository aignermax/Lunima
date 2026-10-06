using Shouldly;
using Xunit;

namespace UnitTests.Services.GdsImport;

/// <summary>
/// Headless pin for the contention rip-up-and-reroute pass's payoff on the MZI
/// electrical fixture: the pass accepts exactly two repairs there. The nazca-gated
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
    public void MziElectricalFixture_ContentionRepair_AcceptsTwoRepairs()
    {
        var canvas = GdsMziElectricalFixture.SharedMziCanvas;

        canvas.ConnectionManager.LastContentionRepairAcceptCount.ShouldBe(2,
            "the contention repair pass re-routes two former fallback wires on his MZI");
    }
}

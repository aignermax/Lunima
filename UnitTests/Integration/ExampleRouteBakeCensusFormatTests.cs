using CAP_Core.Components.Connections;
using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Pins the format of the bake census line (issue #1249) and the per-pass wall-clock
/// breakdown line (issue #1287) so the routing-census table in the follow-up slice can
/// rely on a stable output shape.
/// </summary>
public class ExampleRouteBakeCensusFormatTests
{
    [Fact]
    public void FormatCensusLine_PrintsBlockedWithReasonBreakdown()
    {
        ExampleRouteBakeTests.FormatCensusLine("Logic Gate PC 2-bit.lun", blocked: 5, endpoint: 3, contention: 1, unclassified: 1)
            .ShouldBe("[bake] Logic Gate PC 2-bit.lun: blocked=5 (endpoint=3, contention=1, unclassified=1)");
    }

    [Fact]
    public void FormatCensusLine_ZeroBlocked_PrintsZeroBreakdown()
    {
        ExampleRouteBakeTests.FormatCensusLine("Mach-Zehnder Interferometer.lun", 0, 0, 0, 0)
            .ShouldBe("[bake] Mach-Zehnder Interferometer.lun: blocked=0 (endpoint=0, contention=0, unclassified=0)");
    }

    [Fact]
    public void FormatPassTimingsLine_PrintsEveryPassWithDurationsAndCounts()
    {
        var timings = new RoutingPassTimings
        {
            Total = TimeSpan.FromSeconds(420.5),
            InitialPass = TimeSpan.FromSeconds(12.34),
            OrderingCascade = TimeSpan.FromSeconds(300.0),
            OrderingAttempts = 6,
            CrossingDissolution = TimeSpan.FromSeconds(0.04),
            CrossingInsertion = TimeSpan.FromSeconds(5.06),
            PinLeadCollapse = TimeSpan.FromSeconds(1.0),
            BendUpsizing = TimeSpan.FromSeconds(2.0),
            CrossingScan = TimeSpan.FromSeconds(0.5),
            ContentionRepair = TimeSpan.FromSeconds(99.95),
            ContentionRepairAttempts = 8,
            ContentionRepairAccepts = 0,
        };

        ExampleRouteBakeTests.FormatPassTimingsLine("Logic Gate Register 2-bit.lun", timings)
            .ShouldBe("[bake] Logic Gate Register 2-bit.lun: passes total=420.5s ("
                + "initial=12.3s, ordering-cascade=300.0s/6 attempts, crossing-dissolve=0.0s, "
                + "crossing-insert=5.1s, pin-lead-collapse=1.0s, bend-upsize=2.0s, "
                + "crossing-scan=0.5s, contention-repair=100.0s/8 attempts/0 accepts)");
    }

    [Fact]
    public void FormatPassTimingsLine_CancelledPass_IsMarkedCancelled()
    {
        var timings = new RoutingPassTimings
        {
            Total = TimeSpan.FromSeconds(3.0),
            InitialPass = TimeSpan.FromSeconds(3.0),
            OrderingAttempts = 0,
            WasCancelled = true,
        };

        ExampleRouteBakeTests.FormatPassTimingsLine("Logic Gate ALU 1-bit.lun", timings)
            .ShouldBe("[bake] Logic Gate ALU 1-bit.lun: passes total=3.0s ("
                + "initial=3.0s, ordering-cascade=0.0s/0 attempts, crossing-dissolve=0.0s, "
                + "crossing-insert=0.0s, pin-lead-collapse=0.0s, bend-upsize=0.0s, "
                + "crossing-scan=0.0s, contention-repair=0.0s/0 attempts/0 accepts) [cancelled]");
    }
}

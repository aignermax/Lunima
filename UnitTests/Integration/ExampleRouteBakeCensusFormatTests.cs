using Shouldly;
using Xunit;

namespace UnitTests.Integration;

/// <summary>
/// Pins the format of the bake census line (issue #1249) so the routing-census table
/// in the follow-up slice can rely on a stable output shape.
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
}

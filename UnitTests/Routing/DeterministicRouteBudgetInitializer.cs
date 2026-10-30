using System.Runtime.CompilerServices;
using CAP_Core.Routing;

namespace UnitTests.Routing;

/// <summary>
/// Turns the router's wall-clock budget off for the whole test assembly. Routing tests assert
/// exact outcomes (blocked wires, ordering attempts), and a budget that runs out on a loaded CI
/// runner changes those outcomes; tests of the budget itself set <see cref="WaveguideRouter.MaxRouteDuration"/>
/// explicitly.
/// </summary>
internal static class DeterministicRouteBudgetInitializer
{
    [ModuleInitializer]
    internal static void DisableWallClockRouteBudget() =>
        WaveguideRouter.MaxRouteDurationForNewRouters = Timeout.InfiniteTimeSpan;
}

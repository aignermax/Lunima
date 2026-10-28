using CAP_Core.Export.Contracts;
using CAP_Core.Export.OpenEbl;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Architecture;

public sealed class OpenEblContractContainerTests
{
    [Fact]
    public async Task CheckerContract_UsesTheProductionSingleton()
    {
        var preferences = Path.Combine(Path.GetTempPath(), $"lunima-openebl-contract-{Guid.NewGuid():N}.json");
        await using var services = ProductionContainerTestHelper.BuildWithTempPreferences(preferences);
        services.GetRequiredService<IOpenEblSubmissionChecker>()
            .ShouldBeSameAs(services.GetRequiredService<OpenEblSubmissionChecker>());
    }
}

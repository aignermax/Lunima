using System.Collections.Generic;
using CAP.Avalonia.Services;
using CAP_DataAccess.Persistence.PIR;
using Shouldly;
using Xunit;

namespace UnitTests.Import;

public class SMatrixOverrideGcTests
{
    private static ComponentSMatrixData Data() => new() { SourceNote = "x" };

    [Fact]
    public void Sweep_KeepsUsedKeys_DropsOrphans()
    {
        var store = new Dictionary<string, ComponentSMatrixData>
        {
            ["geo:v1-used"]       = Data(),
            ["geo:v1-orphan"]     = Data(),
            ["legacy_identifier"] = Data(),
        };
        var usedGeometryKeys = new HashSet<string> { "geo:v1-used" };
        var liveIdentifiers  = new HashSet<string> { "legacy_identifier" };

        var kept = SMatrixOverrideGc.Sweep(store, usedGeometryKeys, liveIdentifiers);

        kept.Keys.ShouldBe(new[] { "geo:v1-used", "legacy_identifier" }, ignoreOrder: true);
    }

    [Fact]
    public void Sweep_KeepsTemplateScopedKeys()
    {
        var store = new Dictionary<string, ComponentSMatrixData> { ["Demo PDK::Coupler"] = Data() };
        var kept = SMatrixOverrideGc.Sweep(store, new HashSet<string>(), new HashSet<string>());
        kept.ShouldContainKey("Demo PDK::Coupler");
    }
}

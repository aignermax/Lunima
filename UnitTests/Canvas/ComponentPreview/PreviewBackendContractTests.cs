using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using CAP_Core.Export.Contracts;
using Shouldly;

namespace UnitTests.Canvas.ComponentPreview;

public class PreviewBackendContractTests
{
    [Fact]
    public void BackendAndCanvasCapabilities_UseOneBudgetAndRejectObsoletePublication()
    {
        var owner = new PreviewGeometryCache();
        IPdkPreviewBackend nazca = new NazcaComponentPreviewService("python", "nazca.py") { GeometryCache = owner };
        IPdkPreviewBackend gdsFactory = new GdsFactoryComponentPreviewService("python", "gdsfactory.py") { GeometryCache = owner };
        var canvas = new GdsGeometryCache(nazca);
        var geometry = new NazcaPreviewResult { Success = true, XMax = 12 };

        nazca.Set("nazca", geometry);
        canvas.TryGet("nazca", out var cached).ShouldBeTrue();
        cached.ShouldBeSameAs(geometry);
        for (var i = 0; i < PreviewGeometryCache.MaxEntries; i++)
            gdsFactory.Set("gdsfactory-" + i, geometry);

        canvas.Count.ShouldBe(PreviewGeometryCache.MaxEntries);
        canvas.TryGet("nazca", out _).ShouldBeFalse("all backends consume the same bounded budget");
        var capturedGeneration = nazca.Generation;
        gdsFactory.InvalidateCache();
        canvas.Count.ShouldBe(0);
        nazca.Generation.ShouldBeGreaterThan(capturedGeneration);

        canvas.Set("obsolete", geometry, capturedGeneration);
        nazca.TryGet("obsolete", out _).ShouldBeFalse();
        canvas.Set("current", geometry, nazca.Generation);
        gdsFactory.TryGet("current", out var current).ShouldBeTrue();
        current.ShouldBeSameAs(geometry);
    }

    [Fact]
    public void PureIdentityDigest_PreservesPersistedCacheKeyFormat()
    {
        PreviewIdentityHash.Compute("abc").ShouldBe(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD");
        PreviewEnvironmentIdentity.Hash("interpreter|renderer|revision")
            .ShouldBe(PreviewIdentityHash.Compute("interpreter|renderer|revision"));
    }
}

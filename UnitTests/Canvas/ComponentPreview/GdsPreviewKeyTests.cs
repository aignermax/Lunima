// UnitTests/Canvas/ComponentPreview/GdsPreviewKeyTests.cs
using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

public class GdsPreviewKeyTests
{
    [Fact]
    public void Hash_IsStableForSameInputs()
    {
        var a = new GdsPreviewKey("siepic", "ebeam_dc", "Lc=5");
        var b = new GdsPreviewKey("siepic", "ebeam_dc", "Lc=5");
        a.Hash().ShouldBe(b.Hash());
    }

    [Fact]
    public void Hash_DiffersWhenFunctionOrParamsDiffer()
    {
        var baseKey = new GdsPreviewKey("siepic", "ebeam_dc", "Lc=5");
        baseKey.Hash().ShouldNotBe(new GdsPreviewKey("siepic", "ebeam_dc", "Lc=9").Hash());
        baseKey.Hash().ShouldNotBe(new GdsPreviewKey("siepic", "ebeam_gc", "Lc=5").Hash());
    }

    [Fact]
    public void Hash_IncludesFormatVersion_SoBumpInvalidates()
    {
        var key = new GdsPreviewKey("m", "f", null);
        key.Hash().ShouldStartWith($"v{GdsPreviewKey.FormatVersion}-");
    }

    [Fact]
    public void IsRenderable_FalseWhenFunctionMissing()
    {
        new GdsPreviewKey("m", "", "p").IsRenderable.ShouldBeFalse();
        new GdsPreviewKey("m", "   ", "p").IsRenderable.ShouldBeFalse();
        new GdsPreviewKey("m", "f", "p").IsRenderable.ShouldBeTrue();
    }
}

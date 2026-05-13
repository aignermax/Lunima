using CAP.Avalonia.Controls.Canvas.ComponentPreview;
using CAP_Core.Export;
using Shouldly;
using Xunit;

namespace UnitTests.Canvas.ComponentPreview;

/// <summary>Unit tests for <see cref="GdsPreviewCache"/>.</summary>
public sealed class GdsPreviewCacheTests
{
    private static GdsPreviewData MakeData() =>
        new(new NazcaPreviewResult { Success = true }, 10, 10);

    [Fact]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var cache = new GdsPreviewCache();
        cache.TryGet("missing", out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void Set_ThenTryGet_ReturnsStoredValue()
    {
        var cache = new GdsPreviewCache();
        var data = MakeData();

        cache.Set("key1", data);
        cache.TryGet("key1", out var result).ShouldBeTrue();
        result.ShouldBeSameAs(data);
    }

    [Fact]
    public void Set_NullValue_StoresNullAndReturnsTrueOnGet()
    {
        var cache = new GdsPreviewCache();
        cache.Set("key1", null);
        cache.TryGet("key1", out var result).ShouldBeTrue();
        result.ShouldBeNull();
    }

    [Fact]
    public void Count_ReflectsEntries()
    {
        var cache = new GdsPreviewCache();
        cache.Count.ShouldBe(0);
        cache.Set("k1", MakeData());
        cache.Count.ShouldBe(1);
        cache.Set("k2", null);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void LruEviction_WhenMaxEntriesExceeded_EvictsLeastRecentEntry()
    {
        var cache = new GdsPreviewCache();

        // Fill the cache to capacity
        for (int i = 0; i < GdsPreviewCache.MaxEntries; i++)
            cache.Set($"key{i}", MakeData());

        cache.Count.ShouldBe(GdsPreviewCache.MaxEntries);

        // Adding one more entry should evict the oldest ("key0")
        cache.Set("keyNew", MakeData());

        cache.Count.ShouldBe(GdsPreviewCache.MaxEntries);
        cache.TryGet("key0", out _).ShouldBeFalse();
        cache.TryGet("keyNew", out _).ShouldBeTrue();
    }

    [Fact]
    public void LruPromotion_AccessedEntryIsNotEvicted()
    {
        var cache = new GdsPreviewCache();

        for (int i = 0; i < GdsPreviewCache.MaxEntries; i++)
            cache.Set($"key{i}", MakeData());

        // Promote key0 to most-recently-used
        cache.TryGet("key0", out _);

        // Adding one new entry should evict key1 (now the LRU), not key0
        cache.Set("keyNew", MakeData());

        cache.TryGet("key0", out _).ShouldBeTrue();
        cache.TryGet("key1", out _).ShouldBeFalse();
    }

    [Fact]
    public void Set_DuplicateKey_UpdatesValueWithoutGrowingCache()
    {
        var cache = new GdsPreviewCache();
        var data1 = MakeData();
        var data2 = MakeData();

        cache.Set("key1", data1);
        cache.Set("key1", data2);

        cache.Count.ShouldBe(1);
        cache.TryGet("key1", out var result).ShouldBeTrue();
        result.ShouldBeSameAs(data2);
    }
}

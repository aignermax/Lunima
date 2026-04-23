using CAP_Core;
using CAP_Core.Grid;
using Shouldly;
using Xunit;

namespace UnitTests.Grid;

/// <summary>
/// Unit tests for <see cref="ChipSizeConfiguration"/> — covers size → tile conversion,
/// preset lookup, and the factory methods.
/// </summary>
public class ChipSizeConfigurationTests
{
    // ── Constructor validation ────────────────────────────────────────────

    [Fact]
    public void Constructor_WithValidDimensions_StoresMicrometers()
    {
        var config = new ChipSizeConfiguration(5000.0, 3000.0);

        config.WidthMicrometers.ShouldBe(5000.0);
        config.HeightMicrometers.ShouldBe(3000.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Constructor_WithNonPositiveWidth_Throws(double width)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ChipSizeConfiguration(width, 1000.0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveHeight_Throws(double height)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ChipSizeConfiguration(1000.0, height));
    }

    // ── Tile-count conversion ─────────────────────────────────────────────

    [Fact]
    public void TileColumns_ReturnsWidthDividedByGridPitch()
    {
        // 5 mm / 250 μm = 20 tiles
        var config = new ChipSizeConfiguration(5000.0, 1000.0);

        config.TileColumns.ShouldBe(20);
    }

    [Fact]
    public void TileRows_ReturnsHeightDividedByGridPitch()
    {
        // 30 mm / 250 μm = 120 tiles
        var config = new ChipSizeConfiguration(1000.0, 30_000.0);

        config.TileRows.ShouldBe(120);
    }

    [Fact]
    public void TileColumns_ForPartialTile_TruncatesDown()
    {
        // 600 μm / 250 μm = 2.4 → truncated to 2
        var config = new ChipSizeConfiguration(600.0, 250.0);

        config.TileColumns.ShouldBe(2);
    }

    [Fact]
    public void TileColumns_UsesPhotonicConstantsGridSizeMicrometers()
    {
        // Verify that the tile count matches the shared constant
        var config = new ChipSizeConfiguration(
            PhotonicConstants.GridSizeMicrometers * 8,
            PhotonicConstants.GridSizeMicrometers);

        config.TileColumns.ShouldBe(8);
    }

    // ── FromMillimeters factory ───────────────────────────────────────────

    [Fact]
    public void FromMillimeters_ConvertsToMicrometers()
    {
        var config = ChipSizeConfiguration.FromMillimeters(2.0, 3.0);

        config.WidthMicrometers.ShouldBe(2000.0);
        config.HeightMicrometers.ShouldBe(3000.0);
    }

    [Fact]
    public void FromMillimeters_ProducesCorrectTileCounts()
    {
        // 10 mm / 250 μm = 40 tiles
        var config = ChipSizeConfiguration.FromMillimeters(10.0, 10.0);

        config.TileColumns.ShouldBe(40);
        config.TileRows.ShouldBe(40);
    }

    // ── Presets ───────────────────────────────────────────────────────────

    [Fact]
    public void Presets_ContainsAtLeastFourNamedEntries()
    {
        ChipSizeConfiguration.Presets.Count.ShouldBeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Presets_LastEntryIsCustomSentinel()
    {
        var last = ChipSizeConfiguration.Presets[^1];

        last.IsCustom.ShouldBeTrue();
        last.Name.ShouldBe("Custom");
    }

    [Fact]
    public void Presets_NamedEntriesHavePositiveDimensions()
    {
        foreach (var preset in ChipSizeConfiguration.Presets.Where(p => !p.IsCustom))
        {
            preset.WidthMm.ShouldBeGreaterThan(0, $"Preset '{preset.Name}' has non-positive width");
            preset.HeightMm.ShouldBeGreaterThan(0, $"Preset '{preset.Name}' has non-positive height");
        }
    }

    [Fact]
    public void ChipSizePreset_IsCustom_ReturnsFalseForNamedPresets()
    {
        var preset = new ChipSizePreset("5 × 5 mm", 5.0, 5.0);

        preset.IsCustom.ShouldBeFalse();
    }

    [Fact]
    public void ChipSizePreset_IsCustom_ReturnsTrueForZeroDimensions()
    {
        var sentinel = new ChipSizePreset("Custom", 0.0, 0.0);

        sentinel.IsCustom.ShouldBeTrue();
    }
}

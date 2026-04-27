using System.Text.Json;
using CAP.Avalonia.ViewModels;
using Shouldly;

namespace UnitTests.Persistence;

/// <summary>
/// Verifies the .lun chip-size round-trip contract added in PR #504:
/// <list type="bullet">
/// <item>Chip width/height survive a save → reload cycle.</item>
/// <item>Files written before this PR (no chip-size fields) deserialize without error.</item>
/// <item>JSON property names are stable so future schema changes don't silently rename them.</item>
/// </list>
/// </summary>
public class ChipSizePersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void Roundtrip_WithChipSize_PreservesWidthAndHeight()
    {
        var original = new DesignFileData
        {
            FormatVersion = "2.0",
            ChipWidthMicrometers = 20_000.0,
            ChipHeightMicrometers = 30_000.0,
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var roundtripped = JsonSerializer.Deserialize<DesignFileData>(json);

        roundtripped.ShouldNotBeNull();
        roundtripped!.ChipWidthMicrometers.ShouldBe(20_000.0);
        roundtripped.ChipHeightMicrometers.ShouldBe(30_000.0);
    }

    [Fact]
    public void Roundtrip_LegacyFileWithoutChipSizeFields_DeserializesWithNulls()
    {
        // Pre-PR-#504 .lun files do not contain ChipWidthMicrometers / ChipHeightMicrometers.
        // Backward compatibility requires that they deserialize cleanly with both fields null,
        // so the load path can fall back to the canvas default.
        const string legacyJson = """
            {
              "FormatVersion": "2.0",
              "Components": [],
              "Connections": []
            }
            """;

        var data = JsonSerializer.Deserialize<DesignFileData>(legacyJson);

        data.ShouldNotBeNull();
        data!.ChipWidthMicrometers.ShouldBeNull();
        data.ChipHeightMicrometers.ShouldBeNull();
    }

    [Fact]
    public void Roundtrip_WithChipSize_UsesStableJsonPropertyNames()
    {
        var data = new DesignFileData
        {
            FormatVersion = "2.0",
            ChipWidthMicrometers = 5000.0,
            ChipHeightMicrometers = 5000.0,
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);

        // Pin the on-disk property names — renaming them would break every existing user file.
        json.ShouldContain("\"ChipWidthMicrometers\":5000");
        json.ShouldContain("\"ChipHeightMicrometers\":5000");
    }

    [Fact]
    public void Roundtrip_WithNullChipSize_OmitsFieldsFromJson()
    {
        // The Save() path uses WhenWritingNull, so a null chip size must not pollute the file
        // with explicit nulls — keeps the on-disk format clean for legacy round-trips.
        var data = new DesignFileData
        {
            FormatVersion = "2.0",
            ChipWidthMicrometers = null,
            ChipHeightMicrometers = null,
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);

        json.ShouldNotContain("ChipWidthMicrometers");
        json.ShouldNotContain("ChipHeightMicrometers");
    }
}

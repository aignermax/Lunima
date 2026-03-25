using CAP_Core.Components.Creation;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Tests for ComponentNameGenerator which generates readable, incremental names for copied components.
/// </summary>
public class ComponentNameGeneratorTests
{
    /// <summary>
    /// Verifies that a simple component name gets an incremental suffix.
    /// </summary>
    [Fact]
    public void GenerateCopyName_SimpleComponent_AddsIncrementalSuffix()
    {
        // Arrange
        var originalName = "MMI_1x2";
        var existingNames = new[] { "MMI_1x2" };

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("MMI_1x2_1");
    }

    /// <summary>
    /// Verifies that when no components exist, the first copy gets suffix _1.
    /// </summary>
    [Fact]
    public void GenerateCopyName_NoExistingComponents_ReturnsSuffixOne()
    {
        // Arrange
        var originalName = "Detector";
        var existingNames = Array.Empty<string>();

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("Detector_1");
    }

    /// <summary>
    /// Verifies that copying a component with existing copies increments the number.
    /// </summary>
    [Fact]
    public void GenerateCopyName_ExistingCopies_IncrementsCorrectly()
    {
        // Arrange
        var originalName = "Coupler";
        var existingNames = new[] { "Coupler", "Coupler_1", "Coupler_2" };

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("Coupler_3");
    }

    /// <summary>
    /// Verifies that components with GUID suffixes are cleaned up.
    /// </summary>
    [Fact]
    public void GenerateCopyName_ComponentWithGuidSuffix_RemovesGuid()
    {
        // Arrange
        var originalName = "MMI_1x2_a3f5b2c8";
        var existingNames = new[] { "MMI_1x2" };

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("MMI_1x2_1");
        result.ShouldNotContain("a3f5b2c8");
    }

    /// <summary>
    /// Verifies that full GUIDs (32 chars) are removed.
    /// </summary>
    [Fact]
    public void GenerateCopyName_ComponentWithFullGuid_RemovesGuid()
    {
        // Arrange
        var originalName = "Component_a3f5b2c89d4e4f3ab1c28e7d6f5a4b3c";
        var existingNames = Array.Empty<string>();

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("Component_1");
    }

    /// <summary>
    /// Verifies that GUIDs with hyphens are handled correctly.
    /// </summary>
    [Fact]
    public void GenerateCopyName_ComponentWithHyphenatedGuid_RemovesGuid()
    {
        // Arrange
        var originalName = "MMI_a3f5b2c8-9d4e-4f3a-b1c2-8e7d6f5a4b3c";
        var existingNames = Array.Empty<string>();

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("MMI_1");
    }

    /// <summary>
    /// Verifies that copying a component that already has a numeric suffix strips it.
    /// </summary>
    [Fact]
    public void GenerateCopyName_ComponentWithNumericSuffix_StripsAndReassigns()
    {
        // Arrange
        var originalName = "Waveguide_5";
        var existingNames = new[] { "Waveguide", "Waveguide_1", "Waveguide_2" };

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("Waveguide_3");
    }

    /// <summary>
    /// Verifies that gaps in numbering are filled correctly.
    /// </summary>
    [Fact]
    public void GenerateCopyName_WithGapsInNumbering_FindsNextNumber()
    {
        // Arrange
        var originalName = "Filter";
        var existingNames = new[] { "Filter", "Filter_1", "Filter_5", "Filter_10" };

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("Filter_11"); // Should use max + 1, not fill gaps
    }

    /// <summary>
    /// Verifies that null or empty names get a default name.
    /// </summary>
    [Fact]
    public void GenerateCopyName_NullOrEmptyName_ReturnsDefault()
    {
        // Arrange
        var existingNames = Array.Empty<string>();

        // Act
        var result1 = ComponentNameGenerator.GenerateCopyName(null!, existingNames);
        var result2 = ComponentNameGenerator.GenerateCopyName("", existingNames);
        var result3 = ComponentNameGenerator.GenerateCopyName("  ", existingNames);

        // Assert
        result1.ShouldBe("component_1");
        result2.ShouldBe("component_1");
        result3.ShouldBe("component_1");
    }

    /// <summary>
    /// Verifies that multiple sequential pastes get incremental names.
    /// </summary>
    [Fact]
    public void GenerateCopyName_MultipleSequentialPastes_IncrementsCorrectly()
    {
        // Arrange
        var originalName = "Splitter";
        var existingNames = new List<string> { "Splitter" };

        // Act & Assert
        var copy1 = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);
        copy1.ShouldBe("Splitter_1");
        existingNames.Add(copy1);

        var copy2 = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);
        copy2.ShouldBe("Splitter_2");
        existingNames.Add(copy2);

        var copy3 = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);
        copy3.ShouldBe("Splitter_3");
    }

    /// <summary>
    /// Verifies that case-insensitive matching works correctly.
    /// </summary>
    [Fact]
    public void GenerateCopyName_CaseInsensitive_HandlesCorrectly()
    {
        // Arrange
        var originalName = "detector";
        var existingNames = new[] { "Detector", "DETECTOR_1" };

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        // Should find "DETECTOR_1" case-insensitively and generate "detector_2"
        // But base name extraction is case-preserving from the input
        result.ShouldBe("detector_2");

        // Also verify that if we have matching base names, it increments
        var existingNames2 = new[] { "detector", "detector_1" };
        var result2 = ComponentNameGenerator.GenerateCopyName(originalName, existingNames2);
        result2.ShouldBe("detector_2");
    }

    // =========================================================================
    // Group Name Tests
    // =========================================================================

    /// <summary>
    /// Verifies that group names follow the "group_{name}_{number}" pattern.
    /// </summary>
    [Fact]
    public void GenerateGroupName_SimpleGroup_ReturnsCorrectPattern()
    {
        // Arrange
        var groupName = "MyCircuit";
        var existingNames = Array.Empty<string>();

        // Act
        var result = ComponentNameGenerator.GenerateGroupName(groupName, existingNames);

        // Assert
        result.ShouldBe("group_MyCircuit_1");
    }

    /// <summary>
    /// Verifies that existing group copies are handled correctly.
    /// </summary>
    [Fact]
    public void GenerateGroupName_ExistingGroups_IncrementsCorrectly()
    {
        // Arrange
        var groupName = "Filter";
        var existingNames = new[] { "group_Filter_1", "group_Filter_2" };

        // Act
        var result = ComponentNameGenerator.GenerateGroupName(groupName, existingNames);

        // Assert
        result.ShouldBe("group_Filter_3");
    }

    /// <summary>
    /// Verifies that empty group names get a default.
    /// </summary>
    [Fact]
    public void GenerateGroupName_EmptyName_ReturnsDefault()
    {
        // Arrange
        var existingNames = Array.Empty<string>();

        // Act
        var result = ComponentNameGenerator.GenerateGroupName("", existingNames);

        // Assert
        result.ShouldBe("group_1");
    }

    /// <summary>
    /// Verifies that special characters in component names are handled.
    /// </summary>
    [Fact]
    public void GenerateCopyName_SpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var originalName = "MMI-1x2";
        var existingNames = new[] { "MMI-1x2" };

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("MMI-1x2_1");
    }

    /// <summary>
    /// Verifies that component names with numbers in the base name are preserved.
    /// </summary>
    [Fact]
    public void GenerateCopyName_NumbersInBaseName_PreservesCorrectly()
    {
        // Arrange
        var originalName = "MMI_2x4";
        var existingNames = new[] { "MMI_2x4" };

        // Act
        var result = ComponentNameGenerator.GenerateCopyName(originalName, existingNames);

        // Assert
        result.ShouldBe("MMI_2x4_1");
    }
}

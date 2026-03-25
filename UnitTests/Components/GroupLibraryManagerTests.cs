using CAP_Core.Components.Core;
using CAP_Core.Components.Creation;
using CAP_Core.Routing;
using Shouldly;
using Xunit;

namespace UnitTests.Components;

/// <summary>
/// Unit tests for GroupLibraryManager.
/// Tests template saving, loading, removal, and instantiation.
/// </summary>
public class GroupLibraryManagerTests : IDisposable
{
    private readonly string _testLibraryPath;
    private readonly GroupLibraryManager _manager;

    public GroupLibraryManagerTests()
    {
        // Create a temporary directory for testing
        _testLibraryPath = Path.Combine(Path.GetTempPath(), $"GroupLibraryTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testLibraryPath);
        _manager = new GroupLibraryManager(_testLibraryPath);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testLibraryPath))
        {
            Directory.Delete(_testLibraryPath, true);
        }
    }

    [Fact]
    public void SaveTemplate_CreatesFileAndTemplate()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);

        // Act
        var template = _manager.SaveTemplate(group, "My Test Group", "A test group");

        // Assert
        template.ShouldNotBeNull();
        template.Name.ShouldBe("My Test Group");
        template.Description.ShouldBe("A test group");
        template.ComponentCount.ShouldBe(2);
        template.Source.ShouldBe("User");
        template.FilePath.ShouldNotBeNull();
        File.Exists(template.FilePath!).ShouldBeTrue();
    }

    [Fact]
    public void LoadTemplates_LoadsSavedTemplates()
    {
        // Arrange
        var group1 = CreateTestGroup("Group1", 2);
        var group2 = CreateTestGroup("Group2", 3);
        _manager.SaveTemplate(group1, "Group 1", "First group");
        _manager.SaveTemplate(group2, "Group 2", "Second group");

        // Act
        var newManager = new GroupLibraryManager(_testLibraryPath);
        newManager.LoadTemplates();

        // Assert
        newManager.Templates.Count.ShouldBe(2);
        newManager.UserTemplates.Count().ShouldBe(2);
        newManager.PdkTemplates.Count().ShouldBe(0);
    }

    [Fact]
    public void RemoveTemplate_DeletesFileAndTemplate()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        var template = _manager.SaveTemplate(group, "Test Group");

        // Act
        var removed = _manager.RemoveTemplate(template);

        // Assert
        removed.ShouldBeTrue();
        _manager.Templates.ShouldNotContain(template);
        if (template.FilePath != null)
        {
            File.Exists(template.FilePath).ShouldBeFalse();
        }
    }

    [Fact]
    public void InstantiateTemplate_CreatesDeepCopyWithNewIds()
    {
        // Arrange
        var originalGroup = CreateTestGroup("Original", 2);
        var template = _manager.SaveTemplate(originalGroup, "Template Group");

        // Make sure the template has the group loaded
        template.TemplateGroup = originalGroup;

        // Act
        var instance = _manager.InstantiateTemplate(template, 100, 200);

        // Assert
        instance.ShouldNotBeNull();
        instance.PhysicalX.ShouldBe(100);
        instance.PhysicalY.ShouldBe(200);
        instance.ChildComponents.Count.ShouldBe(2);

        // Verify new unique IDs
        instance.Identifier.ShouldNotBe(originalGroup.Identifier);
        instance.ChildComponents[0].Identifier.ShouldNotBe(originalGroup.ChildComponents[0].Identifier);
        instance.ChildComponents[1].Identifier.ShouldNotBe(originalGroup.ChildComponents[1].Identifier);
    }

    [Fact]
    public void SaveTemplate_WithEmptyName_ThrowsException()
    {
        // Arrange
        var group = CreateTestGroup("Test", 1);

        // Act & Assert
        Should.Throw<ArgumentException>(() => _manager.SaveTemplate(group, ""));
    }

    [Fact]
    public void SaveTemplate_PdkSource_SavesToPdkFolder()
    {
        // Arrange
        var group = CreateTestGroup("PdkGroup", 1);

        // Act
        var template = _manager.SaveTemplate(group, "PDK Macro", "A PDK macro", "PDK");

        // Assert
        template.Source.ShouldBe("PDK");
        template.Category.ShouldBe("PDK Macros");
        template.FilePath.ShouldContain("PdkGroups");
    }

    [Fact]
    public void LoadTemplates_SeparatesUserAndPdkTemplates()
    {
        // Arrange
        var userGroup = CreateTestGroup("UserGroup", 1);
        var pdkGroup = CreateTestGroup("PdkGroup", 1);
        _manager.SaveTemplate(userGroup, "User Group", null, "User");
        _manager.SaveTemplate(pdkGroup, "PDK Group", null, "PDK");

        // Act
        var newManager = new GroupLibraryManager(_testLibraryPath);
        newManager.LoadTemplates();

        // Assert
        newManager.UserTemplates.Count().ShouldBe(1);
        newManager.PdkTemplates.Count().ShouldBe(1);
        newManager.UserTemplates.First().Name.ShouldBe("User Group");
        newManager.PdkTemplates.First().Name.ShouldBe("PDK Group");
    }

    [Fact]
    public void SaveTemplate_MarksGroupAsPrefab()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);
        group.IsPrefab.ShouldBeFalse(); // Initially not a prefab

        // Act
        var template = _manager.SaveTemplate(group, "My Prefab Group");

        // Assert
        group.IsPrefab.ShouldBeTrue(); // Should be marked as prefab after saving
    }

    [Fact]
    public void ComponentGroup_IsPrefabDefaultsFalse()
    {
        // Arrange & Act
        var group = CreateTestGroup("TestGroup", 2);

        // Assert
        group.IsPrefab.ShouldBeFalse();
    }

    [Fact]
    public void ComponentGroup_CanSetIsPrefab()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 2);

        // Act
        group.IsPrefab = true;

        // Assert
        group.IsPrefab.ShouldBeTrue();
    }

    [Fact]
    public void RemoveTemplate_WithNonexistentTemplate_ReturnsFalse()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 1);
        var template = new GroupTemplate
        {
            Name = "Nonexistent",
            Source = "User"
        };

        // Act
        var removed = _manager.RemoveTemplate(template);

        // Assert
        removed.ShouldBeFalse();
    }

    [Fact]
    public void RemoveTemplate_WithNullFilePath_SuccessfullyRemovesTemplate()
    {
        // Arrange
        var group = CreateTestGroup("TestGroup", 1);
        var template = _manager.SaveTemplate(group, "Test Template");
        template.FilePath = null; // Simulate template without file

        // Act
        var removed = _manager.RemoveTemplate(template);

        // Assert
        removed.ShouldBeTrue();
        _manager.Templates.ShouldNotContain(template);
    }

    [Fact]
    public void RemoveTemplate_Multiple_DeletesAllSpecifiedTemplates()
    {
        // Arrange
        var group1 = CreateTestGroup("Group1", 1);
        var group2 = CreateTestGroup("Group2", 2);
        var group3 = CreateTestGroup("Group3", 3);

        var template1 = _manager.SaveTemplate(group1, "Template 1");
        var template2 = _manager.SaveTemplate(group2, "Template 2");
        var template3 = _manager.SaveTemplate(group3, "Template 3");

        _manager.Templates.Count.ShouldBe(3);

        // Act - Remove template 2
        var removed = _manager.RemoveTemplate(template2);

        // Assert
        removed.ShouldBeTrue();
        _manager.Templates.Count.ShouldBe(2);
        _manager.Templates.ShouldContain(template1);
        _manager.Templates.ShouldNotContain(template2);
        _manager.Templates.ShouldContain(template3);

        // Verify file was deleted
        if (template2.FilePath != null)
        {
            File.Exists(template2.FilePath).ShouldBeFalse();
        }
    }

    /// <summary>
    /// Creates a test ComponentGroup with the specified number of child components.
    /// </summary>
    private ComponentGroup CreateTestGroup(string name, int childCount)
    {
        var group = new ComponentGroup(name)
        {
            PhysicalX = 0,
            PhysicalY = 0
        };

        for (int i = 0; i < childCount; i++)
        {
            var child = new Component(
                new Dictionary<int, CAP_Core.LightCalculation.SMatrix>(),
                new List<Slider>(),
                "test_component",
                "",
                new Part[1, 1] { { new Part() } },
                -1,
                $"comp_{i}_{Guid.NewGuid():N}",
                DiscreteRotation.R0,
                new List<PhysicalPin>
                {
                    new PhysicalPin
                    {
                        Name = "a0",
                        OffsetXMicrometers = 0,
                        OffsetYMicrometers = 0,
                        AngleDegrees = 180
                    }
                })
            {
                PhysicalX = i * 100,
                PhysicalY = 0,
                WidthMicrometers = 50,
                HeightMicrometers = 30
            };

            group.AddChild(child);
        }

        return group;
    }
}

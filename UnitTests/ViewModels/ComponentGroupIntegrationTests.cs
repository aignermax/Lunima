using CAP.Avalonia.ViewModels.Library;
using CAP_Core.Components.ComponentHelpers;
using CAP_Core.Components.Core;
using CAP_Core.Components.Connections;
using Shouldly;
using Xunit;

namespace UnitTests.ViewModels;

/// <summary>
/// Integration tests for ComponentGroup workflow (ViewModel + Core).
/// </summary>
public class ComponentGroupIntegrationTests : IDisposable
{
    private readonly string _testCatalogPath;
    private readonly ComponentGroupViewModel _viewModel;

    public ComponentGroupIntegrationTests()
    {
        _testCatalogPath = Path.Combine(Path.GetTempPath(), $"test-groups-{Guid.NewGuid()}.json");
        _viewModel = new ComponentGroupViewModel();
    }

    public void Dispose()
    {
        if (File.Exists(_testCatalogPath))
        {
            File.Delete(_testCatalogPath);
        }
    }

    [Fact]
    public void SaveGroup_UpdatesAvailableGroupsList()
    {
        // Arrange
        var initialCount = _viewModel.AvailableGroups.Count;
        var group = CreateTestGroup("Integration Test Group");

        // Act
        _viewModel.SaveGroup(group);

        // Assert
        _viewModel.AvailableGroups.Count.ShouldBe(initialCount + 1);
        _viewModel.AvailableGroups.Any(g => g.Name == "Integration Test Group").ShouldBeTrue();
        _viewModel.StatusText.ShouldContain("Saved group");
    }

    [Fact]
    public void DeleteSelectedGroup_RemovesFromList()
    {
        // Arrange
        var group = CreateTestGroup("To Delete");
        _viewModel.SaveGroup(group);
        var addedGroup = _viewModel.AvailableGroups.First(g => g.Name == "To Delete");
        _viewModel.SelectedGroup = addedGroup;
        var countBefore = _viewModel.AvailableGroups.Count;

        // Act
        _viewModel.DeleteSelectedGroupCommand.Execute(null);

        // Assert
        _viewModel.AvailableGroups.Count.ShouldBe(countBefore - 1);
        _viewModel.AvailableGroups.Any(g => g.Name == "To Delete").ShouldBeFalse();
        _viewModel.StatusText.ShouldContain("Deleted");
    }

    [Fact]
    public void ComponentGroupInfo_DisplaysCorrectMetadata()
    {
        // Arrange
        var group = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = "Test Display",
            Category = "Test Category",
            Description = "Test Description",
            Components = new List<ComponentGroupMember>
            {
                new ComponentGroupMember { LocalId = 0, TemplateName = "MMI1" },
                new ComponentGroupMember { LocalId = 1, TemplateName = "MMI2" }
            },
            Connections = new List<GroupConnection>
            {
                new GroupConnection { SourceComponentId = 0, TargetComponentId = 1 }
            },
            WidthMicrometers = 300,
            HeightMicrometers = 200,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var info = new ComponentGroupInfo(group);

        // Assert
        info.Name.ShouldBe("Test Display");
        info.Category.ShouldBe("Test Category");
        info.ComponentCount.ShouldBe(2);
        info.ConnectionCount.ShouldBe(1);
        info.WidthMicrometers.ShouldBe(300);
        info.HeightMicrometers.ShouldBe(200);
        info.CreatedAtText.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void PlaceSelectedGroup_WithNoSelection_UpdatesStatus()
    {
        // Arrange
        _viewModel.SelectedGroup = null;

        // Act
        _viewModel.PlaceSelectedGroupCommand.Execute(null);

        // Assert
        _viewModel.StatusText.ShouldContain("No group selected");
    }

    [Fact]
    public void CreateGroup_WithEmptyName_ShowsError()
    {
        // Arrange
        _viewModel.NewGroupName = "";

        // Act
        _viewModel.CreateGroupCommand.Execute(null);

        // Assert
        _viewModel.StatusText.ShouldContain("Error");
        _viewModel.StatusText.ShouldContain("empty");
    }

    [Fact]
    public void CreateGroup_WithValidName_InvokesCallback()
    {
        // Arrange
        string? capturedName = null;
        string? capturedCategory = null;
        string? capturedDescription = null;

        _viewModel.OnCreateGroupFromSelection = (name, category, desc) =>
        {
            capturedName = name;
            capturedCategory = category;
            capturedDescription = desc;
        };

        _viewModel.NewGroupName = "Test Group";
        _viewModel.NewGroupCategory = "Test Category";
        _viewModel.NewGroupDescription = "Test Description";

        // Act
        _viewModel.CreateGroupCommand.Execute(null);

        // Assert
        capturedName.ShouldBe("Test Group");
        capturedCategory.ShouldBe("Test Category");
        capturedDescription.ShouldBe("Test Description");

        // Fields should be cleared
        _viewModel.NewGroupName.ShouldBe("");
        _viewModel.NewGroupCategory.ShouldBe("User Defined");
        _viewModel.NewGroupDescription.ShouldBe("");
    }

    [Fact]
    public void RefreshGroups_LoadsPersistedGroups()
    {
        // Arrange - Save a group first
        var group = CreateTestGroup("Persistent");
        _viewModel.SaveGroup(group);

        // Act - Refresh
        _viewModel.RefreshGroupsCommand.Execute(null);

        // Assert
        _viewModel.AvailableGroups.Any(g => g.Name == "Persistent").ShouldBeTrue();
    }

    [Fact]
    public void StatusText_UpdatesWithGroupCount()
    {
        // Arrange
        var initialStatus = _viewModel.StatusText;

        // Act
        var group = CreateTestGroup("Status Test");
        _viewModel.SaveGroup(group);

        // Assert
        _viewModel.StatusText.ShouldNotBe(initialStatus);
        _viewModel.StatusText.ShouldContain("group");
    }

    private ComponentGroup CreateTestGroup(string name)
    {
        return new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            Category = "Test",
            Description = "Test group for integration testing",
            WidthMicrometers = 200,
            HeightMicrometers = 150,
            Components = new List<ComponentGroupMember>
            {
                new ComponentGroupMember
                {
                    LocalId = 0,
                    TemplateName = "1x2 MMI Splitter",
                    RelativeX = 0,
                    RelativeY = 0,
                    Rotation = DiscreteRotation.R0
                }
            },
            Connections = new List<GroupConnection>(),
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow
        };
    }
}

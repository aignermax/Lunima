using CAP.Avalonia.ViewModels;
using Shouldly;
using System.Text.Json;
using UnitTests.Helpers;
using Xunit;

namespace UnitTests.Serialization;

/// <summary>
/// Tests that lock state is correctly saved and restored when persisting designs.
/// </summary>
public class LockStatePersistenceTests
{
    [Fact]
    public void SaveDesign_WithLockedComponents_SerializesIsLocked()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;
        comp1.IsLocked = true;
        comp2.IsLocked = false;

        canvas.AddComponent(comp1, "Test1");
        canvas.AddComponent(comp2, "Test2");

        // Act - serialize to JSON
        var designData = new DesignFileData
        {
            Components = canvas.Components.Select(c => new ComponentData
            {
                TemplateName = c.TemplateName ?? c.Name,
                X = c.X,
                Y = c.Y,
                Identifier = c.Component.Identifier,
                Rotation = (int)c.Component.Rotation90CounterClock,
                IsLocked = c.Component.IsLocked ? true : null
            }).ToList()
        };

        var json = JsonSerializer.Serialize(designData, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        // Assert
        json.ShouldContain("\"IsLocked\": true");
        json.ShouldNotContain("\"IsLocked\": false"); // null values are not serialized
    }

    [Fact]
    public void SaveDesign_WithLockedConnections_SerializesIsLocked()
    {
        // Arrange
        var canvas = new DesignCanvasViewModel();
        var comp1 = TestComponentFactory.CreateBasicComponent();
        var comp2 = TestComponentFactory.CreateBasicComponent();
        comp1.PhysicalX = 0;
        comp1.PhysicalY = 0;
        comp2.PhysicalX = 500;
        comp2.PhysicalY = 0;

        canvas.AddComponent(comp1, "Test1");
        canvas.AddComponent(comp2, "Test2");

        var connection = TestComponentFactory.CreateConnection(comp1, comp2);
        connection.IsLocked = true;
        canvas.ConnectionManager.AddExistingConnection(connection);
        canvas.Connections.Add(new WaveguideConnectionViewModel(connection));

        // Act - serialize to JSON
        var designData = new DesignFileData
        {
            Connections = canvas.Connections.Select(c => new ConnectionData
            {
                StartComponentIndex = canvas.Components.ToList().FindIndex(
                    comp => comp.Component == c.Connection.StartPin.ParentComponent),
                StartPinName = c.Connection.StartPin.Name,
                EndComponentIndex = canvas.Components.ToList().FindIndex(
                    comp => comp.Component == c.Connection.EndPin.ParentComponent),
                EndPinName = c.Connection.EndPin.Name,
                IsLocked = c.Connection.IsLocked ? true : null
            }).ToList()
        };

        var json = JsonSerializer.Serialize(designData, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        // Assert
        json.ShouldContain("\"IsLocked\": true");
    }

    [Fact]
    public void LoadDesign_WithLockedComponents_RestoresIsLocked()
    {
        // Arrange
        var json = @"{
            ""Components"": [
                {
                    ""TemplateName"": ""Test1"",
                    ""X"": 0,
                    ""Y"": 0,
                    ""Identifier"": ""comp1"",
                    ""Rotation"": 0,
                    ""IsLocked"": true
                },
                {
                    ""TemplateName"": ""Test2"",
                    ""X"": 500,
                    ""Y"": 0,
                    ""Identifier"": ""comp2"",
                    ""Rotation"": 0
                }
            ],
            ""Connections"": []
        }";

        var designData = JsonSerializer.Deserialize<DesignFileData>(json);

        // Assert
        designData.ShouldNotBeNull();
        designData.Components.Count.ShouldBe(2);
        designData.Components[0].IsLocked.ShouldBe(true);
        designData.Components[1].IsLocked.ShouldBeNull(); // null for unlocked
    }

    [Fact]
    public void LoadDesign_WithLockedConnections_RestoresIsLocked()
    {
        // Arrange
        var json = @"{
            ""Components"": [],
            ""Connections"": [
                {
                    ""StartComponentIndex"": 0,
                    ""StartPinName"": ""pin1"",
                    ""EndComponentIndex"": 1,
                    ""EndPinName"": ""pin2"",
                    ""IsLocked"": true
                }
            ]
        }";

        var designData = JsonSerializer.Deserialize<DesignFileData>(json);

        // Assert
        designData.ShouldNotBeNull();
        designData.Connections.Count.ShouldBe(1);
        designData.Connections[0].IsLocked.ShouldBe(true);
    }
}

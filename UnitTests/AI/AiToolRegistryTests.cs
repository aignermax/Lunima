using System.Text.Json;
using CAP.Avalonia.Services.AiTools;
using Moq;
using Shouldly;

namespace UnitTests.AI;

/// <summary>
/// Unit tests for <see cref="AiToolRegistry"/>.
/// </summary>
public class AiToolRegistryTests
{
    private static IAiTool MakeTool(string name) =>
        Mock.Of<IAiTool>(t =>
            t.Name == name &&
            t.Description == $"Description for {name}" &&
            t.InputSchema == (object)new { type = "object" });

    [Fact]
    public void GetTool_RegisteredName_ReturnsTool()
    {
        var tool = MakeTool("my_tool");
        var registry = new AiToolRegistry(new[] { tool });

        registry.GetTool("my_tool").ShouldBe(tool);
    }

    [Fact]
    public void GetTool_UnknownName_ReturnsNull()
    {
        var registry = new AiToolRegistry(Array.Empty<IAiTool>());

        registry.GetTool("nonexistent").ShouldBeNull();
    }

    [Fact]
    public void GetTool_IsCaseInsensitive()
    {
        var tool = MakeTool("Copy_Component");
        var registry = new AiToolRegistry(new[] { tool });

        registry.GetTool("copy_component").ShouldBe(tool);
        registry.GetTool("COPY_COMPONENT").ShouldBe(tool);
    }

    [Fact]
    public void GetAllTools_ReturnsAllRegisteredTools()
    {
        var tools = new[] { MakeTool("tool_a"), MakeTool("tool_b"), MakeTool("tool_c") };
        var registry = new AiToolRegistry(tools);

        registry.GetAllTools().Count.ShouldBe(3);
    }

    [Fact]
    public void GetAllTools_EmptyRegistry_ReturnsEmptyList()
    {
        var registry = new AiToolRegistry(Array.Empty<IAiTool>());

        registry.GetAllTools().ShouldBeEmpty();
    }

    [Fact]
    public void GetAllTools_ToolNamesMatchRegistered()
    {
        var tools = new[] { MakeTool("alpha"), MakeTool("beta") };
        var registry = new AiToolRegistry(tools);

        var names = registry.GetAllTools().Select(t => t.Name).ToHashSet();
        names.ShouldContain("alpha");
        names.ShouldContain("beta");
    }

    [Fact]
    public async Task Tool_ExecuteAsync_DelegatesCallToImplementation()
    {
        var mockTool = new Mock<IAiTool>();
        mockTool.Setup(t => t.Name).Returns("test_tool");
        mockTool.Setup(t => t.ExecuteAsync(It.IsAny<JsonElement>())).ReturnsAsync("ok");

        var registry = new AiToolRegistry(new[] { mockTool.Object });
        var tool = registry.GetTool("test_tool")!;

        using var doc = JsonDocument.Parse("{}");
        var result = await tool.ExecuteAsync(doc.RootElement);

        result.ShouldBe("ok");
    }

    [Fact]
    public void Registry_ToolDefinitions_ExposedViaGetAllTools()
    {
        var tool = MakeTool("place_component");
        var registry = new AiToolRegistry(new[] { tool });

        var found = registry.GetAllTools().First();
        found.Name.ShouldBe("place_component");
        found.Description.ShouldBe("Description for place_component");
    }
}

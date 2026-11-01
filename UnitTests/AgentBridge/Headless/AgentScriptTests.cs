using System.IO;
using System.Linq;
using System.Text.Json;
using CAP.Avalonia.Services.AgentBridge.Headless;
using CAP.Avalonia.Services.AgentBridge.Tools;
using Shouldly;
using Xunit;

namespace UnitTests.AgentBridge.Headless;

/// <summary>Script parsing for <c>lunima agent run</c> and the CLI's JSON result output.</summary>
public class AgentScriptTests
{
    [Fact]
    public void Steps_KeepOrder_AndDefaultArguments()
    {
        AgentScript.TryParse("""[{"tool":"a","arguments":{"x":1}},{"tool":"b"}]""", out var steps, out var error)
            .ShouldBeTrue(error);

        steps.Select(s => s.Tool).ShouldBe(new[] { "a", "b" });
        steps[0].Arguments.GetProperty("x").GetInt32().ShouldBe(1);
        steps[1].Arguments.ValueKind.ShouldBe(JsonValueKind.Object);
    }

    [Theory]
    [InlineData("{}", "array")]
    [InlineData("[{\"arguments\":{}}]", "Step 0")]
    [InlineData("[{\"tool\":\"a\",\"arguments\":[]}]", "must be an object")]
    [InlineData("[", "not valid JSON")]
    public void Malformed_ExplainsWhy(string json, string reason)
    {
        AgentScript.TryParse(json, out _, out var error).ShouldBeFalse();
        error.ShouldContain(reason);
    }

    [Fact]
    public void ResultWriter_EmbedsJsonText_AsJson()
    {
        var writer = new AgentCliResultWriter(Path.GetTempPath());

        var output = writer.Describe("get_design_summary", AgentToolResult.Json(new { ComponentCount = 3 }));

        output["is_error"]!.GetValue<bool>().ShouldBeFalse();
        output["result"]!["component_count"]!.GetValue<int>().ShouldBe(3);
    }

    [Fact]
    public void ResultWriter_KeepsPlainText_AsString()
    {
        var output = new AgentCliResultWriter(Path.GetTempPath())
            .Describe("undo", AgentToolResult.Error("Nothing to undo."));

        output["is_error"]!.GetValue<bool>().ShouldBeTrue();
        output["result"]!.GetValue<string>().ShouldBe("Nothing to undo.");
    }

    [Fact]
    public void ResultWriter_WritesImages_ToFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lunima-cli-test-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var output = new AgentCliResultWriter(directory)
                .Describe("screenshot", AgentToolResult.Png(new byte[] { 1, 2, 3 }, "caption"));

            var path = output["images"]![0]!.GetValue<string>();
            File.ReadAllBytes(path).ShouldBe(new byte[] { 1, 2, 3 });
            output["result"]!.GetValue<string>().ShouldBe("caption");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}

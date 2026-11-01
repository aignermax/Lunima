using System.Text.Json;
using System.Text.Json.Nodes;
using CAP.Avalonia.Services.AgentBridge.Tools;

namespace CAP.Avalonia.Services.AgentBridge.Headless;

/// <summary>
/// Turns tool results into the CLI's JSON output. A text block that holds JSON is embedded as
/// JSON (not as an escaped string) so the output can be piped straight into <c>jq</c>; image
/// blocks are written to PNG files and reported by path instead of as base64.
/// </summary>
internal sealed class AgentCliResultWriter
{
    private readonly string _imageDirectory;
    private int _imageCount;

    /// <summary>Creates the writer; images go to <paramref name="imageDirectory"/> (a temp folder when null).</summary>
    public AgentCliResultWriter(string? imageDirectory)
    {
        _imageDirectory = imageDirectory
            ?? Path.Combine(Path.GetTempPath(), "lunima-agent-images");
    }

    /// <summary>One step's output object: tool, error flag, result content and image paths.</summary>
    public JsonObject Describe(string tool, AgentToolResult result)
    {
        var texts = new List<JsonNode?>();
        var images = new JsonArray();
        foreach (var block in result.Content)
        {
            if (block.Type == "image" && block.Data != null)
                images.Add(WriteImage(tool, block));
            else if (block.Text != null)
                texts.Add(ParseOrText(block.Text));
        }

        var output = new JsonObject
        {
            ["tool"] = tool,
            ["is_error"] = result.IsError,
            ["result"] = texts.Count == 1 ? texts[0] : new JsonArray(texts.ToArray()),
        };
        if (images.Count > 0)
            output["images"] = images;
        return output;
    }

    private string WriteImage(string tool, AgentToolContent block)
    {
        Directory.CreateDirectory(_imageDirectory);
        var path = Path.Combine(_imageDirectory,
            $"{tool}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{++_imageCount}.png");
        File.WriteAllBytes(path, Convert.FromBase64String(block.Data!));
        return path;
    }

    private static JsonNode? ParseOrText(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try { return JsonNode.Parse(text); }
            catch (JsonException) { }
        }
        return JsonValue.Create(text);
    }
}

using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CAP.Avalonia.Services;

/// <summary>
/// Claude (Anthropic) API integration for the in-app AI Design Assistant.
/// Supports plain conversation and tool-calling (function calling) for grid manipulation.
/// </summary>
public class AiService : IAiService
{
    private readonly HttpClient _httpClient;
    private string _apiKey = "";

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ModelId = "claude-sonnet-4-5";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxTokens = 2048;
    private const int MaxToolLoopIterations = 10;

    private const string SystemPrompt =
        "You are an AI assistant integrated into Lunima (Connect-A-PIC-Pro), a photonic integrated circuit design tool. " +
        "You help users design photonic circuits using natural language. " +
        "You can explain photonic concepts, suggest designs, and directly manipulate the circuit grid using tools. " +
        "Chip coordinate space: 0–5000 µm in both X and Y. Leave 50–100 µm spacing between components for routing. " +
        "When building circuits, place components first, then connect them. " +
        "After using tools, summarize what was done concisely. Keep responses under 200 words.";

    /// <inheritdoc/>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>
    /// Initializes the AI service with a shared HTTP client.
    /// </summary>
    public AiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc/>
    public void SetApiKey(string apiKey)
    {
        _apiKey = apiKey ?? "";
    }

    /// <inheritdoc/>
    public async Task<string> SendMessageAsync(
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return "Please configure your Claude API key in the AI Assistant settings below.";

        var messages = BuildSimpleMessageList(userMessage, history);
        var requestBody = new { model = ModelId, max_tokens = MaxTokens, system = SystemPrompt, messages };
        var json = JsonSerializer.Serialize(requestBody);
        using var request = BuildHttpRequest(json);

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            var responseJson = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return $"API error ({(int)response.StatusCode}): Check your API key and try again.";
            return ParseFirstTextBlock(responseJson);
        }
        catch (OperationCanceledException) { throw; }
        catch (HttpRequestException ex) { return $"Network error: {ex.Message}"; }
        catch (Exception ex) { return $"Unexpected error: {ex.Message}"; }
    }

    /// <inheritdoc/>
    public async Task<string> SendMessageWithToolsAsync(
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history,
        IReadOnlyList<string> availableComponentTypes,
        Func<string, string, Task<string>> toolExecutor,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return "Please configure your Claude API key in the AI Assistant settings below.";

        var messages = BuildSimpleMessageList(userMessage, history);
        var tools = BuildToolDefinitions(availableComponentTypes);

        for (int iteration = 0; iteration < MaxToolLoopIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            var requestBody = new
            {
                model = ModelId,
                max_tokens = MaxTokens,
                system = SystemPrompt,
                tools,
                messages
            };

            string responseJson;
            try
            {
                var json = JsonSerializer.Serialize(requestBody);
                using var request = BuildHttpRequest(json);
                var response = await _httpClient.SendAsync(request, ct);
                responseJson = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                    return $"API error ({(int)response.StatusCode}): Check your API key and try again.";
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpRequestException ex) { return $"Network error: {ex.Message}"; }

            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : "";

            if (stopReason != "tool_use")
                return ParseFirstTextBlock(responseJson);

            // Add full assistant message (with tool_use blocks) to history
            var contentElement = root.GetProperty("content");
            messages.Add(new { role = "assistant", content = (object)contentElement.Clone() });

            // Execute each tool call and collect results
            var toolResults = new List<object>();
            foreach (var block in contentElement.EnumerateArray())
            {
                if (block.GetProperty("type").GetString() != "tool_use") continue;

                var toolId = block.GetProperty("id").GetString()!;
                var toolName = block.GetProperty("name").GetString()!;
                var toolInput = block.GetProperty("input").GetRawText();

                var result = await toolExecutor(toolName, toolInput);
                toolResults.Add(new { type = "tool_result", tool_use_id = toolId, content = result });
            }

            messages.Add(new { role = "user", content = (object)toolResults });
        }

        return "Error: Tool calling loop exceeded maximum iterations.";
    }

    private static List<object> BuildSimpleMessageList(
        string userMessage,
        IReadOnlyList<(string Role, string Content)> history)
    {
        var messages = history
            .Select(h => (object)new { role = h.Role, content = h.Content })
            .ToList();
        messages.Add(new { role = "user", content = userMessage });
        return messages;
    }

    private HttpRequestMessage BuildHttpRequest(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        return request;
    }

    private static string ParseFirstTextBlock(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.GetProperty("type").GetString() == "text")
                    return block.GetProperty("text").GetString() ?? "No response received.";
            }
            return "No text response received.";
        }
        catch
        {
            return "Could not parse AI response.";
        }
    }

    private static object[] BuildToolDefinitions(IReadOnlyList<string> componentTypes)
    {
        var typesList = componentTypes.Count > 0
            ? string.Join(", ", componentTypes)
            : "Grating Coupler, Straight Waveguide, MMI 1x2";

        return new object[]
        {
            new
            {
                name = "get_grid_state",
                description = "Get current components and connections on the design grid",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "place_component",
                description = $"Place a component on the photonic circuit grid. Available types: {typesList}",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        component_type = new { type = "string", description = "Component type name (case-insensitive)" },
                        x = new { type = "number", description = "Center X position in micrometers (0–5000)" },
                        y = new { type = "number", description = "Center Y position in micrometers (0–5000)" },
                        rotation = new
                        {
                            type = "integer",
                            description = "Rotation in degrees",
                            @enum = new[] { 0, 90, 180, 270 }
                        }
                    },
                    required = new[] { "component_type", "x", "y" }
                }
            },
            new
            {
                name = "create_connection",
                description = "Connect two components with a waveguide (auto-selects first available pins)",
                input_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        from_component = new { type = "string", description = "Source component ID" },
                        to_component = new { type = "string", description = "Target component ID" }
                    },
                    required = new[] { "from_component", "to_component" }
                }
            },
            new
            {
                name = "clear_grid",
                description = "Remove all components and connections from the design grid",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "start_simulation",
                description = "Run photonic S-Matrix simulation and activate the power flow overlay",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "stop_simulation",
                description = "Stop simulation and hide the power flow overlay",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            },
            new
            {
                name = "get_light_values",
                description = "Get current light power values for all waveguide connections (requires simulation)",
                input_schema = new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }
            }
        };
    }
}
